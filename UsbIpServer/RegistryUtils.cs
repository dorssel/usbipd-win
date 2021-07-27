// SPDX-FileCopyrightText: Microsoft Corporation
//
// SPDX-License-Identifier: GPL-2.0-only

using System;
using System.Linq;
using System.Security.Principal;
using System;
using System.Globalization;
using Microsoft.Win32;

namespace UsbIpServer
{
    static class RegistryUtils
    {
        const string DevicesRegistryPath = @"SOFTWARE\usbipd-win";

        static class DeviceFilter
        {
            return Registry.LocalMachine.OpenSubKey(DevicesRegistryPath)?.GetSubKeyNames().Any(x => x == busId) ?? false;
        }

        public static bool IsDeviceShared(ExportedDevice device)
        {
            if (enable)
            { 
                Registry.LocalMachine.CreateSubKey($@"{DevicesRegistryPath}\{busId}");
            } else if (IsDeviceAvailable(busId))
            {
                Registry.LocalMachine.DeleteSubKey($@"{DevicesRegistryPath}\{busId}");
            }
        }

        public static string[] GetRegistryDevices()
        {
            return Registry.LocalMachine.OpenSubKey(DevicesRegistryPath)?.GetSubKeyNames() ?? Array.Empty<string>();
        }

        public static void InitializeRegistry()
        {
            var devices = Registry.LocalMachine.OpenSubKey(DevicesRegistryPath, true)
                ?? throw new UnexpectedResultException($"registry key missing; try reinstalling the product");
            foreach (var subKeyName in devices.GetSubKeyNames())
            {
                devices.DeleteSubKey(subKeyName);
            }
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
