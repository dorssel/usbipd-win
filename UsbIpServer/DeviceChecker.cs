using System.Collections.Generic;
using System.Management;

namespace UsbIpServer
{
    class DeviceInfoChecker
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
            var possibleDeviceNames = new HashSet<string>();
            foreach (var usbDevice in devices)
            {
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

    class DeviceInfo
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
