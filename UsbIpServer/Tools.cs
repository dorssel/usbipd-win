// SPDX-FileCopyrightText: 2020 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-2.0-only

using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.Win32.Devices.Usb;
using Windows.Win32.Foundation;

using static UsbIpServer.Interop.Linux;
using static UsbIpServer.Interop.UsbIp;
using static UsbIpServer.Interop.VBoxUsb;

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

        public static UsbDeviceSpeed MapWindowsSpeedToLinuxSpeed(USB_DEVICE_SPEED w)
        {
            // Windows and Linux each use a *different* enum for this
            return w switch
            {
                USB_DEVICE_SPEED.UsbLowSpeed => UsbDeviceSpeed.USB_SPEED_LOW,
                USB_DEVICE_SPEED.UsbFullSpeed => UsbDeviceSpeed.USB_SPEED_FULL,
                USB_DEVICE_SPEED.UsbHighSpeed => UsbDeviceSpeed.USB_SPEED_HIGH,
                USB_DEVICE_SPEED.UsbSuperSpeed => UsbDeviceSpeed.USB_SPEED_SUPER,
                _ => UsbDeviceSpeed.USB_SPEED_UNKNOWN,
            };
        }

        /// <summary>
        /// See <see href="https://www.kernel.org/doc/html/latest/driver-api/usb/error-codes.html"/>.
        /// </summary>
        public static Errno ConvertError(UsbSupError usbSupError)
        {
            return usbSupError switch
            {
                UsbSupError.USBSUP_XFER_OK => Errno.SUCCESS,
                UsbSupError.USBSUP_XFER_STALL => Errno.EPIPE,
                UsbSupError.USBSUP_XFER_DNR => Errno.ETIME,
                UsbSupError.USBSUP_XFER_CRC => Errno.EILSEQ,
                UsbSupError.USBSUP_XFER_NAC => Errno.EPROTO,
                UsbSupError.USBSUP_XFER_UNDERRUN => Errno.EREMOTEIO,
                UsbSupError.USBSUP_XFER_OVERRUN => Errno.EOVERFLOW,
                _ => Errno.EPROTO,
            };
        }

        public static void ThrowOnError(this BOOL success, string message)
        {
            if (!success)
            {
                throw new Win32Exception(message);
            }
        }

        public static byte RawEndpoint(this UsbIpHeaderBasic basic)
        {
            return (byte)((basic.ep & 0x7f) | (basic.direction == UsbIpDir.USBIP_DIR_IN ? 0x80u : 0x00u));
        }

        public static UsbSupTransferType EndpointType(this UsbIpHeaderBasic basic, UsbIpHeaderCmdSubmit submit)
        {
            if (basic.ep == 0)
            {
                return UsbSupTransferType.USBSUP_TRANSFER_TYPE_MSG;
            }
            else if (submit.number_of_packets > 0)
            {
                // Specs at https://www.kernel.org/doc/html/latest/usb/usbip_protocol.html state that
                // this shall be 0xffffffff for non-ISO, but Linux itself often sets it to 0.
                return UsbSupTransferType.USBSUP_TRANSFER_TYPE_ISOC;
            }
            else if (submit.interval != 0)
            {
                return UsbSupTransferType.USBSUP_TRANSFER_TYPE_INTR;
            }
            else
            {
                return UsbSupTransferType.USBSUP_TRANSFER_TYPE_BULK;
            }
        }
    }
}
