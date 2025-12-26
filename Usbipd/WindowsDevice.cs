// SPDX-FileCopyrightText: 2025 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Usbipd.Automation;
using Windows.Win32;
using Windows.Win32.Devices.DeviceAndDriverInstallation;
using Windows.Win32.Devices.Properties;
using Windows.Win32.Foundation;

namespace Usbipd;

/// <summary>
/// Represents a device as seen by Windows.
/// <para>
/// Each device has a numeric ID (device node) and an instance ID (string), which have a one-to-one correspondence.
/// </para>
/// </summary>
sealed partial class WindowsDevice : IEquatable<WindowsDevice>
{
    public uint Node { get; }
    // Instance IDs are case-insensitive. We always use uppercase to prevent confusion and comparison issues.
    public string InstanceId { get; }

    WindowsDevice(uint deviceNode, string instanceId)
    {
        Node = deviceNode;
        InstanceId = instanceId.ToUpperInvariant();
    }

    #region IEquatable
    public override int GetHashCode()
    {
        return InstanceId.GetHashCode(StringComparison.Ordinal);
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as WindowsDevice);
    }

    public bool Equals(WindowsDevice? other)
    {
        return other is not null && InstanceId == other.InstanceId;
    }
    #endregion

    /// <returns>false if the device does not exist (neither present nor phantom).</returns>
    public static bool TryCreate(string instanceId, out WindowsDevice device)
    {
        uint deviceNode;
        device = default!;
        unsafe // DevSkim: ignore DS172412
        {
            fixed (char* pInstanceId = instanceId)
            {
                if (PInvoke.CM_Locate_DevNode(out deviceNode, pInstanceId, CM_LOCATE_DEVNODE_FLAGS.CM_LOCATE_DEVNODE_PHANTOM) != CONFIGRET.CR_SUCCESS)
                {
                    return false;
                }
            }
        }
        device = new(deviceNode, instanceId);
        return true;
    }

    /// <summary>
    /// true if the device is a VBoxUSBMon stub device.
    /// </summary>
    public bool IsStub => VidPid.TryParseId(InstanceId, out var vidPid) && (vidPid == DriverDetails.Instance.VidPid);

    /// <summary>
    /// true if the device is a USB hub.
    /// </summary>
    public bool IsHub
    {
        get
        {
            uint length;
            unsafe // DevSkim: ignore DS172412
            {
                fixed (char* pInstanceId = InstanceId)
                {
                    if (PInvoke.CM_Get_Device_Interface_List_Size(out length, PInvoke.GUID_DEVINTERFACE_USB_HUB, pInstanceId,
                        CM_GET_DEVICE_INTERFACE_LIST_FLAGS.CM_GET_DEVICE_INTERFACE_LIST_ALL_DEVICES) != CONFIGRET.CR_SUCCESS)
                    {
                        return false;
                    }
                }
            }
            // A non-empty list (i.e., for a hub) would be double-NUL terminated.
            return length >= 2;
        }
    }

    /// <summary>
    /// true if the device is currently present (plugged in).
    /// </summary>
    public bool IsPresent => TryGetProperty(Node, PInvoke.DEVPKEY_Device_IsPresent, out bool isPresent) && isPresent;

    /// <summary>
    /// true if the device is currently present (plugged in), but disabled in Device Manager.
    /// </summary>
    public bool IsDisabled => IsPresent
        && TryGetProperty(Node, PInvoke.DEVPKEY_Device_DevNodeStatus, out CM_DEVNODE_STATUS_FLAGS status)
        && status.HasFlag(CM_DEVNODE_STATUS_FLAGS.DN_HAS_PROBLEM)
        && TryGetProperty(Node, PInvoke.DEVPKEY_Device_ProblemCode, out CM_PROB problem)
        && problem == CM_PROB.CM_PROB_DISABLED;

    public const string UnknownDescription = "Unknown device";

    /// <returns>An amalgamated "best" description of the device.</returns>
    public string Description
    {
        get
        {
            var isCompositeDevice = TryGetProperty(Node, PInvoke.DEVPKEY_Device_CompatibleIds, out string[] compatibleIds)
                && compatibleIds.Contains(@"USB\COMPOSITE");
            if (!isCompositeDevice)
            {
                // NAME is FriendlyName (if it exists), or else it is Description (if it exists).
                if (!TryGetProperty(Node, PInvoke.DEVPKEY_NAME, out string name))
                {
                    // This can happen for devices without a driver *and* without a USB string table.
                    return UnknownDescription;
                }
                name = name.Trim();
                return string.IsNullOrEmpty(name) ? UnknownDescription : name;
            }

            // For USB\COMPOSITE we need to add the descriptions of the direct children.
            // We remove duplicates, often something like "HID Device", but we also want to preserve the order.
            var descriptionSet = new SortedSet<string>();
            var descriptionList = new List<string>();

            void AddDescription(string desc)
            {
                desc = desc.Trim();
                if (!string.IsNullOrEmpty(desc) && descriptionSet.Add(desc))
                {
                    descriptionList.Add(desc);
                }
            }

            // The FriendlyName (if it exists) is useful for USB\COMPOSITE.
            // Description definitely is not (for composite devices).
            if (TryGetProperty(Node, PInvoke.DEVPKEY_Device_FriendlyName, out string friendlyName))
            {
                AddDescription(friendlyName);
            }

            foreach (var childDevice in Children)
            {
                // NAME is FriendlyName (if it exists), or else it is Description (if it exists).
                if (TryGetProperty(childDevice.Node, PInvoke.DEVPKEY_NAME, out string childName))
                {
                    AddDescription(childName);
                }
            }

            if (descriptionList.Count == 0)
            {
                // This can happen if the USB\COMPOSITE device does not have a FriendlyName and the
                // bus has not yet enumerated the children; for example, right after releasing the device
                // back to Windows. Just fall back to the non-descriptive name for USB\COMPOSITE.
                if (!TryGetProperty(Node, PInvoke.DEVPKEY_NAME, out string name))
                {
                    // This can happen for devices without a driver *and* without a USB string table.
                    return UnknownDescription;
                }
                name = name.Trim();
                return string.IsNullOrEmpty(name) ? UnknownDescription : name;
            }
            else
            {
                return string.Join(", ", descriptionList);
            }
        }
    }

    /// <returns>true on success.</returns>
    public bool SetFriendlyName()
    {
        // NOTE: Must include terminating NUL character.
        return PInvoke.CM_Set_DevNode_Property(Node, PInvoke.DEVPKEY_Device_FriendlyName, DEVPROPTYPE.DEVPROP_TYPE_STRING,
            Encoding.Unicode.GetBytes("USBIP Shared Device\0"), 0) == CONFIGRET.CR_SUCCESS;
    }

    [GeneratedRegex(@"^Port_#([0-9]{4}).Hub_#([0-9]{4})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LocationInfoRegex();

    /// <summary>
    /// The USB bus ID of the device, or <see cref="BusId.IncompatibleHub"/> for non-USB devices, hubs, phantom devices, etc.
    /// <para>
    /// NOTE: This can have a valid value even if the device is currently not present.
    /// </para>
    /// </summary>
    public BusId BusId
    {
        get
        {
            if (!TryGetProperty(Node, PInvoke.DEVPKEY_Device_LocationInfo, out string locationInfo))
            {
                return BusId.IncompatibleHub;
            }
            var match = LocationInfoRegex().Match(locationInfo);
            if (!match.Success)
            {
                // This is probably a device on an unsupported hub-type.
                // See for example https://github.com/dorssel/usbipd-win/issues/809.
                return BusId.IncompatibleHub;
            }
            var bus = ushort.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
            var port = ushort.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            return bus == 0 || port == 0 ? BusId.IncompatibleHub : new(bus, port);
        }
    }

    /// <summary>
    /// true if the device is using the VBoxUSB driver (i.e., either a stub device, or a forced-bound device).
    /// </summary>
    public bool HasVBoxDriver => TryGetProperty(Node, PInvoke.DEVPKEY_Device_MatchingDeviceId, out string matchingDeviceId)
        && VidPid.TryParseId(matchingDeviceId, out var vidPid) && vidPid == DriverDetails.Instance.VidPid;

    /// <summary>
    /// The current driver version.
    /// <para>
    /// NOTE: This is only interesting if HasVBoxDriver is true.
    /// </para>
    /// </summary>
    public Version DriverVersion => (TryGetProperty(Node, PInvoke.DEVPKEY_Device_DriverVersion, out string versionText)
        && Version.TryParse(versionText, out var version)) ? version : new();

    public IEnumerable<WindowsDevice> Children
    {
        get
        {
            if (!TryGetProperty(Node, PInvoke.DEVPKEY_Device_Children, out string[] childInstanceIds))
            {
                yield break;
            }
            foreach (var childInstanceId in childInstanceIds)
            {
                if (TryCreate(childInstanceId, out var childDevice))
                {
                    yield return childDevice;
                }
            }
        }
    }

    static DeviceFile OpenInterface(string instanceId, Guid interfaceClassGuid)
    {
        uint bufferSize;
        unsafe // DevSkim: ignore DS172412
        {
            fixed (char* pInstanceId = instanceId)
            {
                if (PInvoke.CM_Get_Device_Interface_List_Size(out bufferSize, interfaceClassGuid, pInstanceId,
                    CM_GET_DEVICE_INTERFACE_LIST_FLAGS.CM_GET_DEVICE_INTERFACE_LIST_PRESENT) != CONFIGRET.CR_SUCCESS)
                {
                    throw new FileNotFoundException();
                }
            }
        }
        if (bufferSize <= 1)
        {
            throw new FileNotFoundException();
        }
        var buffer = new char[checked((int)bufferSize)];
        unsafe // DevSkim: ignore DS172412
        {
            fixed (char* pInstanceId = instanceId)
            fixed (char* pBuffer = buffer)
            {
                if (PInvoke.CM_Get_Device_Interface_List(interfaceClassGuid, pInstanceId, pBuffer, bufferSize,
                    CM_GET_DEVICE_INTERFACE_LIST_FLAGS.CM_GET_DEVICE_INTERFACE_LIST_PRESENT) != CONFIGRET.CR_SUCCESS)
                {
                    throw new FileNotFoundException();
                }
            }
        }
        // The "list" contains one entry and is double-NUL terminated.
        var interfacePath = new string(buffer.AsSpan()[0..^2]);
        return new(interfacePath);
    }

    public DeviceFile OpenVBoxInterface()
    {
        return OpenInterface(InstanceId, Interop.VBoxUsb.GUID_CLASS_VBOXUSB);
    }

    public DeviceFile OpenHubInterface()
    {
        return TryGetProperty(Node, PInvoke.DEVPKEY_Device_Parent, out string hubInstanceId)
                && TryCreate(hubInstanceId, out var hubDevice)
            ? OpenInterface(hubDevice.InstanceId, PInvoke.GUID_DEVINTERFACE_USB_HUB)
            : throw new FileNotFoundException();
    }

    static bool TryGetProperty(uint deviceNode, in DEVPROPKEY devPropKey, out byte[] value, out DEVPROPTYPE propertyType)
    {
        var bufferSize = 0u;
        if (PInvoke.CM_Get_DevNode_Property(deviceNode, devPropKey, out _, null, ref bufferSize, 0) != CONFIGRET.CR_BUFFER_SMALL)
        {
            value = default!;
            propertyType = DEVPROPTYPE.DEVPROP_TYPE_EMPTY;
            return false;
        }
        var buffer = new byte[checked((int)bufferSize)];
        var bufferSizeConfirm = bufferSize;
        if (PInvoke.CM_Get_DevNode_Property(deviceNode, devPropKey, out propertyType, buffer, ref bufferSizeConfirm, 0) != CONFIGRET.CR_SUCCESS)
        {
            value = default!;
            propertyType = DEVPROPTYPE.DEVPROP_TYPE_EMPTY;
            return false;
        }
        if (bufferSizeConfirm != bufferSize)
        {
            value = default!;
            propertyType = DEVPROPTYPE.DEVPROP_TYPE_EMPTY;
            return false;
        }
        value = buffer;
        return true;
    }

    static bool TryGetProperty(uint deviceNode, in DEVPROPKEY devPropKey, out string value)
    {
        if (!TryGetProperty(deviceNode, devPropKey, out var buffer, out var propertyType))
        {
            value = default!;
            return false;
        }

        // Must be a UTF-16 string.
        if ((propertyType != DEVPROPTYPE.DEVPROP_TYPE_STRING) || (buffer.Length % sizeof(char) != 0))
        {
            value = default!;
            return false;
        }
        var charBuffer = MemoryMarshal.Cast<byte, char>(buffer);

        // Must be NUL terminated.
        if (charBuffer.IsEmpty || (charBuffer[^1] != '\0'))
        {
            value = default!;
            return false;
        }

        value = new(charBuffer[..^1]);
        return true;
    }

    static bool TryGetProperty(uint deviceNode, in DEVPROPKEY devPropKey, out string[] value)
    {
        if (!TryGetProperty(deviceNode, devPropKey, out var buffer, out var propertyType))
        {
            value = default!;
            return false;
        }

        // Must be a UTF-16 string list.
        if ((propertyType != DEVPROPTYPE.DEVPROP_TYPE_STRING_LIST) || (buffer.Length % sizeof(char) != 0))
        {
            value = default!;
            return false;
        }
        var charBuffer = MemoryMarshal.Cast<byte, char>(buffer);

        // Must be NUL terminated.
        if (charBuffer.IsEmpty || (charBuffer[^1] != '\0'))
        {
            value = default!;
            return false;
        }

        if (charBuffer.Length == 1)
        {
            // Empty list.
            value = [];
            return true;
        }

        // Non-empty list must be double-NUL terminated.
        if (charBuffer[^2] != '\0')
        {
            value = default!;
            return false;
        }

        value = new string(charBuffer[..^2]).Split('\0');
        return true;
    }

    static bool TryGetProperty(uint deviceNode, in DEVPROPKEY devPropKey, out uint value)
    {
        if (!TryGetProperty(deviceNode, devPropKey, out var buffer, out var propertyType))
        {
            value = default!;
            return false;
        }

        if ((propertyType != DEVPROPTYPE.DEVPROP_TYPE_UINT32) || (buffer.Length != sizeof(uint)))
        {
            value = default!;
            return false;
        }

        value = MemoryMarshal.Read<uint>(buffer);
        return true;
    }

    static bool TryGetProperty(uint deviceNode, in DEVPROPKEY devPropKey, out bool value)
    {
        if (!TryGetProperty(deviceNode, devPropKey, out var buffer, out var propertyType))
        {
            value = default!;
            return false;
        }

        if ((propertyType != DEVPROPTYPE.DEVPROP_TYPE_BOOLEAN) || (buffer.Length != Unsafe.SizeOf<DEVPROP_BOOLEAN>()))
        {
            value = default!;
            return false;
        }

        var boolean = (DEVPROP_BOOLEAN)buffer[0];
        if (boolean == DEVPROP_BOOLEAN.DEVPROP_TRUE)
        {
            value = true;
            return true;
        }
        else if (boolean == DEVPROP_BOOLEAN.DEVPROP_FALSE)
        {
            value = false;
            return true;
        }
        else
        {
            value = default!;
            return false;
        }
    }

    static bool TryGetProperty<T>(uint deviceNode, in DEVPROPKEY devPropKey, out T value) where T : unmanaged, Enum
    {
        if (!TryGetProperty(deviceNode, devPropKey, out uint tmpValue))
        {
            value = default;
            return false;
        }
        value = (T)Enum.ToObject(typeof(T), tmpValue);
        return true;
    }

    /// <returns>All devices, optionally filtered on installer class GUID and/or presence.</returns>
    public static IEnumerable<WindowsDevice> GetAll(Guid? classGuid, bool presentOnly)
    {
        var filter = classGuid?.ToString("B");
        uint flags = 0;
        if (classGuid is not null)
        {
            flags |= PInvoke.CM_GETIDLIST_FILTER_CLASS;
        }
        if (presentOnly)
        {
            flags |= PInvoke.CM_GETIDLIST_FILTER_PRESENT;
        }

        if (PInvoke.CM_Get_Device_ID_List_Size(out var bufferSize, filter, flags) != CONFIGRET.CR_SUCCESS)
        {
            yield break;
        }
        if (bufferSize <= 1)
        {
            // Empty list.
            yield break;
        }
        var buffer = new char[checked((int)bufferSize)];
        unsafe // DevSkim: ignore DS172412
        {
            fixed (char* pBuffer = buffer)
            {
                if (PInvoke.CM_Get_Device_ID_List(filter, pBuffer, bufferSize, flags) != CONFIGRET.CR_SUCCESS)
                {
                    yield break;
                }
            }
        }
        // The list is double-NUL terminated.
        var instanceIds = new string(buffer.AsSpan()[0..^2]).Split('\0');
        foreach (var instanceId in instanceIds)
        {
            if (TryCreate(instanceId, out var device))
            {
                yield return device;
            }
        }
    }

    /// <returns>All present devices supporting a specific interface class.</returns>
    public static IEnumerable<WindowsDevice> GetAll(Guid interfaceClassGuid)
    {
        string[] interfacePaths;
        {
            if (PInvoke.CM_Get_Device_Interface_List_Size(out var bufferSize, interfaceClassGuid, null,
                CM_GET_DEVICE_INTERFACE_LIST_FLAGS.CM_GET_DEVICE_INTERFACE_LIST_PRESENT) != CONFIGRET.CR_SUCCESS)
            {
                yield break;
            }
            if (bufferSize <= 1)
            {
                // Empty list.
                yield break;
            }
            var buffer = new char[checked((int)bufferSize)];
            unsafe // DevSkim: ignore DS172412
            {
                fixed (char* pBuffer = buffer)
                {
                    if (PInvoke.CM_Get_Device_Interface_List(interfaceClassGuid, null, pBuffer, bufferSize,
                        CM_GET_DEVICE_INTERFACE_LIST_FLAGS.CM_GET_DEVICE_INTERFACE_LIST_PRESENT) != CONFIGRET.CR_SUCCESS)
                    {
                        yield break;
                    }
                }
            }
            // The list is double-NUL terminated.
            interfacePaths = new string(buffer.AsSpan()[..^2]).Split('\0');
        }
        foreach (var interfacePath in interfacePaths)
        {
            var bufferSize = 0u;
            if (PInvoke.CM_Get_Device_Interface_Property(interfacePath, PInvoke.DEVPKEY_Device_InstanceId, out var propertyType, null,
                ref bufferSize, 0) != CONFIGRET.CR_BUFFER_SMALL)
            {
                continue;
            }
            // Must be a NUL-terminated UTF-16 string.
            if ((propertyType != DEVPROPTYPE.DEVPROP_TYPE_STRING)
                || (bufferSize % sizeof(char) != 0) || (bufferSize / sizeof(char) < 1))
            {
                continue;
            }
            var buffer = new byte[checked((int)bufferSize)];
            var bufferSizeConfirm = bufferSize;
            if (PInvoke.CM_Get_Device_Interface_Property(interfacePath, PInvoke.DEVPKEY_Device_InstanceId, out propertyType, buffer,
                ref bufferSizeConfirm, 0) != CONFIGRET.CR_SUCCESS)
            {
                continue;
            }
            if ((propertyType != DEVPROPTYPE.DEVPROP_TYPE_STRING) || (bufferSizeConfirm != bufferSize))
            {
                continue;
            }
            // The buffer includes the terminating NUL character.
            var instanceId = new string(MemoryMarshal.Cast<byte, char>(buffer)[..^1]);
            if (!TryCreate(instanceId, out var device))
            {
                continue;
            }
            yield return device;
        }
    }
}
