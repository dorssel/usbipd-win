// SPDX-FileCopyrightText: 2025 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;
using System.Text.RegularExpressions;
using Usbipd.Automation;
using Windows.Win32;
using Windows.Win32.Devices.DeviceAndDriverInstallation;
using Windows.Win32.Devices.Properties;

namespace Usbipd;

/// <summary>
/// Represents a device as seen by Windows.
/// <para>
/// Each device has a numeric ID (device node) and an instance ID (string), which have a one-to-one correspondence.
/// </para>
/// </summary>
sealed partial class WindowsDevice(uint deviceNode, string instanceId)
{
    public uint Node { get; } = deviceNode;
    public string InstanceId { get; } = instanceId;

    /// <returns>false if the corresponding instance ID does not exist.</returns>
    public static bool TryCreate(uint deviceNode, out WindowsDevice device)
    {
        if (!TryGetProperty(deviceNode, PInvoke.DEVPKEY_Device_InstanceId, out string instanceId))
        {
            device = default!;
            return false;
        }
        device = new(deviceNode, instanceId);
        return true;
    }

    /// <returns>false if the corresponding device node does not exist.</returns>
    public static bool TryCreate(string instanceId, out WindowsDevice device)
    {
        unsafe // DevSkim: ignore DS172412
        {
            fixed (char* pInstanceId = instanceId)
            {
                if (PInvoke.CM_Locate_DevNode(out var deviceNode, pInstanceId, CM_LOCATE_DEVNODE_FLAGS.CM_LOCATE_DEVNODE_PHANTOM) != CONFIGRET.CR_SUCCESS)
                {
                    device = default!;
                    return false;
                }
                device = new(deviceNode, instanceId);
                return true;
            }
        }
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
            unsafe // DevSkim: ignore DS172412
            {
                fixed (char* pInstanceId = InstanceId)
                {
                    if (PInvoke.CM_Get_Device_Interface_List_Size(out var length, PInvoke.GUID_DEVINTERFACE_USB_HUB, pInstanceId,
                        CM_GET_DEVICE_INTERFACE_LIST_FLAGS.CM_GET_DEVICE_INTERFACE_LIST_ALL_DEVICES) != CONFIGRET.CR_SUCCESS)
                    {
                        return false;
                    }
                    // A non-empty list (i.e., for a hub) would be double-NUL terminated.
                    return length >= 2;
                }
            }
        }
    }

    /// <summary>
    /// true if the device is currently present (plugged in).
    /// </summary>
    public bool IsPresent => TryGetProperty(Node, PInvoke.DEVPKEY_Device_DevNodeStatus, out CM_DEVNODE_STATUS_FLAGS _);

    /// <summary>
    /// true if the device is currently present (plugged in), but disabled in Device Manager.
    /// </summary>
    public bool IsDisabled => TryGetProperty(Node, PInvoke.DEVPKEY_Device_DevNodeStatus, out CM_DEVNODE_STATUS_FLAGS status)
        && status.HasFlag(CM_DEVNODE_STATUS_FLAGS.DN_HAS_PROBLEM)
        && TryGetProperty(Node, PInvoke.DEVPKEY_Device_ProblemCode, out CM_PROB problem)
        && problem == CM_PROB.CM_PROB_DISABLED;

    /// <summary>
    /// The FriendlyName of the device (if any).
    /// </summary>
    public string? FriendlyName
    {
        get
        {
            if (!TryGetProperty(Node, PInvoke.DEVPKEY_Device_FriendlyName, out string friendlyName))
            {
                return null;
            }
            friendlyName = friendlyName.Trim();
            return string.IsNullOrEmpty(friendlyName) ? null : friendlyName;
        }
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
            // Failure means: the hub has no (more) children, or a race between
            // removing the hub and enumerating its children. In any case, we are done enumerating.
            if (PInvoke.CM_Get_Child(out var childNode, Node, 0) != CONFIGRET.CR_SUCCESS)
            {
                yield break;
            }
            if (!TryCreate(childNode, out var childDevice))
            {
                yield break;
            }
            yield return childDevice;

            while (true)
            {
                if (PInvoke.CM_Get_Sibling(out childNode, childNode, 0) != CONFIGRET.CR_SUCCESS)
                {
                    yield break;
                }
                if (!TryCreate(childNode, out childDevice))
                {
                    continue;
                }
                yield return childDevice;
            }
        }
    }

    static bool TryGetProperty(uint deviceNode, in DEVPROPKEY devPropKey, out byte[] value, out DEVPROPTYPE propertyType)
    {
        unsafe // DevSkim: ignore DS172412
        {
            var propertyBufferSize = 0u;
            if (PInvoke.CM_Get_DevNode_Property(deviceNode, devPropKey, out propertyType, null, ref propertyBufferSize, 0) != CONFIGRET.CR_BUFFER_SMALL)
            {
                value = default!;
                propertyType = DEVPROPTYPE.DEVPROP_TYPE_EMPTY;
                return false;
            }
            var buffer = new byte[checked((int)propertyBufferSize)];
            fixed (byte* pBuffer = buffer)
            {
                if (PInvoke.CM_Get_DevNode_Property(deviceNode, devPropKey, out propertyType, pBuffer, ref propertyBufferSize, 0) != CONFIGRET.CR_SUCCESS)
                {
                    value = default!;
                    propertyType = DEVPROPTYPE.DEVPROP_TYPE_EMPTY;
                    return false;
                }
            }
            value = buffer;
            return true;
        }
    }

    static bool TryGetProperty(uint deviceNode, in DEVPROPKEY devPropKey, out string value)
    {
        if (!TryGetProperty(deviceNode, devPropKey, out var buffer, out var propertyType))
        {
            value = default!;
            return false;
        }

        if (propertyType != DEVPROPTYPE.DEVPROP_TYPE_STRING)
        {
            value = default!;
            return false;
        }

        unsafe // DevSkim: ignore DS172412
        {
            fixed (byte* pBuffer = buffer)
            {
                // The buffer includes the terminating NUL character.
                value = new string((char*)pBuffer, 0, (buffer.Length / sizeof(char)) - 1);
                return true;
            }
        }
    }

    static bool TryGetProperty(uint deviceNode, in DEVPROPKEY devPropKey, out string[] value)
    {
        if (!TryGetProperty(deviceNode, devPropKey, out var buffer, out var propertyType))
        {
            value = default!;
            return false;
        }

        if (propertyType != DEVPROPTYPE.DEVPROP_TYPE_STRING_LIST)
        {
            value = default!;
            return false;
        }

        unsafe // DevSkim: ignore DS172412
        {
            fixed (byte* pBuffer = buffer)
            {
                // The buffer is double-NUL terminated.
                value = new string((char*)pBuffer, 0, (buffer.Length / sizeof(char)) - 2).Split('\0');
                return true;
            }
        }
    }

    static bool TryGetProperty(uint deviceNode, in DEVPROPKEY devPropKey, out uint value)
    {
        if (!TryGetProperty(deviceNode, devPropKey, out var buffer, out var propertyType))
        {
            value = default!;
            return false;
        }

        if (propertyType != DEVPROPTYPE.DEVPROP_TYPE_UINT32)
        {
            value = default!;
            return false;
        }

        if (buffer.Length != sizeof(uint))
        {
            value = default!;
            return false;
        }

        unsafe // DevSkim: ignore DS172412
        {
            fixed (byte* pBuffer = buffer)
            {
                value = *(uint*)pBuffer;
                return true;
            }
        }
    }

    static bool TryGetProperty<T>(uint deviceNode, in DEVPROPKEY devPropKey, out T value) where T : struct, Enum
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
        string[] instanceIds;

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

        unsafe // DevSkim: ignore DS172412
        {
            if (PInvoke.CM_Get_Device_ID_List_Size(out var bufferLength, filter, flags) != CONFIGRET.CR_SUCCESS)
            {
                yield break;
            }
            if (bufferLength <= 1)
            {
                // Empty list.
                yield break;
            }
            var buffer = new char[checked((int)bufferLength)];
            fixed (char* pBuffer = buffer)
            {
                if (PInvoke.CM_Get_Device_ID_List(filter, pBuffer, bufferLength, flags) != CONFIGRET.CR_SUCCESS)
                {
                    yield break;
                }
                // The list is double-NUL terminated.
                instanceIds = new string(pBuffer, 0, (int)bufferLength - 2).Split('\0');
            }
        }

        foreach (var instanceId in instanceIds)
        {
            if (TryCreate(instanceId, out var device))
            {
                yield return device;
            }
        }
    }
}
