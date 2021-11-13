// SPDX-FileCopyrightText: Microsoft Corporation
//
// SPDX-License-Identifier: GPL-2.0-only

using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;

namespace UsbIpServer
{
    sealed class DeviceInfoChecker
    {
        readonly Dictionary<string, string> DeviceDescriptions = new();

        public DeviceInfoChecker()
        {
            var query = new ObjectQuery(@"SELECT * FROM Win32_PnPEntity");
            var scope = new ManagementScope();
            scope.Options.Context.Add("__ProviderArchitecture", 64);
            scope.Options.Context.Add("__RequiredArchitecture", true);
            using var searcher = new ManagementObjectSearcher(scope, query);
            using var collection = searcher.Get();

            foreach (ManagementObject device in collection)
            {
                if (device.GetPropertyValue("DeviceID") is not string deviceId || !deviceId.StartsWith(@"USB\", StringComparison.InvariantCulture))
                {
                    // filter out everything not USB
                    continue;
                }
                if (device.GetPropertyValue("CompatibleID") is string[] compatibleIds && compatibleIds.Contains(@"USB\COMPOSITE"))
                {
                    // filter out "USB Composite Device" (in an i18n-safe way)
                    continue;
                }
                DeviceDescriptions.TryAdd((string)device.GetPropertyValue("DeviceID"), (string)device.GetPropertyValue("Description"));
            }
        }

        public string GetDeviceDescription(ExportedDevice device)
        {
            // first try to get it from registry (cache)
            if (RegistryUtils.GetDeviceDescription(device) is string description)
            {
                return description;
            }

            var path = device.Path;
            var descriptions = new SortedSet<string>();
            foreach (var (deviceId, deviceDescription) in DeviceDescriptions)
            {
                // Example Path: USB\VID_046D&PID_C539\7&674AA44&0&3
                // The first part is device type, second is vid and pid and third is specific to the device,
                // but we deal with composite devices which have multiple devices in a single device.
                // We get all names/description to give a hint to the user.
                var parts = path.Split(@"\");
                var type = parts[0];
                var vid_pid = parts[1];
                if (deviceId.StartsWith($@"{type}\{vid_pid}", StringComparison.OrdinalIgnoreCase))
                {
                    descriptions.Add(deviceDescription);
                }
            }
            return string.Join(", ", descriptions);
        }
    }
}
