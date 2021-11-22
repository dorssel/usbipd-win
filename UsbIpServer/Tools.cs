﻿// SPDX-FileCopyrightText: 2020 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-2.0-only

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using UsbIpServer.Interop;
using Windows.Win32;
using Windows.Win32.Devices.DeviceAndDriverInstallation;
using Windows.Win32.Devices.Properties;
using Windows.Win32.Devices.Usb;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell.PropertiesSystem;

namespace Windows.Win32.Devices.Properties
{
    partial struct DEVPROPKEY
    {
        /// <summary>
        /// *HACK*
        /// 
        /// CsWin32 confuses PROPERTYKEY and DEVPROPKEY, which are in fact the exact same structure.
        /// This is an implicit c++-like "reinterpret_cast".
        /// </summary>
        public static implicit operator DEVPROPKEY(in PROPERTYKEY propertyKey)
        {
            return Unsafe.As<PROPERTYKEY, DEVPROPKEY>(ref Unsafe.AsRef(propertyKey));
        }
    }
}

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

        /// <summary>
        /// Wrapper for <see cref="PInvoke.SetupDiGetClassDevs(Guid?, string, HWND, uint)"/> that returns
        /// a <see cref="SafeDeviceInfoSetHandle"/> instead of an unsafe <see cref="void"/>*.
        /// This removes the need to call SetupDiGetClassDevs from an unsafe context.
        /// </summary>
        public static SafeDeviceInfoSetHandle SetupDiGetClassDevs(Guid? ClassGuid, string? Enumerator, HWND hwndParent, uint Flags)
        {
            unsafe
            {
                SafeDeviceInfoSetHandle deviceInfoSet = new(PInvoke.SetupDiGetClassDevs(ClassGuid, Enumerator, hwndParent, Flags));
                if (deviceInfoSet.IsInvalid)
                {
                    throw new Win32Exception("SetupDiGetClassDevs");
                }
                return deviceInfoSet;
            }

        }

        public static bool IsUsbHub(string deviceInstanceId)
        {
            using var hubs = SetupDiGetClassDevs(PInvoke.GUID_DEVINTERFACE_USB_HUB, deviceInstanceId, default, PInvoke.DIGCF_DEVICEINTERFACE);
            return EnumDeviceInterfaces(hubs, PInvoke.GUID_DEVINTERFACE_USB_HUB).Any();
        }

        public static uint GetDevicePropertyUInt32(SafeDeviceInfoSetHandle deviceInfoSet, in SP_DEVINFO_DATA devInfoData, in PROPERTYKEY propertyKey)
        {
            unsafe
            {
                uint value;
                uint requiredSize;
                if (!PInvoke.SetupDiGetDeviceProperty(deviceInfoSet.PInvokeHandle, devInfoData, propertyKey, out var devPropType, (byte*)&value, 4, &requiredSize, 0))
                {
                    throw new Win32Exception("SetupDiGetDeviceProperty");
                }
                if (devPropType != PInvoke.DEVPROP_TYPE_UINT32)
                {
                    throw new UnexpectedResultException($"SetupDiGetDeviceProperty returned property type {devPropType}, expected {nameof(PInvoke.DEVPROP_TYPE_UINT32)}");
                }
                if (requiredSize != 4)
                {
                    throw new UnexpectedResultException($"SetupDiGetDeviceProperty returned inconsistent size {requiredSize} != 4");
                }
                return value;
            }
        }

        public static string GetDevicePropertyString(SafeDeviceInfoSetHandle deviceInfoSet, in SP_DEVINFO_DATA devInfoData, in PROPERTYKEY propertyKey)
        {
            unsafe
            {
                uint requiredSize;
                if (PInvoke.SetupDiGetDeviceProperty(deviceInfoSet.PInvokeHandle, devInfoData, propertyKey, out var devPropType, null, 0, &requiredSize, 0))
                {
                    throw new UnexpectedResultException($"SetupDiGetDeviceProperty succeeded, expected to fail with {WIN32_ERROR.ERROR_INSUFFICIENT_BUFFER}");
                }
                else if ((WIN32_ERROR)Marshal.GetLastWin32Error() != WIN32_ERROR.ERROR_INSUFFICIENT_BUFFER)
                {
                    throw new Win32Exception("SetupDiGetDeviceProperty");
                }
                if (devPropType != PInvoke.DEVPROP_TYPE_STRING)
                {
                    throw new UnexpectedResultException($"SetupDiGetDeviceProperty returned property type {devPropType}, expected {nameof(PInvoke.DEVPROP_TYPE_STRING)}");
                }
                if ((requiredSize < 2) || (requiredSize % 2 != 0))
                {
                    throw new UnexpectedResultException($"SetupDiGetDeviceProperty returned size {requiredSize}, expected an even number >= 2");
                }
                var output = stackalloc char[(int)requiredSize / 2];
                uint requiredSize2;
                if (!PInvoke.SetupDiGetDeviceProperty(deviceInfoSet.PInvokeHandle, devInfoData, propertyKey, out devPropType, (byte*)output, requiredSize, &requiredSize2, 0))
                {
                    throw new Win32Exception("SetupDiGetDeviceProperty");
                }
                if (devPropType != PInvoke.DEVPROP_TYPE_STRING)
                {
                    throw new UnexpectedResultException($"SetupDiGetDeviceProperty returned property type {devPropType}, expected {nameof(PInvoke.DEVPROP_TYPE_STRING)}");
                }
                if (requiredSize2 != requiredSize)
                {
                    throw new UnexpectedResultException($"SetupDiGetDeviceProperty returned inconsistent size {requiredSize2} != {requiredSize}");
                }
                if (output[requiredSize / 2 - 1] != '\0')
                {
                    throw new UnexpectedResultException($"SetupDiGetDeviceProperty returned non-NUL terminated string");
                }
                return new(output, 0, (int)(requiredSize / 2 - 1));
            }
        }

        public static string GetDevicePropertyString(string deviceInstanceId, in PROPERTYKEY propertyKey)
        {
            using var deviceInfoSet = SetupDiGetClassDevs(null, deviceInstanceId, default, PInvoke.DIGCF_DEVICEINTERFACE | PInvoke.DIGCF_ALLCLASSES | PInvoke.DIGCF_PRESENT);
            var devinfoData = EnumDeviceInfo(deviceInfoSet).Single();
            return GetDevicePropertyString(deviceInfoSet, devinfoData, propertyKey);
        }

        public static void SetDevicePropertyString(SafeDeviceInfoSetHandle deviceInfoSet, in SP_DEVINFO_DATA devInfoData, in PROPERTYKEY propertyKey, string? value)
        {
            unsafe
            {
                if (value is null)
                {
                    // This will delete the property
                    if (!PInvoke.SetupDiSetDeviceProperty(deviceInfoSet.PInvokeHandle, devInfoData, propertyKey, PInvoke.DEVPROP_TYPE_EMPTY, null, 0u, 0u))
                    {
                        throw new Win32Exception("SetupDiSetDeviceProperty");
                    }
                }
                else
                {
                    // Must include terminating NUL character
                    var buf = Encoding.Unicode.GetBytes(value + '\0');
                    fixed (SP_DEVINFO_DATA* pInfoData = &devInfoData)
                    {
                        fixed (PROPERTYKEY* pPropertyKey = &propertyKey)
                        {
                            fixed (byte* pBuf = buf)
                            {
                                if (!PInvoke.SetupDiSetDeviceProperty(deviceInfoSet.PInvokeHandle, pInfoData, (DEVPROPKEY*)pPropertyKey, PInvoke.DEVPROP_TYPE_STRING, pBuf, (uint)buf.Length, 0u))
                                {
                                    throw new Win32Exception("SetupDiSetDeviceProperty");
                                }
                            }
                        }
                    }
                }
            }
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

        static bool EnumDeviceInfo(SafeDeviceInfoSetHandle deviceInfoSet, uint index, out SP_DEVINFO_DATA devInfoData)
        {
            unsafe
            {
                devInfoData = new()
                {
                    cbSize = (uint)Marshal.SizeOf<SP_DEVINFO_DATA>(),
                };
                if (PInvoke.SetupDiEnumDeviceInfo(deviceInfoSet.PInvokeHandle, index, ref devInfoData))
                {
                    return true;
                }
                else if ((WIN32_ERROR)Marshal.GetLastWin32Error() == WIN32_ERROR.ERROR_NO_MORE_ITEMS)
                {
                    return false;
                }
                else
                {
                    throw new Win32Exception("SetupDiEnumDeviceInfo");
                }
            }
        }

        public static IEnumerable<SP_DEVINFO_DATA> EnumDeviceInfo(SafeDeviceInfoSetHandle deviceInfoSet)
        {
            uint index = 0;
            while (EnumDeviceInfo(deviceInfoSet, index++, out var devInfoData))
            {
                yield return devInfoData;
            }
        }

        static bool EnumDeviceInterfaces(SafeDeviceInfoSetHandle deviceInfoSet, SP_DEVINFO_DATA? devInfoData, in Guid interfaceClassGuid, uint index, out SP_DEVICE_INTERFACE_DATA interfaceData)
        {
            unsafe
            {
                interfaceData = new()
                {
                    cbSize = (uint)Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>(),
                };
                if (PInvoke.SetupDiEnumDeviceInterfaces(deviceInfoSet.PInvokeHandle, devInfoData, interfaceClassGuid, index, ref interfaceData))
                {
                    return true;
                }
                else if ((WIN32_ERROR)Marshal.GetLastWin32Error() == WIN32_ERROR.ERROR_NO_MORE_ITEMS)
                {
                    return false;
                }
                else
                {
                    throw new Win32Exception("SetupDiEnumDeviceInterfaces");
                }

            }
        }

        public static IEnumerable<(SP_DEVINFO_DATA infoData, SP_DEVICE_INTERFACE_DATA interfaceData)> EnumDeviceInterfaces(SafeDeviceInfoSetHandle deviceInfoSet, Guid interfaceClassGuid)
        {
            foreach (var devInfoData in EnumDeviceInfo(deviceInfoSet))
            {
                if (!EnumDeviceInterfaces(deviceInfoSet, devInfoData, interfaceClassGuid, 0, out var interfaceData))
                {
                    throw new Win32Exception("SetupDiEnumDeviceInterfaces");
                }
                yield return (devInfoData, interfaceData);
            }
        }

        public static string GetDeviceInterfaceDetail(SafeDeviceInfoSetHandle deviceInfoSet, in SP_DEVICE_INTERFACE_DATA interfaceData)
        {
            unsafe
            {
                uint requiredSize;
                if (PInvoke.SetupDiGetDeviceInterfaceDetail(deviceInfoSet.PInvokeHandle, interfaceData, null, 0, &requiredSize, null))
                {
                    throw new UnexpectedResultException("SetupDiGetDeviceInterfaceDetail succeeded, expected to fail with ERROR_INSUFFICIENT_BUFFER");
                }
                else if ((WIN32_ERROR)Marshal.GetLastWin32Error() != WIN32_ERROR.ERROR_INSUFFICIENT_BUFFER)
                {
                    throw new Win32Exception("SetupDiGetDeviceInterfaceDetail");
                }
                if ((requiredSize < 6) || (requiredSize % 2 != 0))
                {
                    throw new UnexpectedResultException($"SetupDiGetDeviceInterfaceDetail returned size {requiredSize}, expected an even number >= 6");
                }
                var output = stackalloc byte[(int)requiredSize];
                var detailData = (SP_DEVICE_INTERFACE_DETAIL_DATA_W*)output;
                detailData->cbSize = (uint)Marshal.SizeOf<SP_DEVICE_INTERFACE_DETAIL_DATA_W>();
                uint requiredSize2;
                if (!PInvoke.SetupDiGetDeviceInterfaceDetail(deviceInfoSet.PInvokeHandle, interfaceData, detailData, requiredSize, &requiredSize2, null))
                {
                    throw new Win32Exception("SetupDiGetDeviceInterfaceDetail");
                }
                if (requiredSize2 != requiredSize)
                {
                    throw new UnexpectedResultException($"SetupDiGetDeviceInterfaceDetail returned inconsistent size {requiredSize2} != {requiredSize}");
                }
                return new(&detailData->DevicePath._0);
            }
        }

        public static void GetBusId(SafeDeviceInfoSetHandle deviceInfoSet, in SP_DEVINFO_DATA devInfoData, out BusId busId)
        {
            var locationInfo = GetDevicePropertyString(deviceInfoSet, devInfoData, PInvoke.DEVPKEY_Device_LocationInfo);
            var match = Regex.Match(locationInfo, "^Port_#([0-9]{4}).Hub_#([0-9]{4})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!match.Success)
            {
                throw new NotSupportedException($"DEVPKEY_Device_LocationInfo returned '{locationInfo}', expected form 'Port_#0123.Hub_#4567'");
            }
            // HACK
            // Use Address only, and ignore Port_#
            var address = GetDevicePropertyUInt32(deviceInfoSet, devInfoData, PInvoke.DEVPKEY_Device_Address);
            busId = new()
            {
                Bus = ushort.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture),
                Port = (ushort)address,
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
