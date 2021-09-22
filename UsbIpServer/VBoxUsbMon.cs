// SPDX-FileCopyrightText: 2020 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-2.0-only

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.Win32;

using static UsbIpServer.Interop.VBoxUsb;
using static UsbIpServer.Tools;

namespace UsbIpServer
{
    sealed class VBoxUsbMon : IDisposable
    {
        readonly DeviceFile UsbMonitor = new(USBMON_DEVICE_NAME);

        public async Task CheckVersion()
        {
            var output = new byte[Marshal.SizeOf<UsbSupVersion>()];
            await UsbMonitor.IoControlAsync(SUPUSBFLT_IOCTL.GET_VERSION, null, output);
            BytesToStruct(output, out UsbSupVersion version);
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
            filter.SetMatch(UsbFilterIdx.PORT, UsbFilterMatch.NUM_EXACT, device.BusId.Port);

            var output = new byte[Marshal.SizeOf<UsbSupFltAddOut>()];
            await UsbMonitor.IoControlAsync(SUPUSBFLT_IOCTL.ADD_FILTER, StructToBytes(filter), output);
            var fltAddOut = BytesToStruct<UsbSupFltAddOut>(output);
            if (fltAddOut.rc != 0 /* VINF_SUCCESS */)
            {
                throw new UnexpectedResultException($"SUPUSBFLT_IOCTL_ADD_FILTER failed with returnCode {fltAddOut.rc}");
            }
        }

        public async Task RunFilters()
        {
            await UsbMonitor.IoControlAsync(SUPUSBFLT_IOCTL.RUN_FILTERS, null, null);
        }

        async Task<DeviceFile> ClaimDeviceOnce(ExportedDevice device)
        {
            using var deviceInfoSet = SetupDiGetClassDevs(GUID_CLASS_VBOXUSB, null, default, Constants.DIGCF_DEVICEINTERFACE | Constants.DIGCF_PRESENT);
            foreach (var (infoData, interfaceData) in EnumDeviceInterfaces(deviceInfoSet, GUID_CLASS_VBOXUSB))
            {
                GetBusId(deviceInfoSet, infoData, out var busId);
                if (busId != device.BusId)
                {
                    continue;
                }

                var path = GetDeviceInterfaceDetail(deviceInfoSet, interfaceData);

                var dev = new DeviceFile(path);
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
                        await dev.IoControlAsync(SUPUSB_IOCTL.IS_OPERATIONAL, null, null);
                    }
                    IntPtr hdev;
                    {
                        var getDev = new UsbSupGetDev();
                        var output = new byte[Marshal.SizeOf<UsbSupGetDev>()];
                        await dev.IoControlAsync(SUPUSB_IOCTL.GET_DEVICE, StructToBytes(getDev), output);
                        BytesToStruct(output, out getDev);
                        hdev = getDev.hDevice;
                    }
                    {
                        var getDev = new UsbSupGetDev()
                        {
                            hDevice = hdev,
                        };
                        var output = new byte[Marshal.SizeOf<UsbSupGetDevMon>()];
                        await UsbMonitor.IoControlAsync(SUPUSBFLT_IOCTL.GET_DEVICE, StructToBytes(getDev), output);
                        var getDevMon = BytesToStruct<UsbSupGetDevMon>(output);
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
                    {
                        var getDev = new UsbSupGetDev()
                        {
                            hDevice = hdev,
                        };
                        var output = new byte[Marshal.SizeOf<UsbSupGetDevMon>()];
                        await UsbMonitor.IoControlAsync(SUPUSBFLT_IOCTL.GET_DEVICE, StructToBytes(getDev), output);
                        var getDevMon = BytesToStruct<UsbSupGetDevMon>(output);
                    }

                    try
                    {
                        // We act as a "class installer" for USBIP devices. Override the FriendlyName so
                        // Windows device manager shows a nice descriptive name instead of the confusing
                        // "VBoxUSB".

                        // Best effort, not really a problem if this fails.
                        SetDevicePropertyString(deviceInfoSet, in infoData, in Constants.DEVPKEY_Device_FriendlyName, $"USBIP Shared Device {device.BusId}");
                    }
                    catch (Win32Exception) { }

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

        bool IsDisposed;
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
