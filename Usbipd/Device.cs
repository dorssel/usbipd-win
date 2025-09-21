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

sealed partial class Device(uint deviceNode, string instanceId)
{
    public uint DeviceNode { get; } = deviceNode;
    public string InstanceId { get; } = instanceId;

    public static bool TryCreate(uint deviceNode, out Device device)
    {
        if (!TryGetProperty(deviceNode, PInvoke.DEVPKEY_Device_InstanceId, out string instanceId))
        {
            device = default!;
            return false;
        }
        device = new(deviceNode, instanceId);
        return true;
    }

    public static bool TryCreate(string instanceId, out Device device)
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

    public bool IsStub => VidPid.TryParseId(InstanceId, out var vidPid) && (vidPid == DriverDetails.Instance.VidPid);

    public bool IsPresent => TryGetProperty(DeviceNode, PInvoke.DEVPKEY_Device_DevNodeStatus, out CM_DEVNODE_STATUS_FLAGS _);

    public bool IsDisabled => TryGetProperty(DeviceNode, PInvoke.DEVPKEY_Device_DevNodeStatus, out CM_DEVNODE_STATUS_FLAGS status)
        && status.HasFlag(CM_DEVNODE_STATUS_FLAGS.DN_HAS_PROBLEM)
        && TryGetProperty(DeviceNode, PInvoke.DEVPKEY_Device_ProblemCode, out CM_PROB problem)
        && problem == CM_PROB.CM_PROB_DISABLED;

    public string? FriendlyName
    {
        get
        {
            if (!TryGetProperty(DeviceNode, PInvoke.DEVPKEY_Device_FriendlyName, out string friendlyName))
            {
                return null;
            }
            friendlyName = friendlyName.Trim();
            return string.IsNullOrEmpty(friendlyName) ? null : friendlyName;
        }
    }

    [GeneratedRegex(@"^Port_#([0-9]{4}).Hub_#([0-9]{4})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LocationInfoRegex();

    public BusId BusId
    {
        get
        {
            if (!TryGetProperty(DeviceNode, PInvoke.DEVPKEY_Device_LocationInfo, out string locationInfo))
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

    public bool HasVBoxDriver => TryGetProperty(DeviceNode, PInvoke.DEVPKEY_Device_MatchingDeviceId, out string matchingDeviceId)
        && VidPid.TryParseId(matchingDeviceId, out var vidPid) && vidPid == DriverDetails.Instance.VidPid;

    public Version DriverVersion => (TryGetProperty(DeviceNode, PInvoke.DEVPKEY_Device_DriverVersion, out string versionText)
        && Version.TryParse(versionText, out var version)) ? version : new();

    static bool TryGetProperty(uint deviceNode, in DEVPROPKEY devPropKey, out byte[] value, out DEVPROPTYPE propertyType)
    {
        unsafe // DevSkim: ignore DS172412
        {
            var propertyBufferSize = 0u;
            var cr = PInvoke.CM_Get_DevNode_Property(deviceNode, devPropKey, out propertyType, null, ref propertyBufferSize, 0);
            if (cr != CONFIGRET.CR_BUFFER_SMALL)
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

    public static IEnumerable<Device> GetAll(Guid? classGuid, bool presentOnly)
    {
        string[] instanceIds;

        unsafe // DevSkim: ignore DS172412
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

            PInvoke.CM_Get_Device_ID_List_Size(out var size, filter, flags)
                .ThrowOnError(nameof(PInvoke.CM_Get_Device_ID_List_Size));
            var buffer = new char[checked((int)size)];
            fixed (char* pBuffer = buffer)
            {
                PInvoke.CM_Get_Device_ID_List(filter, pBuffer, size, flags)
                    .ThrowOnError(nameof(PInvoke.CM_Get_Device_ID_List));
                // The list is double-NUL terminated.
                instanceIds = new string(pBuffer, 0, (int)size - 2).Split('\0');
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
