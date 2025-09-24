// SPDX-FileCopyrightText: 2021 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

using System.Text;
using Windows.Win32;
using Windows.Win32.Devices.DeviceAndDriverInstallation;
using Windows.Win32.Devices.Properties;

namespace Usbipd;

static class ConfigurationManager
{
    public static void ThrowOnError(this CONFIGRET configRet, string function)
    {
        if (configRet != CONFIGRET.CR_SUCCESS)
        {
            throw new ConfigurationManagerException(configRet, $"{function} returned {configRet}");
        }
    }

    static unsafe object ConvertProperty(DEVPROPTYPE propertyType, byte* pBuffer, int propertyBufferSize) // DevSkim: ignore DS172412
    {
        return propertyType == DEVPROPTYPE.DEVPROP_TYPE_STRING
            ? new string((char*)pBuffer, 0, propertyBufferSize / sizeof(char)).TrimEnd('\0')
            : propertyType == DEVPROPTYPE.DEVPROP_TYPE_STRING_LIST
                ? (object)new string((char*)pBuffer, 0, propertyBufferSize / sizeof(char)).Split('\0', StringSplitOptions.RemoveEmptyEntries)
                : throw new NotImplementedException($"property type {propertyType}");
    }

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
            var buffer = new byte[propertyBufferSize];
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

    internal static bool TryGetProperty(uint deviceNode, in DEVPROPKEY devPropKey, out string value)
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

    internal static bool TryGetProperty(uint deviceNode, in DEVPROPKEY devPropKey, out string[] value)
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

    static string? GetDeviceName(uint deviceNode)
    {
        if (!TryGetProperty(deviceNode, PInvoke.DEVPKEY_NAME, out string name))
        {
            // This can happen for devices without a driver *and* without a USB string table.
            return null;
        }
        name = name.Trim();
        return string.IsNullOrEmpty(name) ? null : name;
    }

    public const string UnknownDevice = "Unknown device";

    public static string GetDescription(uint deviceNode)
    {
        if (!WindowsDevice.TryCreate(deviceNode, out var device))
        {
            return UnknownDevice;
        }

        var isCompositeDevice = TryGetProperty(deviceNode, PInvoke.DEVPKEY_Device_CompatibleIds, out string[] compatibleIds)
            && compatibleIds.Contains(@"USB\COMPOSITE");
        if (!isCompositeDevice)
        {
            // NAME is FriendlyName (if it exists), or else it is Description (if it exists), or else UnknownDevice
            return GetDeviceName(deviceNode) ?? UnknownDevice;
        }

        // For USB\COMPOSITE we need to add the descriptions of the direct children.
        // We remove duplicates, often something like "HID Device", but we also want to preserve the order
        var descriptionSet = new SortedSet<string>();
        var descriptionList = new List<string>();

        void AddDescription(string? desc)
        {
            if (desc != null)
            {
                desc = desc.Trim();
                if (!string.IsNullOrEmpty(desc) && descriptionSet.Add(desc))
                {
                    descriptionList.Add(desc);
                }
            }
        }

        // The FriendlyName (if it exists) is useful for USB\COMPOSITE.
        // Description definitely is not (for composite devices).
        AddDescription(device.FriendlyName);

        foreach (var childDevice in device.Children)
        {
            AddDescription(GetDeviceName(childDevice.Node));
        }

        if (descriptionList.Count == 0)
        {
            // This can happen if the USB\COMPOSITE device does not have a FriendlyName and the
            // bus has not yet enumerated the children; for example, right after releasing the device
            // back to Windows. Just fall back to the non-descriptive name for USB\COMPOSITE.
            return GetDeviceName(deviceNode) ?? UnknownDevice;
        }

        return string.Join(", ", descriptionList);
    }

    const string FriendlyName = "USBIP Shared Device";

    public static void SetDeviceFriendlyName(uint deviceNode)
    {
        // NOTE: Must include terminating NUL character.
        PInvoke.CM_Set_DevNode_Property(deviceNode, PInvoke.DEVPKEY_Device_FriendlyName, DEVPROPTYPE.DEVPROP_TYPE_STRING,
            Encoding.Unicode.GetBytes($"{FriendlyName}\0"), 0).ThrowOnError(nameof(PInvoke.CM_Set_DevNode_Property));
    }
}
