// SPDX-FileCopyrightText: 2025 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Usbipd.Automation;
using Windows.Win32;
using Windows.Win32.Devices.DeviceAndDriverInstallation;
using Windows.Win32.Foundation;

namespace Usbipd;


sealed class DriverDetails
{
    static public readonly DriverDetails Instance = new();

    public string DriverPath { get; }
    public VidPid VidPid { get; }
    public Guid ClassGuid { get; }
    public Version Version { get; }

    DriverDetails()
    {
        DriverPath = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath)!, "Drivers", "VBoxUSB.inf");

        using var deviceInfoSet = PInvoke.SetupDiCreateDeviceInfoList(null, default);
        if (deviceInfoSet.IsInvalid)
        {
            Tools.ThrowWin32Error(nameof(PInvoke.SetupDiCreateDeviceInfoList));
        }
        var deviceInstallParams = new SP_DEVINSTALL_PARAMS_W()
        {
            cbSize = (uint)Unsafe.SizeOf<SP_DEVINSTALL_PARAMS_W>(),
            Flags = SETUP_DI_DEVICE_INSTALL_FLAGS.DI_ENUMSINGLEINF,
            FlagsEx = SETUP_DI_DEVICE_INSTALL_FLAGS_EX.DI_FLAGSEX_ALLOWEXCLUDEDDRVS,
            DriverPath = DriverPath,
        };
        PInvoke.SetupDiSetDeviceInstallParams(deviceInfoSet, null, deviceInstallParams)
            .ThrowOnWin32Error(nameof(PInvoke.SetupDiSetDeviceInstallParams));
        PInvoke.SetupDiBuildDriverInfoList(deviceInfoSet, SETUP_DI_DRIVER_TYPE.SPDIT_CLASSDRIVER)
            .ThrowOnWin32Error(nameof(PInvoke.SetupDiBuildDriverInfoList));
        var driverInfoData = new SP_DRVINFO_DATA_V2_W()
        {
            cbSize = (uint)Unsafe.SizeOf<SP_DRVINFO_DATA_V2_W>(),
        };
        PInvoke.SetupDiEnumDriverInfo(deviceInfoSet, null, SETUP_DI_DRIVER_TYPE.SPDIT_CLASSDRIVER, 0, ref driverInfoData)
            .ThrowOnWin32Error(nameof(PInvoke.SetupDiEnumDriverInfo));

        Version = new Version(
            (int)((driverInfoData.DriverVersion >> 48) & 0xffff),
            (int)((driverInfoData.DriverVersion >> 32) & 0xffff),
            (int)((driverInfoData.DriverVersion >> 16) & 0xffff),
            (int)(driverInfoData.DriverVersion & 0xffff));

        {
            if (!PInvoke.SetupDiGetDriverInfoDetail(deviceInfoSet, null, driverInfoData, null, out var requiredSize)
                && ((WIN32_ERROR)Marshal.GetLastPInvokeError() != WIN32_ERROR.ERROR_INSUFFICIENT_BUFFER))
            {
                Tools.ThrowWin32Error(nameof(PInvoke.SetupDiGetDriverInfoDetail));
            }
            if (requiredSize < Unsafe.SizeOf<SP_DRVINFO_DETAIL_DATA_W>())
            {
                // This can happen for drivers that do not have any ID at all (neither hardware ID nor compatible ID).
                // For the AsRef below we need at least the base size.
                // NOTE: Of course, this should never be the case for our driver. This should be unreachable code.
                requiredSize = (uint)Unsafe.SizeOf<SP_DRVINFO_DETAIL_DATA_W>();
            }
            var buffer = new byte[requiredSize];
            ref var details = ref MemoryMarshal.AsRef<SP_DRVINFO_DETAIL_DATA_W>(buffer);
            details.cbSize = (uint)Unsafe.SizeOf<SP_DRVINFO_DETAIL_DATA_W>();
            PInvoke.SetupDiGetDriverInfoDetail(deviceInfoSet, null, driverInfoData, buffer)
                .ThrowOnWin32Error(nameof(PInvoke.SetupDiGetDriverInfoDetail));
            var hardwareId = (details.CompatIDsOffset > 0) ?
                new string(details.HardwareID.AsSpan(checked((int)(details.CompatIDsOffset - 1)))) : string.Empty;
            if (!VidPid.TryParseId(hardwareId, out var vidPid))
            {
                throw new FormatException("Invalid HardwareID format.");
            }
            VidPid = vidPid;
        }

        {
            if (!PInvoke.SetupDiGetINFClass(DriverPath, out var classGuid, default, out var requiredSize)
                && ((WIN32_ERROR)Marshal.GetLastPInvokeError() != WIN32_ERROR.ERROR_INSUFFICIENT_BUFFER))
            {
                Tools.ThrowWin32Error(nameof(PInvoke.SetupDiGetINFClass));
            }
            var buffer = new char[requiredSize];
            PInvoke.SetupDiGetINFClass(DriverPath, out classGuid, buffer)
                .ThrowOnWin32Error(nameof(PInvoke.SetupDiGetINFClass));
            ClassGuid = classGuid;
        }
    }
}
