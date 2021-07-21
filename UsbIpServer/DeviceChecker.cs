// SPDX-FileCopyrightText: Microsoft Corporation
//
// SPDX-License-Identifier: GPL-2.0-only

using System.Collections.Generic;
using System.Management;

namespace UsbIpServer
{
    internal sealed class DeviceInfoChecker
    {
        List<DeviceInfo> devices = new List<DeviceInfo>();

        public DeviceInfoChecker()
        {
            devices = new List<DeviceInfo>();

            ManagementObjectCollection collection;
            using (var searcher = new ManagementObjectSearcher(@"Select * From Win32_PnPEntity"))
                collection = searcher.Get();

            foreach (ManagementObject device in collection)
            {
                devices.Add(new DeviceInfo(
                    (string)device.GetPropertyValue("DeviceID"),
                    (string)device.GetPropertyValue("PNPDeviceID"),
                    (string)device.GetPropertyValue("Description")
                ));
            }

            collection.Dispose();
        }

        public string GetDeviceName(string path)
        {
            var possibleDeviceNames = new SortedSet<string>();
            foreach (var usbDevice in devices)
            {
                // Example Path: USB\VID_046D&PID_C539\7&674AA44&0&3
                // The first part is device type, second is vid and pid and third is specific to the device,
                // but we deal with composite devices which have multiple devices in a single device.
                // We get all names/description to give a hint to the user.
                var parts = path.Split(@"\");
                var type = parts[0];
                var vid_pid = parts[1];
                if (usbDevice.DeviceID.StartsWith($@"{type}\{vid_pid}", System.StringComparison.OrdinalIgnoreCase))
                {
                    possibleDeviceNames.Add(usbDevice.Description);
                }
            }

            possibleDeviceNames.RemoveWhere(x => x == "USB Composite Device");
            return string.Join(", ", possibleDeviceNames);
        }
    }

    internal sealed class DeviceInfo
    {
        public DeviceInfo(string deviceID, string pnpDeviceID, string description)
        {
            DeviceID = deviceID;
            PnpDeviceID = pnpDeviceID;
            Description = description;
        }

        public string DeviceID { get; private set; }
        public string PnpDeviceID { get; private set; }
        public string Description { get; private set; }
    }
}
