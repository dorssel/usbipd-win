// SPDX-FileCopyrightText: Microsoft Corporation
//
// SPDX-License-Identifier: GPL-2.0-only

using System;
using System.Linq;
using System.Security.Principal;
using System.Globalization;
using Microsoft.Win32;
using System.Collections.Generic;
using System.Net;

namespace UsbIpServer
{
    static class RegistryUtils
    {
        const string DevicesRegistryPath = @"SOFTWARE\usbipd-win";
        const string IPAddressName = "IPAddress";
        const string TemporaryName = "Temporary";

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
            return GetRegistryKey(device) != null;
        }

        public static bool IsDeviceAttached(ExportedDevice device)
        {
            var key = GetRegistryKey(device);
            if (key != null)
            {
                return key.GetSubKeyNames().Contains("Attached") && Server.IsServerRunning();
            }

            return false;
        }

        public static bool IsDeviceSharedTemporarily(ExportedDevice device)
        {
            return (string?)GetRegistryKey(device)?.GetValue(TemporaryName) == "True";
        }

        static bool IsDeviceMatch(RegistryKey deviceKey, ExportedDevice device)
        {
            
            var cultureInfo = CultureInfo.InvariantCulture;
            if ((string?)deviceKey.GetValue(DeviceFilter.VENDOR_ID) == device.VendorId.ToString(cultureInfo) &&
                (string?)deviceKey.GetValue(DeviceFilter.PRODUCT_ID) == device.ProductId.ToString(cultureInfo) &&
                (string?)deviceKey.GetValue(DeviceFilter.BCD_DEVICE) == device.BcdDevice.ToString(cultureInfo) &&
                (string?)deviceKey.GetValue(DeviceFilter.DEVICE_CLASS) == device.DeviceClass.ToString(cultureInfo) &&
                (string?)deviceKey.GetValue(DeviceFilter.DEVICE_SUBCLASS) == device.DeviceSubClass.ToString(cultureInfo) &&
                (string?)deviceKey.GetValue(DeviceFilter.DEVICE_PROTOCOL) == device.DeviceProtocol.ToString(cultureInfo) &&
                (string?)deviceKey.GetValue(DeviceFilter.DEV_NUM) == device.DevNum.ToString(cultureInfo) &&
                (string?)deviceKey.GetValue(DeviceFilter.BUS_ID) == device.BusId)
            {
                return true;
            }

            return false;
        }

        // To share is to equivalently have it in the registry.
        // If a device is not in the registry, then it is not shared.
        public static void ShareDevice(ExportedDevice device, string name, bool isTemporary)
        {
            var guid = Guid.NewGuid();
            var entry = Registry.LocalMachine.CreateSubKey(@$"{DevicesRegistryPath}\{guid}", true, isTemporary ? RegistryOptions.Volatile : RegistryOptions.None);
            entry.SetValue(DeviceFilter.VENDOR_ID, device.VendorId);
            entry.SetValue(DeviceFilter.PRODUCT_ID, device.ProductId);
            entry.SetValue(DeviceFilter.BCD_DEVICE, device.BcdDevice);
            entry.SetValue(DeviceFilter.DEVICE_CLASS, device.DeviceClass);
            entry.SetValue(DeviceFilter.DEVICE_SUBCLASS, device.DeviceSubClass);
            entry.SetValue(DeviceFilter.DEVICE_PROTOCOL, device.DeviceProtocol);
            entry.SetValue(DeviceFilter.DEV_NUM, device.DevNum);
            entry.SetValue(DeviceFilter.BUS_ID, device.BusId);
            entry.SetValue("Name", name);
            entry.SetValue(TemporaryName, isTemporary);
        }

        public static void StopSharingDevice(ExportedDevice device)
        {
            var guid = GetRegistryKeyName(device);
            if (guid != null)
            {
                Registry.LocalMachine.DeleteSubKeyTree(@$"{DevicesRegistryPath}\{guid}");
            }
        }

        public static void StopSharingDevice(string guid)
        {
            Registry.LocalMachine.DeleteSubKeyTree(@$"{DevicesRegistryPath}\{guid}");
        }

        public static void StopSharingAllDevices()
        {
            var deviceKeyNames = Registry.LocalMachine.CreateSubKey(DevicesRegistryPath).GetSubKeyNames();
            foreach (var keyName in deviceKeyNames)
            {
                StopSharingDevice(keyName);
            }
        }

        public static void StopSharingTemporaryDevices()
        {
            var deviceKeyNames = Registry.LocalMachine.CreateSubKey(DevicesRegistryPath).GetSubKeyNames()
                .Where(key => (string?)Registry.LocalMachine.OpenSubKey(@$"{DevicesRegistryPath}\{key}")?.GetValue(TemporaryName) == "True");
            foreach (var keyName in deviceKeyNames)
            {
                StopSharingDevice(keyName);
            }
        }

        public class PersistedDevice
        {
            public string Guid { get; }
            public string BusId { get; }
            public string Name { get; }
            public PersistedDevice(string guid, string busid, string name)
            {
                Guid = guid;
                BusId = busid;
                Name = name;
            }

        }

        public static RegistryKey? GetRegistryKey(ExportedDevice device)
        {
            var deviceKeyNames = Registry.LocalMachine.CreateSubKey(DevicesRegistryPath).GetSubKeyNames();
            foreach (var keyName in deviceKeyNames)
            {
                var deviceKey = Registry.LocalMachine.CreateSubKey(@$"{DevicesRegistryPath}\{keyName}");
                if (IsDeviceMatch(deviceKey, device))
                {
                    return deviceKey;
                }
            }

            return null;
        }

        public static string? GetRegistryKeyName(ExportedDevice device)
        {
            var deviceKeyNames = Registry.LocalMachine.CreateSubKey(DevicesRegistryPath).GetSubKeyNames();
            foreach (var keyName in deviceKeyNames)
            {
                var deviceKey = Registry.LocalMachine.CreateSubKey(@$"{DevicesRegistryPath}\{keyName}");
                if (IsDeviceMatch(deviceKey, device))
                {
                    return keyName;
                }
            }

            return null;
        }



        public static void SetDeviceAsAttached(ExportedDevice device)
        {
            var key = GetRegistryKey(device);
            if (key != null)
            {
                key.CreateSubKey("Attached", true, RegistryOptions.Volatile);
            }
        }

        public static void SetDeviceAsDetached(ExportedDevice device)
        {
            var keyName = GetRegistryKeyName(device);
            if (keyName != null)
            {
                Registry.LocalMachine.DeleteSubKeyTree(@$"{DevicesRegistryPath}\{keyName}\Attached");
            }
        }

        public static void SetDeviceAddress(ExportedDevice device, string address)
        {
            var key = GetRegistryKey(device);
            if (key != null)
            {
                key.SetValue(IPAddressName, address);
            }
        }

        public static IPAddress? GetDeviceAddress(ExportedDevice device)
        {
            var value = (string?)GetRegistryKey(device)?.GetValue(IPAddressName);
            return value == null ? null : IPAddress.Parse(value);
        }

        public static List<PersistedDevice> GetPersistedDevices(ExportedDevice[] connectedDevices)
        {
            var persistedDevices = new List<PersistedDevice>();
            var deviceKeyNames = Registry.LocalMachine.CreateSubKey(DevicesRegistryPath).GetSubKeyNames();
            foreach (var keyName in deviceKeyNames)
            {
                var deviceKey = Registry.LocalMachine.CreateSubKey(@$"{DevicesRegistryPath}\{keyName}");
                var deviceFound = false;
                foreach (var connectedDevice in connectedDevices)
                {
                    if (IsDeviceMatch(deviceKey, connectedDevice))
                    {
                        deviceFound = true;
                    }
                }

                if (!deviceFound)
                {
                    persistedDevices.Add(
                        new PersistedDevice(
                            keyName,
                            (string?)deviceKey.GetValue(DeviceFilter.BUS_ID)??"",
                            (string?)deviceKey.GetValue("Name")??""
                            )
                        ); ;
                }
            }

            return persistedDevices;
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
