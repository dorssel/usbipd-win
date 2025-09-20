// SPDX-FileCopyrightText: 2024 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Usbipd.Automation;
using Windows.Win32;
using Windows.Win32.Devices.DeviceAndDriverInstallation;
using Windows.Win32.Foundation;
using Windows.Win32.System.Services;

namespace Usbipd;

sealed partial class CommandHandlers : ICommandHandlers
{
    Task < ExitCode> ICommandHandlers.InstallerInstallDriver(IConsole console, CancellationToken cancellationToken)
    {
        ConsoleTools.ReportInfo(console, "install_driver");

        {
            ConsoleTools.ReportInfo(console, $"Installing VBoxUSB");
            // See: https://learn.microsoft.com/en-us/windows-hardware/drivers/install/preinstalling-driver-packages
            unsafe // DevSkim: ignore DS172412
            {
                fixed (char* inf = DriverDetails.Instance.DriverPath)
                {
                    if (!PInvoke.SetupCopyOEMInf(inf, null, OEM_SOURCE_MEDIA_TYPE.SPOST_PATH, 0, null, 0))
                    {
                        console.ReportLastWin32Error(nameof(PInvoke.SetupCopyOEMInf));
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

        var success = true;
        var needReboot = false;

        unsafe // DevSkim: ignore DS172412
        {
            BOOL tmpNeedReboot;
            if (!PInvoke.DiUninstallDriver(HWND.Null, DriverDetails.Instance.DriverPath, 0, &tmpNeedReboot))
            {
                console.ReportLastWin32Error(nameof(PInvoke.DiUninstallDriver));
                success = false;
                // continue
            }
            if (tmpNeedReboot)
            {
                needReboot = true;
            }
        }

        // This is a best effort, we ignore reboot.
        console.ReportInfo($"Need reboot: {needReboot}");

        return Task.FromResult(success ? ExitCode.Success : ExitCode.Failure);
    }

    Task<ExitCode> ICommandHandlers.InstallerInstallMonitor(IConsole console, CancellationToken cancellationToken)
    {
        ConsoleTools.ReportInfo(console, "install_monitor");

        // NOTE: This cannot be done from WiX, since WiX cannot create SERVICE_KERNEL_DRIVER; removal from WiX works though.
        using var manager = PInvoke.OpenSCManager(string.Empty, PInvoke.SERVICES_ACTIVE_DATABASE, PInvoke.SC_MANAGER_ALL_ACCESS);
        if (manager.IsInvalid)
        {
            console.ReportLastWin32Error(nameof(PInvoke.OpenSCManager));
            return Task.FromResult(ExitCode.Failure);
        }
        unsafe // DevSkim: ignore DS172412
        {
            using var service = PInvoke.CreateService(manager, "VBoxUSBMon", "VirtualBox USB Monitor Service",
                (uint)GENERIC_ACCESS_RIGHTS.GENERIC_ALL, ENUM_SERVICE_TYPE.SERVICE_KERNEL_DRIVER, SERVICE_START_TYPE.SERVICE_DEMAND_START,
                SERVICE_ERROR.SERVICE_ERROR_NORMAL,
                Path.Combine(Path.GetDirectoryName(DriverDetails.Instance.DriverPath)!, "VBoxUSBMon.sys"),
                null, null, null, null, null);
            if (service.IsInvalid)
            {
                console.ReportLastWin32Error(nameof(PInvoke.CreateService));
                return Task.FromResult(ExitCode.Failure);
            }
        }

        return Task.FromResult(ExitCode.Success);
    }

    Task<ExitCode> ICommandHandlers.InstallerUninstallOldDrivers(IConsole console, CancellationToken cancellationToken)
    {
        ConsoleTools.ReportInfo(console, "uninstall_old_drivers");

        var success = true;
        var needReboot = false;

        using var deviceInfoSet = PInvoke.SetupDiCreateDeviceInfoList(DriverDetails.Instance.ClassGuid, default);
        if (deviceInfoSet.IsInvalid)
        {
            console.ReportLastWin32Error(nameof(PInvoke.SetupDiCreateDeviceInfoList));
            return Task.FromResult(ExitCode.Failure);
        }
        SP_DEVINSTALL_PARAMS_W deviceInstallParams = new()
        {
            cbSize = (uint)Marshal.SizeOf<SP_DEVINSTALL_PARAMS_W>(),
            FlagsEx = SETUP_DI_DEVICE_INSTALL_FLAGS_EX.DI_FLAGSEX_ALLOWEXCLUDEDDRVS,
        };
        if (!PInvoke.SetupDiSetDeviceInstallParams(deviceInfoSet, null, deviceInstallParams)) {
            console.ReportLastWin32Error(nameof(PInvoke.SetupDiSetDeviceInstallParams));
            return Task.FromResult(ExitCode.Failure);
        }
        unsafe // DevSkim: ignore DS172412
        {
            if (!PInvoke.SetupDiBuildDriverInfoList(deviceInfoSet, null, SETUP_DI_DRIVER_TYPE.SPDIT_CLASSDRIVER))
            {
                console.ReportLastWin32Error(nameof(PInvoke.SetupDiBuildDriverInfoList));
                return Task.FromResult(ExitCode.Failure);
            }
        }
        SP_DRVINFO_DATA_V2_W driverInfoData = new()
        {
            cbSize = (uint)Marshal.SizeOf<SP_DRVINFO_DATA_V2_W>(),
        };
        for (var index = 0u; ; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!PInvoke.SetupDiEnumDriverInfo(deviceInfoSet, null, SETUP_DI_DRIVER_TYPE.SPDIT_CLASSDRIVER, index, ref driverInfoData))
            {
                if ((WIN32_ERROR)Marshal.GetLastPInvokeError() == WIN32_ERROR.ERROR_NO_MORE_ITEMS)
                {
                    break;
                }
                console.ReportLastWin32Error(nameof(PInvoke.SetupDiEnumDriverInfo));
                return Task.FromResult(ExitCode.Failure);
            }
            unsafe // DevSkim: ignore DS172412
            {
                var requiredSize = 0u;
                if (!PInvoke.SetupDiGetDriverInfoDetail(deviceInfoSet, null, driverInfoData, null, 0, &requiredSize))
                {
                    if ((WIN32_ERROR)Marshal.GetLastPInvokeError() != WIN32_ERROR.ERROR_INSUFFICIENT_BUFFER)
                    {
                        console.ReportLastWin32Error(nameof(PInvoke.SetupDiGetDriverInfoDetail));
                        success = false;
                        continue;
                    }
                }
                var buffer = new byte[requiredSize];
                fixed (byte* bufferPointer = buffer)
                {
                    var details = (SP_DRVINFO_DETAIL_DATA_W*)bufferPointer;
                    details->cbSize = (uint)Marshal.SizeOf<SP_DRVINFO_DETAIL_DATA_W>();
                    if (!PInvoke.SetupDiGetDriverInfoDetail(deviceInfoSet, null, driverInfoData, details, requiredSize, null))
                    {
                        console.ReportLastWin32Error(nameof(PInvoke.SetupDiGetDriverInfoDetail));
                        success = false;
                        continue;
                    }
                    var hardwareId = new string((char*)&details->HardwareID);
                    if (!VidPid.TryParseId(hardwareId, out var vidPid))
                    {
                        // This can happen for other drivers -> not a failure.
                        continue;
                    }
                    if (vidPid != DriverDetails.Instance.VidPid)
                    {
                        // This is not a VBoxUSB driver -> don't touch it.
                        continue;
                    }
                    var version = new Version(
                        (int)((driverInfoData.DriverVersion >> 48) & 0xffff),
                        (int)((driverInfoData.DriverVersion >> 32) & 0xffff),
                        (int)((driverInfoData.DriverVersion >> 16) & 0xffff),
                        (int)(driverInfoData.DriverVersion & 0xffff));
                    if (version == DriverDetails.Instance.Version)
                    {
                        // This is the current VBoxUSB driver -> don't remove it.
                        continue;
                    }
                    ConsoleTools.ReportInfo(console, $"Removing old version: {version}, {details->InfFileName}");
                    BOOL tmpNeedReboot;
                    if (!PInvoke.DiUninstallDriver(HWND.Null, details->InfFileName.ToString(), 0, &tmpNeedReboot))
                    {
                        console.ReportLastWin32Error(nameof(PInvoke.DiUninstallDriver));
                        success = false;
                        continue;
                    }
                    if (tmpNeedReboot)
                    {
                        needReboot = true;
                    }
                }
            }
        }

        // This is a best effort, we ignore reboot.
        console.ReportInfo($"Need reboot: {needReboot}");

        return Task.FromResult(success ? ExitCode.Success : ExitCode.Failure);
    }

    Task<ExitCode> ICommandHandlers.InstallerUpdateDrivers(IConsole console, CancellationToken cancellationToken)
    {
        ConsoleTools.ReportInfo(console, "update_drivers");

        var success = true;
        var needReboot = false;

        var instances = new List<string>();
        {
            // Get all devices that exist, including stub devices, present or not, in any hardware profile (i.e., *really* all of them).
            var devInfo = new SP_DEVINFO_DATA()
            {
                cbSize = (uint)Unsafe.SizeOf<SP_DEVINFO_DATA>(),
            };
            using var deviceInfoList = PInvoke.SetupDiGetClassDevs(null, null!, HWND.Null, SETUP_DI_GET_CLASS_DEVS_FLAGS.DIGCF_ALLCLASSES);
            for (uint memberIndex = 0; ; memberIndex++)
            {
                if (!PInvoke.SetupDiEnumDeviceInfo(deviceInfoList, memberIndex, ref devInfo))
                {
                    if ((WIN32_ERROR)Marshal.GetLastPInvokeError() == WIN32_ERROR.ERROR_NO_MORE_ITEMS)
                    {
                        break;
                    }
                    console.ReportLastWin32Error(nameof(PInvoke.SetupDiEnumDeviceInfo));
                    success = false;
                    break;
                }
                if (!ConfigurationManager.HasVBoxDriver(devInfo.DevInst))
                {
                    continue;
                }
                var instanceId = (string)ConfigurationManager.Get_DevNode_Property(devInfo.DevInst, PInvoke.DEVPKEY_Device_InstanceId);
                instances.Add(instanceId);
            }
        }

        foreach (var instanceId in instances)
        {
            cancellationToken.ThrowIfCancellationRequested();
            console.ReportInfo($"Updating device: {instanceId}");
            try
            {
                if (DriverTools.ForceVBoxDriver(instanceId))
                {
                    needReboot = true;
                }
            }
            catch (Win32Exception e)
            {
                console.ReportError($"Failed to update device {instanceId}: {e.Message}");
                success = false;
                continue;
            }
        }

        // This is a best effort, we ignore reboot.
        console.ReportInfo($"Need reboot: {needReboot}");

        return Task.FromResult(success ? ExitCode.Success : ExitCode.Failure);
    }

    /// <summary>
    /// Uninstalls all stub devices. This assumes everything is detached at this point.
    /// This is used by the uninstaller.
    /// This is also used by the installer, to ensure that any future attaches use the newly installed driver.
    /// </summary>
    Task<ExitCode> ICommandHandlers.InstallerUninstallStubs(IConsole console, CancellationToken cancellationToken)
    {
        ConsoleTools.ReportInfo(console, "uninstall_stubs");

        var success = true;
        var needReboot = false;

        unsafe // DevSkim: ignore DS172412
        {
            string[] instanceIds;

            PInvoke.CM_Get_Device_ID_List_Size(out var size, DriverDetails.Instance.ClassGuid.ToString("B"), PInvoke.CM_GETIDLIST_FILTER_CLASS)
                .ThrowOnError(nameof(PInvoke.CM_Get_Device_ID_List_Size));

            fixed (char* buf = stackalloc char[(int)size])
            {
                PInvoke.CM_Get_Device_ID_List(DriverDetails.Instance.ClassGuid.ToString("B"), buf, size, PInvoke.CM_GETIDLIST_FILTER_CLASS)
                    .ThrowOnError(nameof(PInvoke.CM_Get_Device_ID_List_Size));
                // The list is double-NUL terminated.
                instanceIds = new string(buf, 0, (int)size - 2).Split('\0');
            }

            foreach (var instanceId in instanceIds)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!VidPid.TryParseId(instanceId, out var vidPid) || vidPid != DriverDetails.Instance.VidPid)
                {
                    // Not a stub device.
                    continue;
                }

                console.ReportInfo($"Uninstalling stub device: {instanceId}");

                try
                {
                    if (DriverTools.UninstallStubDevice(instanceId))
                    {
                        needReboot = true;
                    }
                }
                catch (Win32Exception e)
                {
                    console.ReportError(e.Message);
                    success = false;
                }
            }
        }

        // This is a best effort, we ignore reboot.
        console.ReportInfo($"Need reboot: {needReboot}");

        return Task.FromResult(success ? ExitCode.Success : ExitCode.Failure);
    }

    /// <summary>
    /// Disable all forced devices.
    /// This is used during update. It ensures VBoxUSBMon can stop while still keeping the driver forced.
    /// </summary>
    Task<ExitCode> ICommandHandlers.InstallerDisableForced(IConsole console, CancellationToken cancellationToken)
    {
        ConsoleTools.ReportInfo(console, "disable_forced");

        var success = true;

        unsafe // DevSkim: ignore DS172412
        {
            string[] instanceIds;

            PInvoke.CM_Get_Device_ID_List_Size(out var size, DriverDetails.Instance.ClassGuid.ToString("B"), PInvoke.CM_GETIDLIST_FILTER_CLASS)
                .ThrowOnError(nameof(PInvoke.CM_Get_Device_ID_List_Size));

            fixed (char* buf = stackalloc char[(int)size])
            {
                PInvoke.CM_Get_Device_ID_List(DriverDetails.Instance.ClassGuid.ToString("B"), buf, size, PInvoke.CM_GETIDLIST_FILTER_CLASS)
                    .ThrowOnError(nameof(PInvoke.CM_Get_Device_ID_List_Size));
                // The list is double-NUL terminated.
                instanceIds = new string(buf, 0, (int)size - 2).Split('\0');
            }

            foreach (var instanceId in instanceIds)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!ConfigurationManager.HasVBoxDriver(instanceId))
                {
                    continue;
                }
                // NOTE: We don't need to disable devices that are not present.
                // NOTE: This will also skip left-over stub devices that a) should have been uninstalled already, and b) should not be running at this point.
                uint devNode;
                CONFIGRET configRet;
                fixed (char* pInstanceId = instanceId)
                {
                    configRet = PInvoke.CM_Locate_DevNode(out devNode, pInstanceId, CM_LOCATE_DEVNODE_FLAGS.CM_LOCATE_DEVNODE_NORMAL);
                    if (configRet != CONFIGRET.CR_SUCCESS)
                    {
                        // Device is not present, so it isn't using the driver. We can ignore it.
                        continue;
                    }
                }
                console.ReportInfo($"Disabling device: {instanceId}");
                configRet = PInvoke.CM_Disable_DevNode(devNode, PInvoke.CM_DISABLE_UI_NOT_OK);
                if (configRet != CONFIGRET.CR_SUCCESS)
                {
                    console.ReportConfigRet(nameof(PInvoke.CM_Disable_DevNode), configRet);
                    success = false;
                }
            }
        }

        return Task.FromResult(success ? ExitCode.Success : ExitCode.Failure);
    }

    /// <summary>
    /// Enable all forced devices.
    /// This is used during (or rather: after) update.
    /// </summary>
    Task<ExitCode> ICommandHandlers.InstallerEnableForced(IConsole console, CancellationToken cancellationToken)
    {
        ConsoleTools.ReportInfo(console, "enable_forced");

        var success = true;

        unsafe // DevSkim: ignore DS172412
        {
            string[] instanceIds;

            PInvoke.CM_Get_Device_ID_List_Size(out var size, DriverDetails.Instance.ClassGuid.ToString("B"), PInvoke.CM_GETIDLIST_FILTER_CLASS)
                .ThrowOnError(nameof(PInvoke.CM_Get_Device_ID_List_Size));

            fixed (char* buf = stackalloc char[(int)size])
            {
                PInvoke.CM_Get_Device_ID_List(DriverDetails.Instance.ClassGuid.ToString("B"), buf, size, PInvoke.CM_GETIDLIST_FILTER_CLASS)
                    .ThrowOnError(nameof(PInvoke.CM_Get_Device_ID_List_Size));
                // The list is double-NUL terminated.
                instanceIds = new string(buf, 0, (int)size - 2).Split('\0');
            }

            foreach (var instanceId in instanceIds)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // We will enable all devices that are present, disabled, and use the VBoxUSB driver.
                // At this point, that should only be the forced devices that are actually present.
                if (!ConfigurationManager.HasVBoxDriver(instanceId))
                {
                    continue;
                }
                uint devNode;
                CONFIGRET configRet;
                fixed (char* pInstanceId = instanceId)
                {
                    configRet = PInvoke.CM_Locate_DevNode(out devNode, pInstanceId, CM_LOCATE_DEVNODE_FLAGS.CM_LOCATE_DEVNODE_NORMAL);
                }
                if (configRet != CONFIGRET.CR_SUCCESS)
                {
                    // If the device is not present, it is not even possible to enable/disable/status.
                    if (configRet != CONFIGRET.CR_NO_SUCH_DEVINST)
                    {
                        console.ReportConfigRet(nameof(PInvoke.CM_Locate_DevNode), configRet);
                        success = false;
                    }
                    continue;
                }
                configRet = PInvoke.CM_Get_DevNode_Status(out var status, out var problemCode, devNode, 0);
                if (configRet != CONFIGRET.CR_SUCCESS)
                {
                    console.ReportConfigRet(nameof(PInvoke.CM_Get_DevNode_Status), configRet);
                    success = false;
                    continue;
                }
                if (!status.HasFlag(CM_DEVNODE_STATUS_FLAGS.DN_HAS_PROBLEM) || !problemCode.HasFlag(CM_PROB.CM_PROB_DISABLED))
                {
                    // Device is not disabled.
                    continue;
                }
                console.ReportInfo($"Enabling device: {instanceId}");
                configRet = PInvoke.CM_Enable_DevNode(devNode, 0);
                if (configRet != CONFIGRET.CR_SUCCESS)
                {
                    console.ReportConfigRet(nameof(PInvoke.CM_Enable_DevNode), configRet);
                    success = false;
                }
            }
        }

        return Task.FromResult(success ? ExitCode.Success : ExitCode.Failure);
    }
}
