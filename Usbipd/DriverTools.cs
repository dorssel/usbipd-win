// SPDX-FileCopyrightText: 2021 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

using System.ComponentModel;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Devices.DeviceAndDriverInstallation;
using Windows.Win32.Foundation;

namespace Usbipd;

static class DriverTools
{
    /// <summary>
    /// Remove any driver for the given device.
    ///
    /// This is a helper function that is used both when forcing the driver and when restoring the default driver.
    /// </summary>
    /// <returns>true if a reboot is required.</returns>
    /// <exception cref="Win32Exception">On failure.</exception>
    public static bool SetNullDriver(WindowsDevice device)
    {
        unsafe // DevSkim: ignore DS172412
        {
            using var deviceInfoList = PInvoke.SetupDiCreateDeviceInfoList(null, default);
            if (deviceInfoList.IsInvalid)
            {
                Tools.ThrowWin32Error(nameof(PInvoke.SetupDiCreateDeviceInfoList));
            }
            var deviceInfoData = new SP_DEVINFO_DATA()
            {
                cbSize = (uint)Marshal.SizeOf<SP_DEVINFO_DATA>(),
            };
            PInvoke.SetupDiOpenDeviceInfo(deviceInfoList, device.InstanceId, default, 0, &deviceInfoData)
                .ThrowOnWin32Error(nameof(PInvoke.SetupDiOpenDeviceInfo));
            BOOL reboot;
            PInvoke.DiInstallDevice(default, deviceInfoList, deviceInfoData, null, DIINSTALLDEVICE_FLAGS.DIIDFLAG_INSTALLNULLDRIVER, &reboot)
                .ThrowOnWin32Error(nameof(DIINSTALLDEVICE_FLAGS.DIIDFLAG_INSTALLNULLDRIVER));
            return reboot;
        }
    }

    /// <summary>
    /// Force the driver for the given device to the current VBoxUsb driver.
    ///
    /// This is a helper function that is used both when forcing the driver (requires setting a NULL driver first)
    /// and when updating the driver.
    /// </summary>
    /// <returns>true if a reboot is required.</returns>
    /// <exception cref="Win32Exception">On failure.</exception>
    public static bool SetVBoxDriver(WindowsDevice device)
    {
        unsafe // DevSkim: ignore DS172412
        {
            using var deviceInfoList = PInvoke.SetupDiCreateDeviceInfoList(null, default);
            if (deviceInfoList.IsInvalid)
            {
                Tools.ThrowWin32Error(nameof(PInvoke.SetupDiCreateDeviceInfoList));
            }
            var deviceInfoData = new SP_DEVINFO_DATA()
            {
                cbSize = (uint)Marshal.SizeOf<SP_DEVINFO_DATA>(),
            };
            PInvoke.SetupDiOpenDeviceInfo(deviceInfoList, device.InstanceId, default, 0, &deviceInfoData)
                .ThrowOnWin32Error(nameof(PInvoke.SetupDiOpenDeviceInfo));
            var deviceInstallParams = new SP_DEVINSTALL_PARAMS_W()
            {
                cbSize = (uint)Marshal.SizeOf<SP_DEVINSTALL_PARAMS_W>(),
                Flags = SETUP_DI_DEVICE_INSTALL_FLAGS.DI_ENUMSINGLEINF,
                FlagsEx = SETUP_DI_DEVICE_INSTALL_FLAGS_EX.DI_FLAGSEX_ALLOWEXCLUDEDDRVS,
                DriverPath = DriverDetails.Instance.DriverPath,
            };
            PInvoke.SetupDiSetDeviceInstallParams(deviceInfoList, deviceInfoData, deviceInstallParams)
                .ThrowOnWin32Error(nameof(PInvoke.SetupDiSetDeviceInstallParams));
            PInvoke.SetupDiBuildDriverInfoList(deviceInfoList, &deviceInfoData, SETUP_DI_DRIVER_TYPE.SPDIT_CLASSDRIVER)
                .ThrowOnWin32Error(nameof(PInvoke.SetupDiBuildDriverInfoList));
            var driverInfoData = new SP_DRVINFO_DATA_V2_W()
            {
                cbSize = (uint)Marshal.SizeOf<SP_DRVINFO_DATA_V2_W>(),
            };
            PInvoke.SetupDiEnumDriverInfo(deviceInfoList, deviceInfoData, SETUP_DI_DRIVER_TYPE.SPDIT_CLASSDRIVER, 0, ref driverInfoData)
                .ThrowOnWin32Error(nameof(PInvoke.SetupDiEnumDriverInfo));
            BOOL reboot;
            PInvoke.DiInstallDevice(default, deviceInfoList, deviceInfoData, driverInfoData, 0, &reboot)
                .ThrowOnWin32Error(nameof(PInvoke.DiInstallDevice));

            // Best effort, not really a problem if this fails.
            _ = device.SetFriendlyName();

            return reboot;
        }
    }

    public static bool ForceVBoxDriver(WindowsDevice device)
    {
        var reboot = false;

        // First, we must set a NULL driver to clear any existing Device Setup Class.
        if (SetNullDriver(device))
        {
            reboot = true;
        }

        // For some devices (Google Pixel) it takes a while before the driver can be set again.
        // 200 ms seems to work, so delay for 500 ms for good measure...
        Thread.Sleep(TimeSpan.FromMilliseconds(500));

        if (SetVBoxDriver(device))
        {
            reboot = true;
        }

        return reboot;
    }

    public static bool UnforceVBoxDriver(WindowsDevice device)
    {
        if (!device.HasVBoxDriver)
        {
            // The device does not have the VBoxUsb driver installed ... we're done.
            return false;
        }

        var reboot = false;

        // First, we must set a NULL driver, just in case no default driver exists.
        if (SetNullDriver(device))
        {
            reboot = true;
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
                Tools.ThrowWin32Error(nameof(PInvoke.SetupDiCreateDeviceInfoList));
            }
            var deviceInfoData = new SP_DEVINFO_DATA()
            {
                cbSize = (uint)Marshal.SizeOf<SP_DEVINFO_DATA>(),
            };
            PInvoke.SetupDiOpenDeviceInfo(deviceInfoSet, device.InstanceId, default, 0, &deviceInfoData)
                .ThrowOnWin32Error(nameof(PInvoke.SetupDiOpenDeviceInfo));
            try
            {
                BOOL tmpReboot;
                PInvoke.DiInstallDevice(default, deviceInfoSet, deviceInfoData, null, 0, &tmpReboot).ThrowOnWin32Error(nameof(PInvoke.DiInstallDevice));
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

    public static bool UninstallStubDevice(WindowsDevice device)
    {
        var reboot = false;

        unsafe // DevSkim: ignore DS172412
        {
            using var deviceInfoSet = PInvoke.SetupDiCreateDeviceInfoList(null, default);
            if (deviceInfoSet.IsInvalid)
            {
                Tools.ThrowWin32Error(nameof(PInvoke.SetupDiCreateDeviceInfoList));
            }
            var deviceInfoData = new SP_DEVINFO_DATA()
            {
                cbSize = (uint)Marshal.SizeOf<SP_DEVINFO_DATA>(),
            };
            PInvoke.SetupDiOpenDeviceInfo(deviceInfoSet, device.InstanceId, default, 0, &deviceInfoData)
                .ThrowOnWin32Error(nameof(PInvoke.SetupDiOpenDeviceInfo));
            BOOL tmpReboot;
            PInvoke.DiUninstallDevice(default, deviceInfoSet, deviceInfoData, 0, &tmpReboot).ThrowOnWin32Error(nameof(PInvoke.DiUninstallDevice));
            if (tmpReboot)
            {
                reboot = true;
            }
        }

        return reboot;
    }
}
