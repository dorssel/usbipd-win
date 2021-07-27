// SPDX-FileCopyrightText: Microsoft Corporation
//
// SPDX-License-Identifier: GPL-2.0-only

using System;
using System.Linq;
using System.Security.Principal;
using System;
using System.Globalization;
using Microsoft.Win32;

namespace UsbIpServer
{
    static class RegistryUtils
    {
        const string DevicesRegistryPath = @"SOFTWARE\usbipd-win";

        static class DeviceFilter
        {
            public const string VENDOR_ID = "VendorId";
            public const string PRODUCT_ID = "ProductId";
            public const string BCD_DEVICE = "BcdDevice";
            public const string DEVICE_CLASS = "DeviceClass";
            public const string DEVICE_SUBCLASS = "DeviceSubClass";
            public const string DEVICE_PROTOCOL = "DeviceProtocol";
            public const string DEV_NUM = "DevNum";
            public const string BUS_ID = "BusId";
        }

        public static bool IsDeviceShared(ExportedDevice device)
        {
            var deviceKeyNames = Registry.LocalMachine.CreateSubKey(devicesRegistryPath).GetSubKeyNames();
            foreach (var keyName in deviceKeyNames)
            {
                var deviceKey = Registry.LocalMachine.CreateSubKey(@$"{devicesRegistryPath}\{keyName}");
                if (IsDeviceMatchInRegistry(deviceKey, device))
                {
                    return true;
                }
            }

            return false;
        }

        static bool IsDeviceMatchInRegistry(RegistryKey deviceKey, ExportedDevice device)
        {
            try
            {
                var cultureInfo = new CultureInfo("en-US");
#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type.
                if ((string)deviceKey.GetValue(DeviceFilter.VENDOR_ID) == device.VendorId.ToString(cultureInfo) |
                    (string)deviceKey.GetValue(DeviceFilter.PRODUCT_ID) == device.ProductId.ToString(cultureInfo) ||
                    (string)deviceKey.GetValue(DeviceFilter.BCD_DEVICE) == device.BcdDevice.ToString(cultureInfo) ||
                    (string)deviceKey.GetValue(DeviceFilter.DEVICE_CLASS) == device.DeviceClass.ToString(cultureInfo) ||
                    (string)deviceKey.GetValue(DeviceFilter.DEVICE_SUBCLASS) == device.DeviceSubClass.ToString(cultureInfo) ||
                    (string)deviceKey.GetValue(DeviceFilter.DEVICE_PROTOCOL) == device.DeviceProtocol.ToString(cultureInfo) ||
                    (string)deviceKey.GetValue(DeviceFilter.DEV_NUM) == device.DevNum.ToString(cultureInfo) ||
                    (string)deviceKey.GetValue(DeviceFilter.BUS_ID) == device.BusId)
#pragma warning restore CS8600 // Converting null literal or possible null value to non-nullable type.
                {
                    return true;
                }
            }
            catch (NullReferenceException)
            {
                // this is expected if a value does not exist in the registry key.
            }

            return false;
        }

        // To share is to equivalently have it in the registry.
        // If a device is not in the registry, then it is not shared.
        public static void ShareDevice(ExportedDevice device)
        {
            var guid = Guid.NewGuid();
            var entry = Registry.LocalMachine.CreateSubKey(@$"{devicesRegistryPath}\{guid}");
            entry.SetValue(DeviceFilter.VENDOR_ID, device.VendorId);
            entry.SetValue(DeviceFilter.PRODUCT_ID, device.ProductId);
            entry.SetValue(DeviceFilter.BCD_DEVICE, device.BcdDevice);
            entry.SetValue(DeviceFilter.DEVICE_CLASS, device.DeviceClass);
            entry.SetValue(DeviceFilter.DEVICE_SUBCLASS, device.DeviceSubClass);
            entry.SetValue(DeviceFilter.DEVICE_PROTOCOL, device.DeviceProtocol);
            entry.SetValue(DeviceFilter.DEV_NUM, device.DevNum);
            entry.SetValue(DeviceFilter.BUS_ID, device.BusId);
        }

        public static void StopSharingDevice(ExportedDevice device)
        {
            var deviceKeyNames = Registry.LocalMachine.CreateSubKey(devicesRegistryPath).GetSubKeyNames();
            foreach (var keyName in deviceKeyNames)
            {
                var deviceKey = Registry.LocalMachine.CreateSubKey(@$"{devicesRegistryPath}\{keyName}");
                if (IsDeviceMatchInRegistry(deviceKey, device))
                {
                    Registry.LocalMachine.DeleteSubKeyTree(@$"{devicesRegistryPath}\{keyName}");
                }               
            }
        }

        public static string[] GetRegistryDevices()
        {
            return Registry.LocalMachine.OpenSubKey(DevicesRegistryPath)?.GetSubKeyNames() ?? Array.Empty<string>();
        }

        public static void InitializeRegistry()
        {
            var devices = Registry.LocalMachine.OpenSubKey(DevicesRegistryPath, true)
                ?? throw new UnexpectedResultException($"registry key missing; try reinstalling the product");
            foreach (var subKeyName in devices.GetSubKeyNames())
            {
                devices.DeleteSubKey(subKeyName);
            }
        }

        public static bool HasRegistryAccess()
        {
            bool isElevated;
            using (var identity = WindowsIdentity.GetCurrent())
            {
                var principal = new WindowsPrincipal(identity);
                isElevated = principal.IsInRole(WindowsBuiltInRole.Administrator);
            }

            return isElevated;
        }
    }
}
