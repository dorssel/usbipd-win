// SPDX-FileCopyrightText: 2021 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-2.0-only

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

using static UsbIpServer.Interop.VBoxUsb;
using static UsbIpServer.Tools;

namespace UsbIpServer
{
    static class VBoxUsb
    {
        static async Task<(ConfigurationManager.VBoxDevice, DeviceFile)> ClaimDeviceOnce(BusId busId)
        {
            var vboxDevice = ConfigurationManager.GetVBoxDevice(busId);
            var dev = new DeviceFile(vboxDevice.InterfacePath);
            try
            {
                {
                    var output = new byte[Marshal.SizeOf<UsbSupVersion>()];
                    await dev.IoControlAsync(SUPUSB_IOCTL.GET_VERSION, null, output);
                    BytesToStruct(output, out UsbSupVersion version);
                    if ((version.major != USBDRV_MAJOR_VERSION) || (version.minor < USBDRV_MINOR_VERSION))
                    {
                        throw new NotSupportedException($"device version not supported: {version.major}.{version.minor}, expected {USBDRV_MAJOR_VERSION}.{USBDRV_MINOR_VERSION}");
                    }
                }
                {
                    var claimDev = new UsbSupClaimDev();
                    var output = new byte[Marshal.SizeOf<UsbSupClaimDev>()];
                    await dev.IoControlAsync(SUPUSB_IOCTL.USB_CLAIM_DEVICE, StructToBytes(claimDev), output);
                    BytesToStruct(output, out claimDev);
                    if (!claimDev.fClaimed)
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
                    ConfigurationManager.SetDeviceFriendlyName(vboxDevice.DeviceNode);
                }
                catch (Win32Exception) { }

                var result = dev;
                dev = null!;
                return (vboxDevice, result);
            }
            finally
            {
                dev?.Dispose();
            }
            throw new FileNotFoundException();
        }

        public static async Task<(ConfigurationManager.VBoxDevice, DeviceFile)> ClaimDevice(BusId busId)
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
                    if (sw.Elapsed > TimeSpan.FromSeconds(5))
                    {
                        throw;
                    }
                    await Task.Delay(100);
                }
            }
        }
    }
}
