// SPDX-FileCopyrightText: 2024 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

using System.ComponentModel;
using Windows.Win32;
using Windows.Win32.Devices.DeviceAndDriverInstallation;
using Windows.Win32.Foundation;
using Windows.Win32.System.Services;

namespace Usbipd;

sealed partial class CommandHandlers : ICommandHandlers
{
    Task<ExitCode> ICommandHandlers.InstallerInstallDriver(IConsole console, CancellationToken cancellationToken)
    {
        ConsoleTools.ReportInfo(console, "install_driver");
        var path = Path.GetDirectoryName(Environment.ProcessPath)!;
        ConsoleTools.ReportInfo(console, $"path = {path}");

        {
            ConsoleTools.ReportInfo(console, $"Installing VBoxUSB");
            // See: https://learn.microsoft.com/en-us/windows-hardware/drivers/install/preinstalling-driver-packages
            unsafe // DevSkim: ignore DS172412
            {
                fixed (char* inf = Path.Combine(path, "Drivers", "VBoxUSB.inf"))
                {
                    if (!PInvoke.SetupCopyOEMInf(inf, null, OEM_SOURCE_MEDIA_TYPE.SPOST_PATH, 0, null, 0))
                    {
                        console.ReportError($"SetupCopyOEMInf -> {new Win32Exception()}");
                        return Task.FromResult(ExitCode.Failure);
                    }
                }
            }
        }

        return Task.FromResult(ExitCode.Success);
    }

    Task<ExitCode> ICommandHandlers.InstallerUninstallDriver(IConsole console, CancellationToken cancellationToken)
    {
        ConsoleTools.ReportInfo(console, $"uninstall_driver");
        var path = Path.GetDirectoryName(Environment.ProcessPath)!;
        ConsoleTools.ReportInfo(console, $"path = {path}");

        var success = true;

        {
            ConsoleTools.ReportInfo(console, $"Uninstalling VBoxUSB");
            unsafe // DevSkim: ignore DS172412
            {
                BOOL needReboot;
                if (!PInvoke.DiUninstallDriver(HWND.Null, Path.Combine(path, "Drivers", "VBoxUSB.inf"), 0, &needReboot))
                {
                    console.ReportError($"DiUninstallDriver -> {new Win32Exception()}");
                    success = false;
                    // continue
                }
                // ignore needReboot
            }
        }

        return Task.FromResult(success ? ExitCode.Success : ExitCode.Failure);
    }

    Task<ExitCode> ICommandHandlers.InstallerInstallMonitor(IConsole console, CancellationToken cancellationToken)
    {
        ConsoleTools.ReportInfo(console, "install_monitor");
        var path = Path.GetDirectoryName(Environment.ProcessPath)!;
        ConsoleTools.ReportInfo(console, $"path = {path}");

        {
            ConsoleTools.ReportInfo(console, $"Installing VBoxUSBMon");
            // NOTE: This cannot be done from WiX, since WiX cannot create SERVICE_KERNEL_DRIVER; removal from WiX works though.
            using var manager = PInvoke.OpenSCManager(string.Empty, PInvoke.SERVICES_ACTIVE_DATABASE, PInvoke.SC_MANAGER_ALL_ACCESS);
            if (manager.IsInvalid)
            {
                console.ReportError($"OpenSCManager -> {new Win32Exception()}");
                return Task.FromResult(ExitCode.Failure);
            }
            unsafe // DevSkim: ignore DS172412
            {
                using var service = PInvoke.CreateService(manager, "VBoxUSBMon", "VirtualBox USB Monitor Service",
                    (uint)GENERIC_ACCESS_RIGHTS.GENERIC_ALL, ENUM_SERVICE_TYPE.SERVICE_KERNEL_DRIVER, SERVICE_START_TYPE.SERVICE_DEMAND_START,
                    SERVICE_ERROR.SERVICE_ERROR_NORMAL, Path.Combine(path, "Drivers", "VBoxUSBMon.sys"), null, null, null, null, null);
                if (service.IsInvalid)
                {
                    console.ReportError($"CreateService -> {new Win32Exception()}");
                    return Task.FromResult(ExitCode.Failure);
                }
            }
        }

        return Task.FromResult(ExitCode.Success);
    }
}
