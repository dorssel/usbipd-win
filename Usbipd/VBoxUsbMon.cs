// SPDX-FileCopyrightText: 2020 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

using System.ComponentModel;
using System.Runtime.InteropServices;
using System.ServiceProcess;

using static Usbipd.Interop.VBoxUsbMon;
using static Usbipd.Tools;

namespace Usbipd;

sealed partial class VBoxUsbMon : IDisposable
{
    readonly DeviceFile UsbMonitor = new(USBMON_DEVICE_NAME);

    public static UsbSupVersion? GetRunningVersion()
    {
        try
        {
            using var mon = new VBoxUsbMon();
            return mon.GetVersion().Result;
        }
        catch (Win32Exception)
        {
            return null;
        }
    }

    public static bool IsServiceInstalled()
    {
        return ServiceController.GetDevices().Any(sc => sc.ServiceName.Equals(ServiceName, StringComparison.InvariantCultureIgnoreCase));
    }

    public static bool IsVersionSupported(UsbSupVersion version)
    {
        return (version.major == USBMON_MAJOR_VERSION) && (version.minor >= USBMON_MINOR_VERSION);
    }

    public async Task<UsbSupVersion> GetVersion()
    {
        var output = new byte[Marshal.SizeOf<UsbSupVersion>()];
        _ = await UsbMonitor.IoControlAsync(SUPUSBFLT_IOCTL.GET_VERSION, null, output);
        BytesToStruct(output, out UsbSupVersion version);
        return version;
    }

    public async Task<ulong> AddFilter(ExportedDevice device)
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
        _ = await UsbMonitor.IoControlAsync(SUPUSBFLT_IOCTL.ADD_FILTER, StructToBytes(filter), output);
        var fltAddOut = BytesToStruct<UsbSupFltAddOut>(output);
        return fltAddOut.rc == 0 ? fltAddOut.uId
            : throw new UnexpectedResultException($"SUPUSBFLT_IOCTL_ADD_FILTER failed with returnCode {fltAddOut.rc}");
    }

    public async Task RemoveFilter(ulong filterId)
    {
        var output = new byte[sizeof(int)];
        _ = await UsbMonitor.IoControlAsync(SUPUSBFLT_IOCTL.REMOVE_FILTER, BitConverter.GetBytes(filterId), output);
        var rc = BitConverter.ToInt32(output);
        if (rc != 0 /* VINF_SUCCESS */)
        {
            throw new UnexpectedResultException($"SUPUSBFLT_IOCTL_REMOVE_FILTER failed with returnCode {rc}");
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
