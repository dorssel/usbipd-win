// SPDX-FileCopyrightText: Microsoft Corporation
//
// SPDX-License-Identifier: GPL-2.0-only

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32;
using Windows.Win32;

using static UsbIpServer.Interop.VBoxUsbMon;

namespace UsbIpServer
{
    static class RegistryUtils
    {
        public const string DevicesRegistryPath = @"SOFTWARE\usbipd-win";

        static RegistryKey OpenBaseKey(bool writable)
        {
            return Registry.LocalMachine.OpenSubKey(DevicesRegistryPath, writable)
                ?? throw new UnexpectedResultException("Registry key not found; try reinstalling the software.");
        }

        static readonly Lazy<RegistryKey> ReadOnlyBaseKey = new(() => OpenBaseKey(false));
        static readonly Lazy<RegistryKey> WritableBaseKey = new(() => OpenBaseKey(true));

        static RegistryKey BaseKey(bool writable) => (writable ? WritableBaseKey : ReadOnlyBaseKey).Value;

        const string DevicesName = "Devices";
        const string DescriptionName = "Description";
        const string BusIdName = "BusId";
        const string FilterName = "Filter";
        const string AttachedName = "Attached";
        const string IPAddressName = "IPAddress";
        const string OriginalInstanceIdName = "OriginalInstanceId";

        static RegistryKey GetDevicesKey(bool writable)
        {
            return BaseKey(writable).OpenSubKey(DevicesName, writable)
                ?? throw new UnexpectedResultException("Registry key not found; try reinstalling the software.");
        }

        static RegistryKey? GetDeviceKey(Guid guid, bool writable)
        {
            using var devicesKey = GetDevicesKey(writable);
            return devicesKey.OpenSubKey(guid.ToString("B"), writable);
        }

        static RegistryKey? GetDeviceKey(ExportedDevice device, bool writable)
        {
            foreach (var persistedDevice in GetPersistedDevices())
            {
                var deviceKey = GetDeviceKey(persistedDevice.Guid, writable);
                if (deviceKey is not null && IsDeviceMatch(deviceKey, device))
                {
                    return deviceKey;
                }
                deviceKey?.Dispose();
            }
            return null;
        }

        // To share is to equivalently have it in the registry.
        // If a device is not in the registry, then it is not shared.
        public static void ShareDevice(ExportedDevice device, string description)
        {
            var guid = Guid.NewGuid();
            using var deviceKey = GetDevicesKey(true).CreateSubKey($"{guid:B}");
            deviceKey.SetValue(BusIdName, device.BusId.ToString());
            deviceKey.SetValue(DescriptionName, description);
            using var filterKey = deviceKey.CreateSubKey(FilterName);
            // int maps to RegistryValueKind.DWord
            filterKey.SetValue(nameof(UsbFilterIdx.VENDOR_ID), (int)device.VendorId);
            filterKey.SetValue(nameof(UsbFilterIdx.PRODUCT_ID), (int)device.ProductId);
            filterKey.SetValue(nameof(UsbFilterIdx.DEVICE), (int)device.BcdDevice);
            filterKey.SetValue(nameof(UsbFilterIdx.DEVICE_CLASS), (int)device.DeviceClass);
            filterKey.SetValue(nameof(UsbFilterIdx.DEVICE_SUB_CLASS), (int)device.DeviceSubClass);
            filterKey.SetValue(nameof(UsbFilterIdx.DEVICE_PROTOCOL), (int)device.DeviceProtocol);
        }

        public static string? GetDeviceDescription(ExportedDevice device)
        {
            using var deviceKey = GetDeviceKey(device, false);
            return (string?)deviceKey?.GetValue(DescriptionName);
        }

        public static bool IsDeviceShared(ExportedDevice device)
        {
            using var deviceKey = GetDeviceKey(device, false);
            return deviceKey is not null;
        }

        static bool IsDeviceMatch(RegistryKey deviceKey, ExportedDevice device)
        {
            using var filterKey = deviceKey.OpenSubKey(FilterName);
            return filterKey is not null
                && BusId.TryParse((string?)deviceKey.GetValue(BusIdName) ?? string.Empty, out var busId)
                && busId == device.BusId
                && (int?)filterKey.GetValue(nameof(UsbFilterIdx.VENDOR_ID)) == device.VendorId
                && (int?)filterKey.GetValue(nameof(UsbFilterIdx.PRODUCT_ID)) == device.ProductId
                && (int?)filterKey.GetValue(nameof(UsbFilterIdx.DEVICE)) == device.BcdDevice
                && (int?)filterKey.GetValue(nameof(UsbFilterIdx.DEVICE_CLASS)) == device.DeviceClass
                && (int?)filterKey.GetValue(nameof(UsbFilterIdx.DEVICE_SUB_CLASS)) == device.DeviceSubClass
                && (int?)filterKey.GetValue(nameof(UsbFilterIdx.DEVICE_PROTOCOL)) == device.DeviceProtocol
                ;
        }

        public static void StopSharingDevice(Guid guid)
        {
            using var devicesKey = GetDevicesKey(true);
            devicesKey.DeleteSubKeyTree(guid.ToString("B"), false);
        }

        public static void StopSharingDevice(ExportedDevice device)
        {
            foreach (var persistedDevice in GetPersistedDevices())
            {
                using var deviceKey = GetDeviceKey(persistedDevice.Guid, false);
                if (deviceKey is not null && IsDeviceMatch(deviceKey, device))
                {
                    StopSharingDevice(persistedDevice.Guid);
                    return;
                }
            }
        }

        public static void StopSharingAllDevices()
        {
            foreach (var persistedDevice in GetPersistedDevices())
            {
                StopSharingDevice(persistedDevice.Guid);
            }
        }

        public class PersistedDevice
        {
            public Guid Guid { get; }
            public BusId BusId { get; }
            public string Description { get; }
            public PersistedDevice(Guid guid, BusId busid, string description)
            {
                Guid = guid;
                BusId = busid;
                Description = description;
            }
        }

        public static bool IsDeviceAttached(ExportedDevice device)
        {
            using var deviceKey = GetDeviceKey(device, false);
            return deviceKey is not null && deviceKey.GetSubKeyNames().Contains(AttachedName) && Server.IsServerRunning();
        }

        public static RegistryKey SetDeviceAsAttached(ExportedDevice device, IPAddress address)
        {
            using var key = GetDeviceKey(device, true)
                ?? throw new UnexpectedResultException($"{nameof(SetDeviceAsAttached)}: Device key not found");
            var attached = key.CreateSubKey(AttachedName, true, RegistryOptions.Volatile)
                ?? throw new UnexpectedResultException($"{nameof(SetDeviceAsAttached)}: Unable to create ${AttachedName} subkey");
            // Allow users that are logged in on the console to delete the key (detach).
            var registrySecurity = attached.GetAccessControl(AccessControlSections.All);
            registrySecurity.AddAccessRule(new RegistryAccessRule(new SecurityIdentifier(WellKnownSidType.WinConsoleLogonSid, null),
                RegistryRights.Delete, AccessControlType.Allow));
            attached.SetAccessControl(registrySecurity);
            try
            {
                attached.SetValue(IPAddressName, address.ToString());
                attached.SetValue(OriginalInstanceIdName, device.InstanceId);
                return attached;
            }
            catch
            {
                attached.Dispose();
                throw;
            }
        }

        static bool RemoveAttachedSubKey(RegistryKey deviceKey)
        {
            // .NET does not have this functionality: delete a key to which you have rights while
            // you do not have rights to the containing key. So, we must use the API directly.
            // Instead of checking the return value we will check if the Attached key is actually gone.
            PInvoke.RegDeleteKey(deviceKey.Handle, AttachedName);
            using var attached = deviceKey.OpenSubKey(AttachedName, false);
            return attached is null;
        }

        public static bool SetDeviceAsDetached(ExportedDevice device)
        {
            using var deviceKey = GetDeviceKey(device, false);
            if (deviceKey is null)
            {
                return true;
            }
            return RemoveAttachedSubKey(deviceKey);
        }

        public static bool SetAllDevicesAsDetached()
        {
            using var devicesKey = GetDevicesKey(false);
            var deviceKeyNames = devicesKey?.GetSubKeyNames() ?? Array.Empty<string>();
            var failure = false;
            foreach (var deviceKeyName in deviceKeyNames)
            {
                using var deviceKey = devicesKey?.OpenSubKey(deviceKeyName, false);
                if (deviceKey is null)
                {
                    continue;
                }
                if (!RemoveAttachedSubKey(deviceKey))
                {
                    failure = true;
                }
            }
            return !failure;
        }

        public static IPAddress? GetDeviceAddress(ExportedDevice device)
        {
            using var deviceKey = GetDeviceKey(device, false);
            using var subKey = deviceKey?.OpenSubKey(AttachedName, false);
            return subKey?.GetValue(IPAddressName) is string value ? IPAddress.Parse(value) : null;
        }

        public static string? GetOriginalInstanceId(ExportedDevice device)
        {
            using var deviceKey = GetDeviceKey(device, false);
            using var subKey = deviceKey?.OpenSubKey(AttachedName, false);
            return (string?)subKey?.GetValue(OriginalInstanceIdName);
        }

        /// <summary>
        /// Enumerates all persisted devices, currently present or not.
        /// </summary>
        public static IEnumerable<PersistedDevice> GetPersistedDevices()
        {
            var guids = new SortedSet<Guid>();
            using var devicesKey = GetDevicesKey(false);
            foreach (var subKeyName in devicesKey.GetSubKeyNames())
            {
                if (Guid.TryParseExact(subKeyName, "B", out var guid))
                {
                    guids.Add(guid);
                }
            }
            var persistedDevices = new List<PersistedDevice>();
            foreach (var guid in guids)
            {
                using var deviceKey = GetDeviceKey(guid, false);
                if (deviceKey is not null)
                {
                    if (!BusId.TryParse(deviceKey.GetValue(BusIdName) as string ?? "", out var busId))
                    {
                        continue;
                    }
                    if (deviceKey.GetValue(DescriptionName) is not string description)
                    {
                        continue;
                    }
                    persistedDevices.Add(new(guid, busId, description));
                }
            }
            return persistedDevices;
        }

        /// <summary>
        /// Enumerates the persisted devices that are currently not present.
        /// </summary>
        public static IEnumerable<PersistedDevice> GetPersistedDevices(ExportedDevice[] connectedDevices)
        {
            return GetPersistedDevices().Where(persistedDevice =>
            {
                using var deviceKey = GetDeviceKey(persistedDevice.Guid, false);
                if (deviceKey is null)
                {
                    return false;
                }
                return !connectedDevices.Any(connectedDevice => IsDeviceMatch(deviceKey, connectedDevice));
            });
        }

        public static bool HasWriteAccess()
        {
            try
            {
                return BaseKey(true) is not null;
            }
            catch (SecurityException)
            {
                return false;
            }
        }
    }
}
