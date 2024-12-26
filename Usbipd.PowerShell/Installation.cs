// SPDX-FileCopyrightText: 2022 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

using System.Diagnostics;
using System.Management.Automation;
using System.Text;
using System.Text.RegularExpressions;

namespace Usbipd.PowerShell;

static class Installation
{
    static string GetRegistryStringValue(string regOutput, string valueName)
    {
        // Example regOutput:
        //
        // HKEY_LOCAL_MACHINE\SOFTWARE\usbipd-win
        //     APPLICATIONFOLDER    REG_SZ    C:\Program Files\usbipd-win\
        //     Version REG_SZ       3.0.0
        //
        // HKEY_LOCAL_MACHINE\SOFTWARE\usbipd-win\Devices

        var match = Regex.Match(regOutput, @$"^\s*{valueName}\s+REG_SZ\s+(.*)$", RegexOptions.Multiline);
        return match.Success ? match.Groups[1].Value.TrimEnd() : throw new ApplicationFailedException("usbipd-win is not installed.");
    }

    public static string ExePath
    {
        get
        {
            // NOTE: User may be running 32-bit PowerShell, so we must explicitly ask for 64-bit registry.
            var regExe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "reg.exe");

            var startInfo = new ProcessStartInfo
            {
                FileName = regExe,
                Arguments = @"query HKLM\SOFTWARE\usbipd-win /reg:64",
                UseShellExecute = false,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            using var process = Process.Start(startInfo) ?? throw new ApplicationFailedException($"Cannot execute '{regExe}'.");
            var stdout = string.Empty;
            var stderr = string.Empty;

            var captureTasks = new[]
            {
                Task.Run(async () => stdout = await process.StandardOutput.ReadToEndAsync()),
                Task.Run(async () => stderr = await process.StandardError.ReadToEndAsync()),
            };

            process.WaitForExit();
            Task.WhenAll(captureTasks).Wait();

            if (process.ExitCode != 0)
            {
                throw new ApplicationFailedException($"reg.exe failed with exit code {process.ExitCode}.");
            }
            if (!string.IsNullOrEmpty(stderr))
            {
                throw new ApplicationFailedException($"reg.exe returned unexpected error text:\n\n{stderr}");
            }

            var applicationFolder = GetRegistryStringValue(stdout, "APPLICATIONFOLDER");
            var exeFile = Path.Combine(Path.GetFullPath(applicationFolder), "usbipd.exe");
            if (!Version.TryParse(GetRegistryStringValue(stdout, "Version"), out var version) || !File.Exists(exeFile))
            {
                throw new ApplicationFailedException("usbipd-win is not installed.");
            }
            if (version != Version.Parse(GitVersionInformation.MajorMinorPatch))
            {
                throw new ApplicationFailedException(
                    $"PowerShell module version {GitVersionInformation.MajorMinorPatch} does not match installed usbipd-win version {version}.");
            }
#if DEBUG
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetCallingAssembly().Location),
                @"..\..\..\..\Usbipd\bin\x64\Debug\net9.0-windows10.0.17763\win-x64\usbipd.exe"));
#else
            return exeFile;
#endif
        }
    }
}
