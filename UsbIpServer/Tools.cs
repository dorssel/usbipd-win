// SPDX-FileCopyrightText: 2020 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-2.0-only

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using UsbIpServer.Interop;
using static UsbIpServer.Interop.WinSDK;

namespace UsbIpServer
{
    static class Tools
    {
        public static async Task RecvExactSizeAsync(Stream stream, Memory<byte> buf, CancellationToken cancellationToken)
        {
            var remain = buf.Length;
            while (remain > 0)
            {
                var rlen = await stream.ReadAsync(buf[^remain..], cancellationToken);
                if (rlen == 0)
                {
                    throw new EndOfStreamException();
                }
                remain -= rlen;
            }
        }

        public static void StructToBytes<T>(in T s, byte[] bytes, int offset) where T : struct
        {
            if (offset > bytes.Length)
            {
                throw new ArgumentException($"offset {offset} > array length {bytes.Length}", nameof(offset));
            }
            if (bytes.Length - offset < Marshal.SizeOf<T>())
            {
                throw new ArgumentException($"remaining buffer length {bytes.Length - offset} (from offset {offset}) < structure size {Marshal.SizeOf<T>()}", nameof(bytes));
            }
            unsafe
            {
                fixed (byte* dst = bytes)
                {
                    Marshal.StructureToPtr(s, (IntPtr)(dst + offset), false);
                }
            }
        }

        public static byte[] StructToBytes<T>(in T s) where T : struct
        {
            var buf = new byte[Marshal.SizeOf<T>()];
            StructToBytes(s, buf, 0);
            return buf;
        }

        public static void BytesToStruct<T>(ReadOnlySpan<byte> bytes, int offset, out T s) where T : struct
        {
            if (offset > bytes.Length)
            {
                throw new ArgumentException($"offset {offset} > array length {bytes.Length}", nameof(offset));
            }
            if (bytes.Length - offset < Marshal.SizeOf<T>())
            {
                throw new ArgumentException($"remaining buffer length {bytes.Length - offset} (from offset {offset}) < structure size {Marshal.SizeOf<T>()}", nameof(bytes));
            }
            unsafe
            {
                fixed (byte* src = bytes)
                {
                    s = Marshal.PtrToStructure<T>((IntPtr)(src + offset));
                }
            }
        }

        public static T BytesToStruct<T>(ReadOnlySpan<byte> bytes, int offset) where T : struct
        {
            BytesToStruct(bytes, offset, out T result);
            return result;
        }

        public static bool IsUsbHub(string deviceInstanceId)
        {
            using var hubs = NativeMethods.SetupDiGetClassDevs(GUID_DEVINTERFACE_USB_HUB, deviceInstanceId, IntPtr.Zero, DiGetClassFlags.DIGCF_DEVICEINTERFACE);
            return EnumDeviceInterfaces(hubs, GUID_DEVINTERFACE_USB_HUB).Any();
        }

        public static uint GetDevicePropertyUInt32(SafeDeviceInfoSetHandle deviceInfoSet, SpDevInfoData devInfoData, DevPropKey devPropKey)
        {
            var output = new byte[4];
            if (!NativeMethods.SetupDiGetDeviceProperty(deviceInfoSet, devInfoData, devPropKey, out var devPropType, output, (uint)output.Length, out var requiredSize, 0))
            {
                throw new Win32Exception("SetupDiGetDeviceProperty");
            }
            if (devPropType != DevPropType.DEVPROP_TYPE_UINT32)
            {
                throw new UnexpectedResultException($"SetupDiGetDeviceProperty returned property type {devPropType}, expected {DevPropType.DEVPROP_TYPE_UINT32}");
            }
            if (requiredSize != 4)
            {
                throw new UnexpectedResultException($"SetupDiGetDeviceProperty returned inconsistent size {requiredSize} != 4");
            }
            return BinaryPrimitives.ReadUInt32LittleEndian(output);
        }


        public static string GetDevicePropertyString(SafeDeviceInfoSetHandle deviceInfoSet, SpDevInfoData devInfoData, DevPropKey devPropKey)
        {
            if (NativeMethods.SetupDiGetDeviceProperty(deviceInfoSet, devInfoData, devPropKey, out var devPropType, null, 0, out var requiredSize, 0))
            {
                throw new UnexpectedResultException($"SetupDiGetDeviceProperty succeeded, expected to fail with {Win32Error.ERROR_INSUFFICIENT_BUFFER}");
            }
            else if ((Win32Error)Marshal.GetLastWin32Error() != Win32Error.ERROR_INSUFFICIENT_BUFFER)
            {
                throw new Win32Exception("SetupDiGetDeviceProperty");
            }
            if (devPropType != DevPropType.DEVPROP_TYPE_STRING)
            {
                throw new UnexpectedResultException($"SetupDiGetDeviceProperty returned property type {devPropType}, expected {DevPropType.DEVPROP_TYPE_STRING}");
            }
            if ((requiredSize < 2) || (requiredSize % 2 != 0))
            {
                throw new UnexpectedResultException($"SetupDiGetDeviceProperty returned size {requiredSize}, expected an even number >= 2");
            }
            var output = new byte[requiredSize];
            if (!NativeMethods.SetupDiGetDeviceProperty(deviceInfoSet, devInfoData, devPropKey, out devPropType, output, (uint)output.Length, out var requiredSize2, 0))
            {
                throw new Win32Exception("SetupDiGetDeviceProperty");
            }
            if (devPropType != DevPropType.DEVPROP_TYPE_STRING)
            {
                throw new UnexpectedResultException($"SetupDiGetDeviceProperty returned property type {devPropType}, expected {DevPropType.DEVPROP_TYPE_STRING}");
            }
            if (requiredSize2 != requiredSize)
            {
                throw new UnexpectedResultException($"SetupDiGetDeviceProperty returned inconsistent size {requiredSize2} != {requiredSize}");
            }
            var result = Encoding.Unicode.GetString(output);
            if (result[^1] != '\0')
            {
                throw new UnexpectedResultException($"SetupDiGetDeviceProperty returned non-NUL terminated string");
            }
            result = result[..^1];

            return result;
        }

        public static string GetDevicePropertyString(string deviceInstanceId, DevPropKey propKey)
        {
            using var deviceInfoSet = NativeMethods.SetupDiGetClassDevs(IntPtr.Zero, deviceInstanceId, IntPtr.Zero,
                DiGetClassFlags.DIGCF_DEVICEINTERFACE | DiGetClassFlags.DIGCF_ALLCLASSES | DiGetClassFlags.DIGCF_PRESENT);
            if (deviceInfoSet.IsInvalid)
            {
                throw new Win32Exception("SetupDiGetClassDevs");
            }
            var devinfoData = EnumDeviceInfo(deviceInfoSet).Single();
            return GetDevicePropertyString(deviceInfoSet, devinfoData, propKey);
        }

        public static Linux.UsbDeviceSpeed MapWindowsSpeedToLinuxSpeed(UsbDeviceSpeed w)
        {
            // Windows and Linux each use a *different* enum for this
            return w switch
            {
                UsbDeviceSpeed.UsbLowSpeed => Linux.UsbDeviceSpeed.USB_SPEED_LOW,
                UsbDeviceSpeed.UsbFullSpeed => Linux.UsbDeviceSpeed.USB_SPEED_FULL,
                UsbDeviceSpeed.UsbHighSpeed => Linux.UsbDeviceSpeed.USB_SPEED_HIGH,
                UsbDeviceSpeed.UsbSuperSpeed => Linux.UsbDeviceSpeed.USB_SPEED_SUPER,
                _ => Linux.UsbDeviceSpeed.USB_SPEED_UNKNOWN,
            };
        }

        public static IEnumerable<SpDevInfoData> EnumDeviceInfo(SafeDeviceInfoSetHandle deviceInfoSet)
        {
            var devInfoData = new SpDevInfoData() { cbSize = (uint)Marshal.SizeOf<SpDevInfoData>() };
            for (uint i = 0; ; ++i)
            {
                if (!NativeMethods.SetupDiEnumDeviceInfo(deviceInfoSet, i, ref devInfoData))
                {
                    if ((Win32Error)Marshal.GetLastWin32Error() == Win32Error.ERROR_NO_MORE_ITEMS)
                    {
                        yield break;
                    }
                    throw new Win32Exception("SetupDiEnumDeviceInfo");
                }
                yield return devInfoData;
            }
        }

        public static IEnumerable<(SpDevInfoData infoData, SpDeviceInterfaceData interfaceData)> EnumDeviceInterfaces(SafeDeviceInfoSetHandle deviceInfoSet, Guid interfaceClassGuid)
        {
            var infoData = new SpDevInfoData() { cbSize = (uint)Marshal.SizeOf<SpDevInfoData>() };
            var interfaceData = new SpDeviceInterfaceData() { cbSize = (uint)Marshal.SizeOf<SpDeviceInterfaceData>() };
            for (uint i = 0; ; ++i)
            {
                if (!NativeMethods.SetupDiEnumDeviceInfo(deviceInfoSet, i, ref infoData))
                {
                    if ((Win32Error)Marshal.GetLastWin32Error() == Win32Error.ERROR_NO_MORE_ITEMS)
                    {
                        yield break;
                    }
                    throw new Win32Exception("SetupDiEnumDeviceInfo");
                }
                if (!NativeMethods.SetupDiEnumDeviceInterfaces(deviceInfoSet, IntPtr.Zero, interfaceClassGuid, i, ref interfaceData))
                {
                    throw new Win32Exception("SetupDiEnumDeviceInterfaces");
                }
                yield return (infoData, interfaceData);
            }
        }

        public static string GetDeviceInterfaceDetail(SafeDeviceInfoSetHandle deviceInfoSet, in SpDeviceInterfaceData interfaceData)
        {
            if (NativeMethods.SetupDiGetDeviceInterfaceDetail(deviceInfoSet, interfaceData, null, 0, out var requiredSize, IntPtr.Zero))
            {
                throw new UnexpectedResultException("SetupDiGetDeviceInterfaceDetail succeeded, expected to fail with ERROR_INSUFFICIENT_BUFFER");
            }
            else if ((Win32Error)Marshal.GetLastWin32Error() != Win32Error.ERROR_INSUFFICIENT_BUFFER)
            {
                throw new Win32Exception("SetupDiGetDeviceInterfaceDetail");
            }
            if ((requiredSize < 6) || (requiredSize % 2 != 0))
            {
                throw new UnexpectedResultException($"SetupDiGetDeviceInterfaceDetail returned size {requiredSize}, expected an even number >= 6");
            }
            var output = new byte[requiredSize];
            BinaryPrimitives.WriteUInt32LittleEndian(output, 8 /* sizeof(SP_DEVICE_INTERFACE_DETAIL_DATA) */);
            if (!NativeMethods.SetupDiGetDeviceInterfaceDetail(deviceInfoSet, interfaceData, output, (uint)output.Length, out var requiredSize2, IntPtr.Zero))
            {
                throw new Win32Exception("SetupDiGetDeviceInterfaceDetail");
            }
            if (requiredSize2 != requiredSize)
            {
                throw new UnexpectedResultException($"SetupDiGetDeviceInterfaceDetail returned inconsistent size {requiredSize2} != {requiredSize}");
            }
            var devicePath = Encoding.Unicode.GetString(output.AsSpan(4));
            if (devicePath[^1] != '\0')
            {
                throw new UnexpectedResultException($"SetupDiGetDeviceInterfaceDetail returned non-NUL terminated string");
            }
            devicePath = devicePath[..^1];
            return devicePath;
        }

        public static void GetBusId(SafeDeviceInfoSetHandle deviceInfoSet, in SpDevInfoData devInfoData, out ushort hubNum, out ushort connectionIndex)
        {
            var locationInfo = GetDevicePropertyString(deviceInfoSet, devInfoData, DEVPKEY_Device_LocationInfo);
            var match = Regex.Match(locationInfo, "^Port_#([0-9]{4}).Hub_#([0-9]{4})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!match.Success)
            {
                throw new NotSupportedException($"DEVPKEY_Device_LocationInfo returned '{locationInfo}', expected form 'Port_#0123.Hub_#4567'");
            }
            hubNum = ushort.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
            connectionIndex = ushort.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            if (hubNum == 0)
            {
                throw new NotSupportedException($"DEVPKEY_Device_LocationInfo returned unexpected {nameof(hubNum)} 0");
            }
            if (connectionIndex == 0)
            {
                throw new NotSupportedException($"DEVPKEY_Device_LocationInfo returned unexpected {nameof(connectionIndex)} 0");
            }
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
