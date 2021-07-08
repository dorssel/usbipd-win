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
            return GetRegistryDevices().Where(x => x.IsAvailable).Select(x=> x.BusId).Contains(busId);
        }


        public static void EnableRegistryDevice(string busId)
        {
            var devices = Registry.LocalMachine.CreateSubKey(devicesRegistryPath);
            var deviceIds = devices.GetSubKeyNames();
            foreach (var id in deviceIds)
            {
                if (id == busId)
                {
                    var d = devices.CreateSubKey(id);
                    d.SetValue("available", true);
                }
            }
        }

        public static void DisableRegistryDevice(string busId)
        {
            var devices = Registry.LocalMachine.CreateSubKey(devicesRegistryPath);
            var deviceIds = devices.GetSubKeyNames();
            foreach (var id in deviceIds)
            {
                if (id == busId)
                {
                    var d = devices.CreateSubKey(id);
                    d.SetValue("available", false);
                }
            }
        }

        public static async Task UpdateRegistry(CancellationToken stoppingToken)
        {
            // sync device list
            var devices = await ExportedDevice.GetAll(stoppingToken);
            var deviceKeys = Registry.LocalMachine.CreateSubKey(devicesRegistryPath);

            // update device list
            foreach (var d in devices)
            {
                var deviceKey = deviceKeys.CreateSubKey(d.BusId);
                if (!deviceKey.GetValueNames().Contains("available"))
                {
                    // first time seen, so don't share by default
                    deviceKey.SetValue("available", false);
                }

                // get device information 
                deviceKey.SetValue("vendor-id", d.VendorId);
                deviceKey.SetValue("product-id", d.ProductId);
            }

            // delete outdated devices from registry
            var busIds = devices.Select(x => x.BusId).ToArray();
            foreach (var busId in deviceKeys.GetSubKeyNames())
            {
                if (!busIds.Contains(busId))
                {
                    deviceKeys.DeleteSubKey(busId);
                }
            }
        }
    }
}
