// SPDX-FileCopyrightText: 2025 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

using System.ComponentModel;
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

        unsafe // DevSkim: ignore DS172412
        {
            using var deviceInfoSet = PInvoke.SetupDiCreateDeviceInfoList(null, default);
            if (deviceInfoSet.IsInvalid)
            {
                throw new Win32Exception();
            }
            var deviceInstallParams = new SP_DEVINSTALL_PARAMS_W()
            {
                cbSize = (uint)Marshal.SizeOf<SP_DEVINSTALL_PARAMS_W>(),
                Flags = SETUP_DI_DEVICE_INSTALL_FLAGS.DI_ENUMSINGLEINF,
                FlagsEx = SETUP_DI_DEVICE_INSTALL_FLAGS_EX.DI_FLAGSEX_ALLOWEXCLUDEDDRVS,
                DriverPath = DriverPath,
            };
            PInvoke.SetupDiSetDeviceInstallParams(deviceInfoSet, null, deviceInstallParams)
                .ThrowOnError(nameof(PInvoke.SetupDiSetDeviceInstallParams));
            PInvoke.SetupDiBuildDriverInfoList(deviceInfoSet, null, SETUP_DI_DRIVER_TYPE.SPDIT_CLASSDRIVER)
                .ThrowOnError(nameof(PInvoke.SetupDiBuildDriverInfoList));
            var driverInfoData = new SP_DRVINFO_DATA_V2_W()
            {
                cbSize = (uint)Marshal.SizeOf<SP_DRVINFO_DATA_V2_W>(),
            };
            PInvoke.SetupDiEnumDriverInfo(deviceInfoSet, null, SETUP_DI_DRIVER_TYPE.SPDIT_CLASSDRIVER, 0, ref driverInfoData)
                .ThrowOnError(nameof(PInvoke.SetupDiEnumDriverInfo));

            Version = new Version(
                (int)((driverInfoData.DriverVersion >> 48) & 0xffff),
                (int)((driverInfoData.DriverVersion >> 32) & 0xffff),
                (int)((driverInfoData.DriverVersion >> 16) & 0xffff),
                (int)(driverInfoData.DriverVersion & 0xffff));

            {
                var requiredSize = 0u;
                if (!PInvoke.SetupDiGetDriverInfoDetail(deviceInfoSet, null, driverInfoData, null, 0, &requiredSize))
                {
                    if ((WIN32_ERROR)Marshal.GetLastPInvokeError() != WIN32_ERROR.ERROR_INSUFFICIENT_BUFFER)
                    {
                        throw new Win32Exception();
                    }
                }
                var buffer = new byte[requiredSize];
                fixed (byte* bufferPointer = buffer)
                {
                    var details = (SP_DRVINFO_DETAIL_DATA_W*)bufferPointer;
                    details->cbSize = (uint)Marshal.SizeOf<SP_DRVINFO_DETAIL_DATA_W>();
                    PInvoke.SetupDiGetDriverInfoDetail(deviceInfoSet, null, driverInfoData, details, requiredSize, null)
                        .ThrowOnError(nameof(PInvoke.SetupDiGetDriverInfoDetail));
                    var hardwareId = new string((char*)&details->HardwareID);
                    if (!VidPid.TryParseId(hardwareId, out var vidPid))
                    {
                        throw new FormatException("Invalid HardwareID format.");
                    }
                    VidPid = vidPid;
                }
            }

            {
                var requiredSize = 0u;
                if (!PInvoke.SetupDiGetINFClass(DriverPath, out var classGuid, default, &requiredSize))
                {
                    if ((WIN32_ERROR)Marshal.GetLastPInvokeError() != WIN32_ERROR.ERROR_INSUFFICIENT_BUFFER)
                    {
                        throw new Win32Exception();
                    }
                }
                var buffer = new char[requiredSize];
                PInvoke.SetupDiGetINFClass(DriverPath, out classGuid, buffer, null)
                    .ThrowOnError(nameof(PInvoke.SetupDiGetINFClass));
                ClassGuid = classGuid;
            }
        }
    }
}
