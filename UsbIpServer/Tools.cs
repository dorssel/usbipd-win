// SPDX-FileCopyrightText: 2020 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-2.0-only

using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using UsbIpServer.Interop;
using Windows.Win32.Devices.Usb;

namespace UsbIpServer
{
    static class Tools
    {
        public static async Task ReadExactlyAsync(this Stream stream, Memory<byte> buf, CancellationToken cancellationToken)
        {
            var remain = buf.Length;
            while (remain > 0)
            {
                var readCount = await stream.ReadAsync(buf[^remain..], cancellationToken);
                if (readCount == 0)
                {
                    throw new EndOfStreamException();
                }
                remain -= readCount;
            }
        }

        public static void StructToBytes<T>(in T s, Span<byte> bytes) where T : struct
        {
            var required = Marshal.SizeOf<T>();
            if (bytes.Length < required)
            {
                throw new ArgumentException($"buffer too small for structure: {bytes.Length} < {required}", nameof(bytes));
            }
            unsafe
            {
                fixed (byte* dst = bytes)
                {
                    Marshal.StructureToPtr(s, (IntPtr)dst, false);
                }
            }
        }

        public static byte[] StructToBytes<T>(in T s) where T : struct
        {
            var buf = new byte[Marshal.SizeOf<T>()];
            StructToBytes(s, buf);
            return buf;
        }

        public static void BytesToStruct<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.NonPublicConstructors)] T>(ReadOnlySpan<byte> bytes, out T s) where T : struct
        {
            var required = Marshal.SizeOf<T>();
            if (bytes.Length < required)
            {
                throw new ArgumentException($"buffer too small for structure: {bytes.Length} < {required}", nameof(bytes));
            }
            unsafe
            {
                fixed (byte* src = bytes)
                {
                    s = Marshal.PtrToStructure<T>((IntPtr)src);
                }
            }
        }

        public static T BytesToStruct<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.NonPublicConstructors)] T>(ReadOnlySpan<byte> bytes) where T : struct
        {
            BytesToStruct(bytes, out T result);
            return result;
        }

        public static Linux.UsbDeviceSpeed MapWindowsSpeedToLinuxSpeed(USB_DEVICE_SPEED w)
        {
            // Windows and Linux each use a *different* enum for this
            return w switch
            {
                USB_DEVICE_SPEED.UsbLowSpeed => Linux.UsbDeviceSpeed.USB_SPEED_LOW,
                USB_DEVICE_SPEED.UsbFullSpeed => Linux.UsbDeviceSpeed.USB_SPEED_FULL,
                USB_DEVICE_SPEED.UsbHighSpeed => Linux.UsbDeviceSpeed.USB_SPEED_HIGH,
                USB_DEVICE_SPEED.UsbSuperSpeed => Linux.UsbDeviceSpeed.USB_SPEED_SUPER,
                _ => Linux.UsbDeviceSpeed.USB_SPEED_UNKNOWN,
            };
        }

        /// <summary>
        /// See <see href="https://www.kernel.org/doc/html/latest/driver-api/usb/error-codes.html"/>.
        /// </summary>
        public static Linux.Errno ConvertError(VBoxUsb.UsbSupError usbSupError)
        {
            return usbSupError switch
            {
                VBoxUsb.UsbSupError.USBSUP_XFER_OK => Linux.Errno.SUCCESS,
                VBoxUsb.UsbSupError.USBSUP_XFER_STALL => Linux.Errno.EPIPE,
                VBoxUsb.UsbSupError.USBSUP_XFER_DNR => Linux.Errno.ETIME,
                VBoxUsb.UsbSupError.USBSUP_XFER_CRC => Linux.Errno.EILSEQ,
                VBoxUsb.UsbSupError.USBSUP_XFER_NAC => Linux.Errno.EPROTO,
                VBoxUsb.UsbSupError.USBSUP_XFER_UNDERRUN => Linux.Errno.EREMOTEIO,
                VBoxUsb.UsbSupError.USBSUP_XFER_OVERRUN => Linux.Errno.EOVERFLOW,
                _ => Linux.Errno.EPROTO,
            };
        }
    }
}
