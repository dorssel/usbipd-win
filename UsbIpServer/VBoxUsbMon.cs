/*
    usbipd-win
    Copyright (C) 2020  Frans van Dorsselaer

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program.  If not, see <https://www.gnu.org/licenses/>.
*/

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using UsbIpServer.Interop;
using static UsbIpServer.Interop.VBoxUsb;
using static UsbIpServer.Interop.WinSDK;
using static UsbIpServer.Tools;

namespace UsbIpServer
{
    sealed class VBoxUsbMon : IDisposable
    {
        readonly DeviceFile UsbMonitor = new DeviceFile(USBMON_DEVICE_NAME);

        public async Task CheckVersion()
        {
            var output = new byte[Marshal.SizeOf<UsbSupVersion>()];
            await UsbMonitor.IoControlAsync(VBoxUsb.IoControl.SUPUSBFLT_IOCTL_GET_VERSION, null, output);
            BytesToStruct(output, 0, out UsbSupVersion version);
            if ((version.major != USBMON_MAJOR_VERSION) || (version.minor < USBMON_MINOR_VERSION))
            {
                throw new NotSupportedException($"version not supported: {version.major}.{version.minor}, expected {USBMON_MAJOR_VERSION}.{USBMON_MINOR_VERSION}");
            }
        }

        public async Task AddFilter(ExportedDevice device)
        {
            var filter = UsbFilter.Create(UsbFilterType.CAPTURE);
            filter.SetMatch(UsbFilterIdx.VENDOR_ID, UsbFilterMatch.NUM_EXACT, device.VendorId);
            filter.SetMatch(UsbFilterIdx.PRODUCT_ID, UsbFilterMatch.NUM_EXACT, device.ProductId);
            filter.SetMatch(UsbFilterIdx.DEVICE_REV, UsbFilterMatch.NUM_EXACT, device.BcdDevice);
            filter.SetMatch(UsbFilterIdx.DEVICE_CLASS, UsbFilterMatch.NUM_EXACT, device.DeviceClass);
            filter.SetMatch(UsbFilterIdx.DEVICE_SUB_CLASS, UsbFilterMatch.NUM_EXACT, device.DeviceSubClass);
            filter.SetMatch(UsbFilterIdx.DEVICE_PROTOCOL, UsbFilterMatch.NUM_EXACT, device.DeviceProtocol);
            filter.SetMatch(UsbFilterIdx.PORT, UsbFilterMatch.NUM_EXACT, (ushort)device.DevNum);

            var output = new byte[Marshal.SizeOf<UsbSupFltAddOut>()];
            await UsbMonitor.IoControlAsync(VBoxUsb.IoControl.SUPUSBFLT_IOCTL_ADD_FILTER, StructToBytes(filter), output);
            var fltAddOut = BytesToStruct<UsbSupFltAddOut>(output, 0);
            if (fltAddOut.rc != 0 /* VINF_SUCCESS */)
            {
                throw new SystemException($"SUPUSBFLT_IOCTL_ADD_FILTER failed with returnCode {fltAddOut.rc}");
            }
        }

        public async Task RunFilters()
        {
            await UsbMonitor.IoControlAsync(VBoxUsb.IoControl.SUPUSBFLT_IOCTL_RUN_FILTERS, null, null);
        }

        async Task<DeviceFile> ClaimDeviceOnce(ExportedDevice device)
        {
            using var deviceInfoSet = NativeMethods.SetupDiGetClassDevs(GUID_CLASS_VBOXUSB, null, IntPtr.Zero, DiGetClassFlags.DIGCF_DEVICEINTERFACE | DiGetClassFlags.DIGCF_PRESENT);
            if (deviceInfoSet.IsInvalid)
            {
                throw new Win32Exception("SetupDiGetClassDevs");
            }
            foreach (var (infoData, interfaceData) in EnumDeviceInterfaces(deviceInfoSet, GUID_CLASS_VBOXUSB))
            {
                GetBusId(deviceInfoSet, infoData, out var hubNum, out var connectionIndex);
                if ((hubNum != device.BusNum) || (connectionIndex != device.DevNum))
                {
                    continue;
                }

                var path = GetDeviceInterfaceDetail(deviceInfoSet, interfaceData);

                var dev = new DeviceFile(path);
                try
                {
                    {
                        var output = new byte[Marshal.SizeOf<UsbSupVersion>()];
                        await dev.IoControlAsync(VBoxUsb.IoControl.SUPUSB_IOCTL_GET_VERSION, null, output);
                        BytesToStruct(output, 0, out UsbSupVersion version);
                        if ((version.major != USBDRV_MAJOR_VERSION) || (version.minor < USBDRV_MINOR_VERSION))
                        {
                            throw new NotSupportedException($"device version not supported: {version.major}.{version.minor}, expected {USBDRV_MAJOR_VERSION}.{USBDRV_MINOR_VERSION}");
                        }
                    }
                    {
                        await dev.IoControlAsync(VBoxUsb.IoControl.SUPUSB_IOCTL_IS_OPERATIONAL, null, null);
                    }
                    IntPtr hdev;
                    {
                        var getDev = new UsbSupGetDev();
                        var output = new byte[Marshal.SizeOf<UsbSupGetDev>()];
                        await dev.IoControlAsync(VBoxUsb.IoControl.SUPUSB_IOCTL_GET_DEVICE, StructToBytes(getDev), output);
                        BytesToStruct(output, 0, out getDev);
                        hdev = getDev.hDevice;
                    }
                    {
                        var getDev = new UsbSupGetDev()
                        {
                            hDevice = hdev,
                        };
                        var output = new byte[Marshal.SizeOf<UsbSupGetDevMon>()];
                        await UsbMonitor.IoControlAsync(VBoxUsb.IoControl.SUPUSBFLT_IOCTL_GET_DEVICE, StructToBytes(getDev), output);
                        var getDevMon = BytesToStruct<UsbSupGetDevMon>(output, 0);
                    }
                    {
                        var claimDev = new UsbSupClaimDev();
                        var output = new byte[Marshal.SizeOf<UsbSupClaimDev>()];
                        await dev.IoControlAsync(VBoxUsb.IoControl.SUPUSB_IOCTL_USB_CLAIM_DEVICE, StructToBytes(claimDev), output);
                        BytesToStruct(output, 0, out claimDev);
                        if (!claimDev.fClaimed)
                        {
                            throw new ProtocolViolationException("could not claim");
                        }
                    }
                    {
                        var getDev = new UsbSupGetDev()
                        {
                            hDevice = hdev,
                        };
                        var output = new byte[Marshal.SizeOf<UsbSupGetDevMon>()];
                        await UsbMonitor.IoControlAsync(VBoxUsb.IoControl.SUPUSBFLT_IOCTL_GET_DEVICE, StructToBytes(getDev), output);
                        var getDevMon = BytesToStruct<UsbSupGetDevMon>(output, 0);
                    }
                    var result = dev;
                    dev = null!;
                    return result;
                }
                finally
                {
                    dev?.Dispose();
                }
            }
            throw new FileNotFoundException();
        }

        public async Task<DeviceFile> ClaimDevice(ExportedDevice device)
        {
            var sw = new Stopwatch();
            sw.Start();
            while (true)
            {
                try
                {
                    return await ClaimDeviceOnce(device);
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

        private bool IsDisposed;
        public void Dispose()
        {
            if (!IsDisposed)
            {
                UsbMonitor.Dispose();
                IsDisposed = true;
            }
        }
    }
}
