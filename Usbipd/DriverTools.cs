// SPDX-FileCopyrightText: 2021 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Usbipd.Automation;
using Windows.Win32;
using Windows.Win32.Devices.DeviceAndDriverInstallation;
using Windows.Win32.Foundation;

namespace Usbipd;

static class DriverTools
{
    public static bool ForceVBoxDriver(string originalInstanceId)
    {
        BOOL reboot = false;
        {
            // First, we must set a NULL driver to clear any existing Device Setup Class.
            using var deviceInfoSet = PInvoke.SetupDiCreateDeviceInfoList(null, default);
            if (deviceInfoSet.IsInvalid)
            {
                throw new Win32Exception(nameof(PInvoke.SetupDiCreateDeviceInfoList));
            }
            var deviceInfoData = new SP_DEVINFO_DATA()
            {
                cbSize = (uint)Marshal.SizeOf<SP_DEVINFO_DATA>(),
            };
            unsafe // DevSkim: ignore DS172412
            {
                PInvoke.SetupDiOpenDeviceInfo(deviceInfoSet, originalInstanceId, default, 0, &deviceInfoData)
                    .ThrowOnError(nameof(PInvoke.SetupDiOpenDeviceInfo));
            }
            BOOL tmpReboot;
            unsafe // DevSkim: ignore DS172412
            {
                PInvoke.DiInstallDevice(default, deviceInfoSet, deviceInfoData, null, DIINSTALLDEVICE_FLAGS.DIIDFLAG_INSTALLNULLDRIVER, &tmpReboot)
                    .ThrowOnError(nameof(DIINSTALLDEVICE_FLAGS.DIIDFLAG_INSTALLNULLDRIVER));
            }
            if (tmpReboot)
            {
                reboot = true;
            }
        }

        // For some devices (Google Pixel) it takes a while before the driver can be set again.
        // 200 ms seems to work, so delay for 500 ms for good measure...
        Thread.Sleep(TimeSpan.FromMilliseconds(500));

        {
            // Now we can update the driver.
            using var deviceInfoSet = PInvoke.SetupDiCreateDeviceInfoList(null, default);
            if (deviceInfoSet.IsInvalid)
            {
                throw new Win32Exception(nameof(PInvoke.SetupDiCreateDeviceInfoList));
            }
            var deviceInfoData = new SP_DEVINFO_DATA()
            {
                cbSize = (uint)Marshal.SizeOf<SP_DEVINFO_DATA>(),
            };
            unsafe // DevSkim: ignore DS172412
            {
                PInvoke.SetupDiOpenDeviceInfo(deviceInfoSet, originalInstanceId, default, 0, &deviceInfoData)
                    .ThrowOnError(nameof(PInvoke.SetupDiOpenDeviceInfo));
            }
            var deviceInstallParams = new SP_DEVINSTALL_PARAMS_W()
            {
                cbSize = (uint)Marshal.SizeOf<SP_DEVINSTALL_PARAMS_W>(),
                Flags = SETUP_DI_DEVICE_INSTALL_FLAGS.DI_ENUMSINGLEINF,
                FlagsEx = SETUP_DI_DEVICE_INSTALL_FLAGS_EX.DI_FLAGSEX_ALLOWEXCLUDEDDRVS,
                DriverPath = @$"{RegistryUtilities.InstallationFolder ?? throw new UnexpectedResultException("not installed")}\Drivers\VBoxUSB.inf",
            };
            PInvoke.SetupDiSetDeviceInstallParams(deviceInfoSet, deviceInfoData, deviceInstallParams)
                .ThrowOnError(nameof(PInvoke.SetupDiSetDeviceInstallParams));
            unsafe // DevSkim: ignore DS172412
            {
                PInvoke.SetupDiBuildDriverInfoList(deviceInfoSet, &deviceInfoData, SETUP_DI_DRIVER_TYPE.SPDIT_CLASSDRIVER)
                    .ThrowOnError(nameof(PInvoke.SetupDiBuildDriverInfoList));
            }
            var driverInfoData = new SP_DRVINFO_DATA_V2_W()
            {
                cbSize = (uint)Marshal.SizeOf<SP_DRVINFO_DATA_V2_W>(),
            };
            PInvoke.SetupDiEnumDriverInfo(deviceInfoSet, deviceInfoData, SETUP_DI_DRIVER_TYPE.SPDIT_CLASSDRIVER, 0, ref driverInfoData)
                .ThrowOnError(nameof(PInvoke.SetupDiEnumDriverInfo));
            BOOL tmpReboot;
            unsafe // DevSkim: ignore DS172412
            {
                PInvoke.DiInstallDevice(default, deviceInfoSet, deviceInfoData, driverInfoData, 0, &tmpReboot).ThrowOnError(nameof(PInvoke.DiInstallDevice));
            }
            if (tmpReboot)
            {
                reboot = true;
            }
            ConfigurationManager.SetDeviceFriendlyName(deviceInfoData.DevInst);
        }
        return reboot;
    }

    public static bool UnforceVBoxDriver(string originalInstanceId)
    {
        if (!ConfigurationManager.HasVBoxDriver(originalInstanceId))
        {
            // The device does not have the VBoxUsb driver installed ... we're done.
            return false;
        }

        BOOL reboot = false;
        unsafe // DevSkim: ignore DS172412
        {
            // First, we must set a NULL driver, just in case no default driver exists.
            using var deviceInfoSet = PInvoke.SetupDiCreateDeviceInfoList(null, default);
            if (deviceInfoSet.IsInvalid)
            {
                throw new Win32Exception(nameof(PInvoke.SetupDiCreateDeviceInfoList));
            }
            var deviceInfoData = new SP_DEVINFO_DATA()
            {
                cbSize = (uint)Marshal.SizeOf<SP_DEVINFO_DATA>(),
            };
            PInvoke.SetupDiOpenDeviceInfo(deviceInfoSet, originalInstanceId, default, 0, &deviceInfoData).ThrowOnError(nameof(PInvoke.SetupDiOpenDeviceInfo));
            BOOL tmpReboot;
            PInvoke.DiInstallDevice(default, deviceInfoSet, deviceInfoData, null, DIINSTALLDEVICE_FLAGS.DIIDFLAG_INSTALLNULLDRIVER, &tmpReboot)
                .ThrowOnError(nameof(DIINSTALLDEVICE_FLAGS.DIIDFLAG_INSTALLNULLDRIVER));
            if (tmpReboot)
            {
                reboot = true;
            }
        }

        // For some devices (Google Pixel) it takes a while before the driver can be set again.
        // 200 ms seems to work, so delay for 500 ms for good measure...
        Thread.Sleep(TimeSpan.FromMilliseconds(500));

        unsafe // DevSkim: ignore DS172412
        {
            // Now we let Windows install the default PnP driver.
            // We don't fail if no such driver can be found.
            using var deviceInfoSet = PInvoke.SetupDiCreateDeviceInfoList(null, default);
            if (deviceInfoSet.IsInvalid)
            {
                throw new Win32Exception(nameof(PInvoke.SetupDiCreateDeviceInfoList));
            }
            var deviceInfoData = new SP_DEVINFO_DATA()
            {
                cbSize = (uint)Marshal.SizeOf<SP_DEVINFO_DATA>(),
            };
            PInvoke.SetupDiOpenDeviceInfo(deviceInfoSet, originalInstanceId, default, 0, &deviceInfoData).ThrowOnError(nameof(PInvoke.SetupDiOpenDeviceInfo));
            try
            {
                BOOL tmpReboot;
                PInvoke.DiInstallDevice(default, deviceInfoSet, deviceInfoData, null, 0, &tmpReboot).ThrowOnError(nameof(PInvoke.DiInstallDevice));
                if (tmpReboot)
                {
                    reboot = true;
                }
            }
            catch (Win32Exception ex) when ((WIN32_ERROR)ex.NativeErrorCode == WIN32_ERROR.ERROR_NO_DRIVER_SELECTED)
            {
                // Not really an error; this just means Windows does not have a default PnP driver for it.
                // The device will be listed under "Other devices" with a question mark.
            }
        }
        return reboot;
    }

    /// <summary>
    /// Uninstalls all stub devices. This assumes everything is detached at this point.
    /// This is used by the uninstaller.
    /// This is also used by the installer, to ensure that any future attaches use the newly installed driver.
    /// </summary>
    /// <returns>True if a reboot is required.</returns>
    public static bool DeleteStubDevices(TextWriter log)
    {
        var reboot = false;

        // Get all devices that exist, including mock devices, present or not, in any hardware profile (i.e., *really* all of them).
        var devInfo = new SP_DEVINFO_DATA()
        {
            cbSize = (uint)Unsafe.SizeOf<SP_DEVINFO_DATA>(),
        };
        using var usbDevices = PInvoke.SetupDiGetClassDevs(null, null!, HWND.Null, SETUP_DI_GET_CLASS_DEVS_FLAGS.DIGCF_ALLCLASSES);
        for (uint memberIndex = 0; PInvoke.SetupDiEnumDeviceInfo(usbDevices, memberIndex, ref devInfo); memberIndex++)
        {
            if (!ConfigurationManager.HasVBoxDriver(devInfo.DevInst))
            {
                continue;
            }
            string instanceId;
            try
            {
                instanceId = (string)ConfigurationManager.Get_DevNode_Property(devInfo.DevInst, PInvoke.DEVPKEY_Device_InstanceId);
            }
            catch (ConfigurationManagerException)
            {
                // Device disappeared in the meantime; ignore it.
                continue;
            }
            if (!VidPid.TryParseId(instanceId, out var vidPid) || vidPid != Interop.VBoxUsb.Stub)
            {
                // Not a stub device.
                continue;
            }

            log.WriteLine($"Uninstalling stub device: {instanceId}");

            BOOL tmpReboot;
            unsafe // DevSkim: ignore DS172412
            {
                // This is a best-effort attempt; we ignore errors.
                if (!PInvoke.DiUninstallDevice(default, usbDevices, devInfo, 0, &tmpReboot))
                {
                    log.WriteLine($"Uninstall failed: {Marshal.GetLastPInvokeError()}: {Marshal.GetLastPInvokeErrorMessage()}");
                }
                else if (tmpReboot)
                {
                    log.WriteLine($"Uninstall requires reboot");
                    reboot = true;
                }
            }
        }

        return reboot;
    }
}
