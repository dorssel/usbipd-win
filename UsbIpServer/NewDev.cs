// SPDX-FileCopyrightText: 2021 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-2.0-only

using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using Windows.Win32.Devices.DeviceAndDriverInstallation;
using Windows.Win32.Foundation;

namespace UsbIpServer
{
    static class NewDev
    {
        static void ThrowOnError(this BOOL success, string message)
        {
            if (!success)
            {
                throw new Win32Exception(message);
            }
        }

        sealed class SafeDeviceInfoSet : SafeHandleZeroOrMinusOneIsInvalid
        {
            public unsafe SafeDeviceInfoSet(void* handle)
                : base(true)
            {
                SetHandle((IntPtr)handle);
            }

            public static unsafe implicit operator void*(SafeDeviceInfoSet deviceInfoSet) =>
                deviceInfoSet.IsClosed || deviceInfoSet.IsInvalid ? PInvoke.INVALID_HANDLE_VALUE.Value.ToPointer() : deviceInfoSet.handle.ToPointer();

            protected override bool ReleaseHandle()
            {
                unsafe
                {
                    return PInvoke.SetupDiDestroyDeviceInfoList(handle.ToPointer());
                }
            }
        }

        public static bool ForceVBoxDriver(string originalInstanceId)
        {
            BOOL reboot = false;
            unsafe
            {
                // First, we must set a NULL driver to clear any existing Device Setup Class.
                using var deviceInfoSet = new SafeDeviceInfoSet(PInvoke.SetupDiCreateDeviceInfoList((Guid*)null, default));
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
                PInvoke.DiInstallDevice(default, deviceInfoSet, &deviceInfoData, null, PInvoke.DIIDFLAG_INSTALLNULLDRIVER, &tmpReboot).ThrowOnError(nameof(PInvoke.DIIDFLAG_INSTALLNULLDRIVER));
                if (tmpReboot)
                {
                    reboot = true;
                }
            }
            unsafe
            {
                // Now we can update the driver.
                using var deviceInfoSet = new SafeDeviceInfoSet(PInvoke.SetupDiCreateDeviceInfoList((Guid*)null, default));
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
                    DriverPath = @"C:\Program Files\usbipd-win\Drivers\VBoxUSB\VBoxUSB.inf",
                };
                PInvoke.SetupDiSetDeviceInstallParams(deviceInfoSet, deviceInfoData, deviceInstallParams).ThrowOnError(nameof(PInvoke.SetupDiSetDeviceInstallParams));
                PInvoke.SetupDiBuildDriverInfoList(deviceInfoSet, &deviceInfoData, SETUP_DI_BUILD_DRIVER_DRIVER_TYPE.SPDIT_CLASSDRIVER).ThrowOnError(nameof(PInvoke.SetupDiBuildDriverInfoList));
                var driverInfoData = new SP_DRVINFO_DATA_V2_W()
                {
                    cbSize = (uint)Marshal.SizeOf<SP_DRVINFO_DATA_V2_W>(),
                };
                PInvoke.SetupDiEnumDriverInfo(deviceInfoSet, deviceInfoData, (uint)SETUP_DI_BUILD_DRIVER_DRIVER_TYPE.SPDIT_CLASSDRIVER, 0, ref driverInfoData).ThrowOnError(nameof(PInvoke.SetupDiEnumDriverInfo));
                BOOL tmpReboot;
                PInvoke.DiInstallDevice(default, deviceInfoSet, &deviceInfoData, (SP_DRVINFO_DATA_V2_A*)&driverInfoData, 0, &tmpReboot).ThrowOnError(nameof(PInvoke.DiInstallDevice));
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
                using var deviceInfoSet = new SafeDeviceInfoSet(PInvoke.SetupDiCreateDeviceInfoList((Guid*)null, default));
                if (deviceInfoSet.IsInvalid)
                {
                    throw new Win32Exception(nameof(PInvoke.SetupDiCreateDeviceInfoList));
                }
                var deviceInfoData = new SP_DEVINFO_DATA()
                {
                    cbSize = (uint)Marshal.SizeOf<SP_DEVINFO_DATA>(),
                };
                PInvoke.SetupDiOpenDeviceInfo(deviceInfoSet, originalInstanceId, default, 0, &deviceInfoData).ThrowOnError(nameof(PInvoke.SetupDiOpenDeviceInfo));
                PInvoke.DiInstallDevice(default, deviceInfoSet, &deviceInfoData, null, 0, &reboot).ThrowOnError(nameof(PInvoke.DiInstallDevice));
            }
            return reboot;
        }
    }
}
