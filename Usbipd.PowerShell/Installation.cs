// SPDX-FileCopyrightText: 2022 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-2.0-only

using System;
using System.IO;
using System.Management.Automation;
using Microsoft.Win32;

[assembly: CLSCompliant(false)]

namespace Usbipd.PowerShell
{
    static class Installation
    {
        public static string ExePath
        {
            get
            {
                // NOTE: User may be running 32-bit PowerShell, so we must explicitly ask for 64-bit registry.
                using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);

                string exeFile;
                if (root.OpenSubKey(@"SOFTWARE\usbipd-win") is not RegistryKey key
                    || key.GetValue("APPLICATIONFOLDER") is not string applicationFolder
                    || !Version.TryParse(key.GetValue("Version") as string, out var version)
                    || !File.Exists(exeFile = Path.Combine(Path.GetFullPath(applicationFolder), "usbipd.exe")))
                {
                    throw new ApplicationFailedException("usbipd-win is not installed.");
                }
                if (version != Version.Parse(GitVersionInformation.MajorMinorPatch))
                {
                    throw new ApplicationFailedException($"PowerShell module version {GitVersionInformation.MajorMinorPatch} does not match installed usbipd-win version {version}.");
                }
#if DEBUG
                return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetCallingAssembly().Location), @"..\..\..\..\..\UsbIpServer\bin\x64\Debug\net6.0-windows8.0\win-x64\usbipd.exe"));
#else
                return exeFile;
#endif
            }
        }
    }
}
