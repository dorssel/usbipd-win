using System.Linq;
using Microsoft.Win32;
using System.Security.Principal;

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
                Registry.LocalMachine.CreateSubKey($@"{devicesRegistryPath}\{busId}");
            } else
            {
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

        public static bool HasRegistryAccess()
        {
            bool isElevated;
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            {
                WindowsPrincipal principal = new WindowsPrincipal(identity);
                isElevated = principal.IsInRole(WindowsBuiltInRole.Administrator);
            }

            return isElevated;
        }
    }
}
