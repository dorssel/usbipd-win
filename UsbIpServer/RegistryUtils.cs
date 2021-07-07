using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32;
using System.Linq;
using System.Threading;

namespace UsbIpServer
{
    class RegistryUtils
    {
        public struct RegistryDevice
        {
            public string busId;
            public string vendorId;
            public string productId;
            public bool isAvailable;
        }

        const string devicesRegistryPath = @"SOFTWARE\USBIPD-WIN";

        public static RegistryDevice[] getRegistryDevices()
        {
            var registryDevices = new List<RegistryDevice>();
            var devices = Registry.LocalMachine.CreateSubKey(devicesRegistryPath);
            var deviceIds = devices.GetSubKeyNames();
            foreach (var id in deviceIds)
            {   
                var d = devices.OpenSubKey(id);
                RegistryDevice registryDevice;
                registryDevice.busId = id;
                registryDevice.productId = (string)d.GetValue("product-id");
                registryDevice.vendorId = (string)d.GetValue("vendor-id");
                registryDevice.isAvailable = (string)d.GetValue("available") == "True" ;
                registryDevices.Add(registryDevice);
            }

            return registryDevices.ToArray();
        }

        public static HashSet<string> getAvailableDevicesIds()
        {
            return getRegistryDevices().Where(x => x.isAvailable).Select(x=> x.busId).ToHashSet();
        }

        public static void enableRegistryDevice(string busId)
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

        public static void disableRegistryDevice(string busId)
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

        public static async Task updateRegistry(CancellationToken stoppingToken)
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
