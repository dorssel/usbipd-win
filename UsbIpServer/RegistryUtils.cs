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
        const string devicesRegistryPath = @"SOFTWARE\USBIPD-WIN";

        public static bool IsDeviceAvailable(string busId)
        {
            return Registry.LocalMachine.CreateSubKey(devicesRegistryPath).GetSubKeyNames().Any(x => x == busId);
        }

        public static void SetDeviceAvailability(string busId, bool enable)
        {
            if (enable)
            {
              
                var key = Registry.LocalMachine.CreateSubKey($@"{devicesRegistryPath}\{busId}");
                Console.WriteLine(key);
                Console.WriteLine("hello");
                Console.WriteLine(busId);
            } else
            {
                Console.WriteLine("bye");
                Registry.LocalMachine.DeleteSubKey($@"{devicesRegistryPath}\{busId}");
            }
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
    }
}
