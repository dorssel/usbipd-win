// SPDX-FileCopyrightText: Microsoft Corporation
//
// SPDX-License-Identifier: GPL-2.0-only

using System.Linq;
using Microsoft.Win32;
using System.Security.Principal;
using System;

namespace UsbIpServer
{
    static class RegistryUtils
    {
        const string devicesRegistryPath = @"SOFTWARE\USBIPD-WIN";

        public static bool IsDeviceShared(ExportedDevice device)
        {
            var deviceKeyNames = Registry.LocalMachine.CreateSubKey(devicesRegistryPath).GetSubKeyNames();
            foreach (var keyName in deviceKeyNames)
            {
                var deviceKey = Registry.LocalMachine.CreateSubKey(@$"{devicesRegistryPath}\{keyName}");
                try
                {
#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type.
                    if ((string) deviceKey.GetValue(Filter.VENDOR_ID) != device.VendorId.ToString() |
                    (string) deviceKey.GetValue(Filter.PRODUCT_ID) != device.ProductId.ToString() ||
                    (string) deviceKey.GetValue(Filter.BCD_DEVICE) != device.BcdDevice.ToString() ||
                    (string) deviceKey.GetValue(Filter.DEVICE_CLASS) != device.DeviceClass.ToString() ||
                    (string) deviceKey.GetValue(Filter.DEVICE_SUBCLASS) != device.DeviceSubClass.ToString() ||
                    (string) deviceKey.GetValue(Filter.DEVICE_PROTOCOL) != device.DeviceProtocol.ToString() ||
                    (string) deviceKey.GetValue(Filter.DEV_NUM) != device.DevNum.ToString() ||
                    (string)deviceKey.GetValue(Filter.BUS_ID) != device.BusId)
#pragma warning restore CS8600 // Converting null literal or possible null value to non-nullable type.
                    {
                        continue;
                    }
                }
                catch (NullReferenceException)
                {
                    // this is expected if a value does not exist in the registry key.
                    continue;
                }
                

                return true;
            }

            return false;
        }

        static class Filter
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

        // To share is to equivalently have it in the registry.
        // If a device is not in the registry, then it is not shared.
        public static void ShareDevice(ExportedDevice device)
        {
            var guid = Guid.NewGuid();
            var entry = Registry.LocalMachine.CreateSubKey(@$"{devicesRegistryPath}\{guid}");
            entry.SetValue(Filter.VENDOR_ID, device.VendorId);
            entry.SetValue(Filter.PRODUCT_ID, device.ProductId);
            entry.SetValue(Filter.BCD_DEVICE, device.BcdDevice);
            entry.SetValue(Filter.DEVICE_CLASS, device.DeviceClass);
            entry.SetValue(Filter.DEVICE_SUBCLASS, device.DeviceSubClass);
            entry.SetValue(Filter.DEVICE_PROTOCOL, device.DeviceProtocol);
            entry.SetValue(Filter.DEV_NUM, device.DevNum);
            entry.SetValue(Filter.BUS_ID, device.BusId);
        }

        public static string[] GetRegistryDevices()
        {
            return Registry.LocalMachine.CreateSubKey(devicesRegistryPath).GetSubKeyNames();
        }

        public static void InitializeRegistry()
        {
            Registry.LocalMachine.DeleteSubKeyTree(devicesRegistryPath);
            Registry.LocalMachine.CreateSubKey(devicesRegistryPath);
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
