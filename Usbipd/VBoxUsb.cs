// SPDX-FileCopyrightText: 2021 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Usbipd.Automation;
using static Usbipd.Interop.VBoxUsb;

namespace Usbipd;

static class VBoxUsb
{
    static async Task<(WindowsDevice vboxDevice, DeviceFile deviceInterfaceFile)> ClaimDeviceOnce(BusId busId)
    {
        var device = WindowsDevice.GetAll(GUID_CLASS_VBOXUSB).FirstOrDefault(di => di.BusId == busId) ?? throw new FileNotFoundException();
        var file = device.OpenVBoxInterface();
        try
        {
            {
                var output = new byte[Unsafe.SizeOf<UsbSupVersion>()];
                _ = await file.IoControlAsync(SUPUSB_IOCTL.GET_VERSION, null, output);
                ref readonly var version = ref MemoryMarshal.AsRef<UsbSupVersion>(output);
                if ((version.major != USBDRV_MAJOR_VERSION) || (version.minor < USBDRV_MINOR_VERSION))
                {
                    throw new NotSupportedException(
                        $"device version not supported: {version.major}.{version.minor}, expected {USBDRV_MAJOR_VERSION}.{USBDRV_MINOR_VERSION}");
                }
            }
            {
                var output = new byte[Unsafe.SizeOf<UsbSupClaimDev>()];
                // NOTE: input is not actually used by the driver, but it needs to be present and have the same length as the output.
                _ = await file.IoControlAsync(SUPUSB_IOCTL.USB_CLAIM_DEVICE, output, output);
                ref readonly var claimDev = ref MemoryMarshal.AsRef<UsbSupClaimDev>(output);
                if (!claimDev.Claimed)
                {
                    throw new ProtocolViolationException("could not claim");
                }
            }

            try
            {
                // We act as a "class installer" for USBIP devices. Override the FriendlyName so
                // Windows device manager shows a nice descriptive name instead of the confusing
                // "VBoxUSB".

                // Best effort, not really a problem if this fails.
                _ = device.SetFriendlyName();
            }
            catch (Win32Exception) { }

            var result = file;
            file = null!;
            return (device, result);
        }
        finally
        {
            file?.Dispose();
        }
        throw new FileNotFoundException();
    }

    public static async Task<(WindowsDevice vboxDevice, DeviceFile deviceInterfaceFile)> ClaimDevice(BusId busId)
    {
        var sw = new Stopwatch();
        sw.Start();
        while (true)
        {
            try
            {
                return await ClaimDeviceOnce(busId);
            }
            catch (FileNotFoundException)
            {
                if (sw.Elapsed > TimeSpan.FromSeconds(10))
                {
                    throw;
                }
                await Task.Delay(100);
            }
        }
    }
}
