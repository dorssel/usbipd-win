using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Win32;
using System.Threading;

namespace UsbIpServer
{
    class RegistryUtils
    {
        public struct RegistryDevice
        {
            public string BusId;
            public string VendorId;
            public string ProductId;
            public bool IsAvailable;
        }

        const string devicesRegistryPath = @"SOFTWARE\USBIPD-WIN";

        public static IEnumerable<RegistryDevice> GetRegistryDevices()
        {
            var registryDevices = new List<RegistryDevice>();
            var devices = Registry.LocalMachine.CreateSubKey(devicesRegistryPath);
            var deviceIds = devices.GetSubKeyNames();
            foreach (var id in deviceIds)
            {   
                var d = devices.OpenSubKey(id);
                RegistryDevice registryDevice;
                registryDevice.BusId = id;
                registryDevice.ProductId = (string)d.GetValue("product-id");
                registryDevice.VendorId = (string)d.GetValue("vendor-id");
                registryDevice.IsAvailable = (string)d.GetValue("available") == "True" ;
                registryDevices.Add(registryDevice);
            }

            return registryDevices;
        }

        public static bool IsDeviceAvailable(string busId)
        {
            return GetRegistryDevices().Any(x => x.IsAvailable && x.BusId == busId);
        }

        public static void SetDeviceAvailability(string busId, bool enable)
        {
            var deviceKey = Registry.LocalMachine.CreateSubKey($@"{devicesRegistryPath}\{busId}");
            deviceKey.SetValue("available", enable);
        }

        public static void InitializeRegistry()
        {
            Registry.LocalMachine.DeleteSubKey(devicesRegistryPath);
            Registry.LocalMachine.CreateSubKey(devicesRegistryPath);
        }
    }
}
