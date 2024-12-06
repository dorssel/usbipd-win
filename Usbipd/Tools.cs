// SPDX-FileCopyrightText: 2020 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Runtime.InteropServices;
using Windows.Win32.Devices.Usb;
using Windows.Win32.Foundation;

using static Usbipd.Interop.Linux;
using static Usbipd.Interop.UsbIp;
using static Usbipd.Interop.VBoxUsb;

namespace Usbipd;

static class Tools
{
    /// <summary>
    /// Slight variation on <see cref="Stream.ReadExactlyAsync(Memory{byte}, CancellationToken)" />.
    /// <para/>
    /// Throws <see cref="ProtocolViolationException"/> instead of <see cref="EndOfStreamException"/>
    /// if at least 1 byte was read, but end-of-stream is reached before reading the entire buffer.
    /// </summary>
    /// <exception cref="EndOfStreamException">If no bytes at all were read.</exception>
    /// <exception cref="ProtocolViolationException">If at least 1 byte was read, but not the entire buffer.</exception>
    public static async Task ReadMessageAsync(this Stream stream, Memory<byte> buf, CancellationToken cancellationToken)
    {
        if (buf.IsEmpty)
        {
            return;
        }
        var rlen = await stream.ReadAtLeastAsync(buf, 1, true, cancellationToken);
        if (rlen < buf.Length)
        {
            try
            {
                await stream.ReadExactlyAsync(buf[rlen..], cancellationToken);
            }
            catch (EndOfStreamException)
            {
                throw new ProtocolViolationException($"client disconnect in the middle of a message");
            }
        }
    }

    public static void StructToBytes<T>(in T s, Span<byte> bytes) where T : struct
    {
        var required = Marshal.SizeOf<T>();
        if (bytes.Length < required)
        {
            throw new ArgumentException($"buffer too small for structure: {bytes.Length} < {required}", nameof(bytes));
        }
        unsafe // DevSkim: ignore DS172412
        {
            fixed (byte* dst = bytes)
            {
                Marshal.StructureToPtr(s, (nint)dst, false);
            }
        }
    }

    public static byte[] StructToBytes<T>(in T s) where T : struct
    {
        var buf = new byte[Marshal.SizeOf<T>()];
        StructToBytes(s, buf);
        return buf;
    }

    public static void BytesToStruct<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors
        | DynamicallyAccessedMemberTypes.NonPublicConstructors)] T>(ReadOnlySpan<byte> bytes, out T s) where T : struct
    {
        var required = Marshal.SizeOf<T>();
        if (bytes.Length < required)
        {
            throw new ArgumentException($"buffer too small for structure: {bytes.Length} < {required}", nameof(bytes));
        }
        unsafe // DevSkim: ignore DS172412
        {
            fixed (byte* src = bytes)
            {
                s = Marshal.PtrToStructure<T>((nint)src);
            }
        }
    }

    public static T BytesToStruct<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors
        | DynamicallyAccessedMemberTypes.NonPublicConstructors)] T>(ReadOnlySpan<byte> bytes) where T : struct
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
        // Specs at https://www.kernel.org/doc/html/latest/usb/usbip_protocol.html state that
        // number_of_packets shall be 0xffffffff for non-ISO, but Linux itself often sets it to 0.
        return basic.ep == 0 ? UsbSupTransferType.USBSUP_TRANSFER_TYPE_MSG
            : submit.number_of_packets > 0 ? UsbSupTransferType.USBSUP_TRANSFER_TYPE_ISOC
            : submit.interval == 0 ? UsbSupTransferType.USBSUP_TRANSFER_TYPE_BULK
            : UsbSupTransferType.USBSUP_TRANSFER_TYPE_INTR;
    }

    public static Version UsbIpVersionToVersion(this ushort usbipVersion)
    {
        // See: https://www.kernel.org/doc/html/latest/usb/usbip_protocol.html
        //
        // (ushort)0x0111 -> 1.1.1
        // (ushort)0x0abc -> 10.11.12
        // (ushort)0xffff -> 255.15.15
        return new Version(usbipVersion >> 8, (usbipVersion >> 4) & 0xf, usbipVersion & 0xf);
    }
}
