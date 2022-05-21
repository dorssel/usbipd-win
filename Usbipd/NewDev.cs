// SPDX-FileCopyrightText: 2021 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-2.0-only

using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using Windows.Win32;
using Windows.Win32.Devices.DeviceAndDriverInstallation;
using Windows.Win32.Foundation;

namespace Usbipd;

static class NewDev
{
    public static bool ForceVBoxDriver(string originalInstanceId)
    {
        BOOL reboot = false;
        unsafe
        {
            // First, we must set a NULL driver to clear any existing Device Setup Class.
            using var deviceInfoSet = PInvoke.SetupDiCreateDeviceInfoList((Guid?)null, default);
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
            PInvoke.DiInstallDevice(default, deviceInfoSet, deviceInfoData, null, PInvoke.DIIDFLAG_INSTALLNULLDRIVER, &tmpReboot).ThrowOnError(nameof(PInvoke.DIIDFLAG_INSTALLNULLDRIVER));
            if (tmpReboot)
            {
                reboot = true;
            }
        }

        // For some devices (Google Pixel) it takes a while before the driver can be set again.
        // 200 ms seems to work, so delay for 500 ms for good measure...
        Thread.Sleep(TimeSpan.FromMilliseconds(500));

        unsafe
        {
            // Now we can update the driver.
            using var deviceInfoSet = PInvoke.SetupDiCreateDeviceInfoList((Guid?)null, default);
            if (deviceInfoSet.IsInvalid)
            {
                throw new Win32Exception(nameof(PInvoke.SetupDiCreateDeviceInfoList));
            }
            var deviceInfoData = new SP_DEVINFO_DATA()
            {
                cbSize = (uint)Marshal.SizeOf<SP_DEVINFO_DATA>(),
            };
            PInvoke.SetupDiOpenDeviceInfo(deviceInfoSet, originalInstanceId, default, 0, &deviceInfoData).ThrowOnError(nameof(PInvoke.SetupDiOpenDeviceInfo));
            var deviceInstallParams = new SP_DEVINSTALL_PARAMS_W()
            {
                cbSize = (uint)Marshal.SizeOf<SP_DEVINSTALL_PARAMS_W>(),
                Flags = PInvoke.DI_ENUMSINGLEINF,
                FlagsEx = PInvoke.DI_FLAGSEX_ALLOWEXCLUDEDDRVS,
                DriverPath = @$"{RegistryUtils.InstallationFolder ?? throw new UnexpectedResultException("not installed")}\Drivers\VBoxUSB\VBoxUSB.inf",
            };
            PInvoke.SetupDiSetDeviceInstallParams(deviceInfoSet, deviceInfoData, deviceInstallParams).ThrowOnError(nameof(PInvoke.SetupDiSetDeviceInstallParams));
            PInvoke.SetupDiBuildDriverInfoList(deviceInfoSet, &deviceInfoData, SETUP_DI_BUILD_DRIVER_DRIVER_TYPE.SPDIT_CLASSDRIVER).ThrowOnError(nameof(PInvoke.SetupDiBuildDriverInfoList));
            var driverInfoData = new SP_DRVINFO_DATA_V2_W()
            {
                cbSize = (uint)Marshal.SizeOf<SP_DRVINFO_DATA_V2_W>(),
            };
            PInvoke.SetupDiEnumDriverInfo(deviceInfoSet, deviceInfoData, (uint)SETUP_DI_BUILD_DRIVER_DRIVER_TYPE.SPDIT_CLASSDRIVER, 0, ref driverInfoData).ThrowOnError(nameof(PInvoke.SetupDiEnumDriverInfo));
            BOOL tmpReboot;
            // NOTE: Workaround for https://github.com/microsoft/win32metadata/issues/903
            PInvoke.DiInstallDevice(default, (HDEVINFO)deviceInfoSet.DangerousGetHandle(), &deviceInfoData, (SP_DRVINFO_DATA_V2_A*)&driverInfoData, 0, &tmpReboot).ThrowOnError(nameof(PInvoke.DiInstallDevice));
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
        unsafe
        {
            // First, we must set a NULL driver, just in case no default driver exists.
            using var deviceInfoSet = PInvoke.SetupDiCreateDeviceInfoList((Guid?)null, default);
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
            PInvoke.DiInstallDevice(default, deviceInfoSet, deviceInfoData, null, PInvoke.DIIDFLAG_INSTALLNULLDRIVER, &tmpReboot).ThrowOnError(nameof(PInvoke.DIIDFLAG_INSTALLNULLDRIVER));
            if (tmpReboot)
            {
                reboot = true;
            }
        }

        // For some devices (Google Pixel) it takes a while before the driver can be set again.
        // 200 ms seems to work, so delay for 500 ms for good measure...
        Thread.Sleep(TimeSpan.FromMilliseconds(500));

        unsafe
        {
            // Now we let Windows install the default PnP driver.
            // We don't fail if no such driver can be found.
            using var deviceInfoSet = PInvoke.SetupDiCreateDeviceInfoList((Guid?)null, default);
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
            catch (Win32Exception ex) when ((uint)ex.NativeErrorCode == Interop.WinSDK.ERROR_NO_DRIVER_SELECTED)
            {
                // Not really an error; this just means Windows does not have a default PnP driver for it.
                // The device will be listed under "Other devices" with a question mark.
            }
        }
        return reboot;
    }
}
