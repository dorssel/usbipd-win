// SPDX-FileCopyrightText: 2025 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

using Windows.Win32;
using Windows.Win32.Devices.DeviceAndDriverInstallation;
using Windows.Win32.Devices.Properties;

namespace Usbipd;

/// <summary>
/// Represents a device interface as seen by Windows.
/// <para>
/// Each device interface is for a specific device and has a unique interface path.
/// </para>
/// </summary>
sealed partial class WindowsDeviceInterface(WindowsDevice device, string interfacePath)
{
    public WindowsDevice Device { get; } = device;
    public string InterfacePath { get; } = interfacePath;

    /// <returns>false if the corresponding device does not exist.</returns>
    public static bool TryCreate(string interfacePath, out WindowsDeviceInterface deviceInterface)
    {
        if (!TryGetProperty(interfacePath, PInvoke.DEVPKEY_Device_InstanceId, out var instanceId)
            || !WindowsDevice.TryCreate(instanceId, out var device))
        {
            deviceInterface = default!;
            return false;
        }
        deviceInterface = new(device, interfacePath);
        return true;
    }

    static bool TryGetProperty(string interfacePath, in DEVPROPKEY devPropKey, out byte[] value, out DEVPROPTYPE propertyType)
    {
        unsafe // DevSkim: ignore DS172412
        {
            var propertyBufferSize = 0u;
            if (PInvoke.CM_Get_Device_Interface_Property(interfacePath, devPropKey, out propertyType, null, ref propertyBufferSize, 0)
                != CONFIGRET.CR_BUFFER_SMALL)
            {
                value = default!;
                propertyType = DEVPROPTYPE.DEVPROP_TYPE_EMPTY;
                return false;
            }
            var buffer = new byte[checked((int)propertyBufferSize)];
            fixed (byte* pBuffer = buffer)
            {
                if (PInvoke.CM_Get_Device_Interface_Property(interfacePath, devPropKey, out propertyType, pBuffer, ref propertyBufferSize, 0)
                    != CONFIGRET.CR_SUCCESS)
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

    static bool TryGetProperty(string interfacePath, in DEVPROPKEY devPropKey, out string value)
    {
        if (!TryGetProperty(interfacePath, devPropKey, out var buffer, out var propertyType))
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

    /// <returns>All present device interfaces of a specific class.</returns>
    public static IEnumerable<WindowsDeviceInterface> GetAll(Guid interfaceClassGuid)
    {
        string[] interfacePaths;

        unsafe // DevSkim: ignore DS172412
        {
            if (PInvoke.CM_Get_Device_Interface_List_Size(out var bufferLength, interfaceClassGuid, null,
                CM_GET_DEVICE_INTERFACE_LIST_FLAGS.CM_GET_DEVICE_INTERFACE_LIST_PRESENT) != CONFIGRET.CR_SUCCESS)
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
                if (PInvoke.CM_Get_Device_Interface_List(interfaceClassGuid, null, pBuffer, bufferLength,
                    CM_GET_DEVICE_INTERFACE_LIST_FLAGS.CM_GET_DEVICE_INTERFACE_LIST_PRESENT) != CONFIGRET.CR_SUCCESS)
                {
                    yield break;
                }
                // The list is double-NUL terminated.
                interfacePaths = new string(pBuffer, 0, (int)bufferLength - 2).Split('\0');
            }
        }

        foreach (var interfacePath in interfacePaths)
        {
            if (TryCreate(interfacePath, out var deviceInterface))
            {
                yield return deviceInterface;
            }
        }
    }
}
