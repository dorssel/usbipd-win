/*
    usbipd-win: a server for hosting USB devices across networks
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

using Microsoft.Win32.SafeHandles;
using System;
using System.IO;
using System.Runtime.InteropServices;
using static UsbIpServer.Interop.Usb;

namespace UsbIpServer.Interop
{
    static class WinSDK
    {
        public enum Win32Error : uint
        {
            /// <summary>WinSDK: winerror.h</summary>
            ERROR_SUCCESS = 0,

            /// <summary>WinSDK: winerror.h</summary>
            ERROR_INSUFFICIENT_BUFFER = 122,

            /// <summary>WinSDK: winerror.h</summary>
            ERROR_NO_MORE_ITEMS = 259,

            /// <summary>WinSDK: winerror.h</summary>
            ERROR_IO_PENDING = 997,
        }

        [Flags]
        public enum FileFlags : uint
        {
            /// <summary>WinSDK: WinBase.h</summary>
            FILE_FLAG_OVERLAPPED = (1 << 30),
        }

        /// <summary>WinSDK: devpropdev.h: DEVPROPTYPE</summary>
        public enum DevPropType : uint
        {
            /// <summary>WinSDK: devpropdev.h</summary>
            DEVPROP_TYPE_UINT32 = 0x00000007,

            /// <summary>WinSDK: devpropdev.h</summary>
            DEVPROP_TYPE_STRING = 0x00000012,
        }

        [Flags]
        public enum DiGetClassFlags : uint
        {
            /// <summary>WinSDK: SetupAPI.h</summary>
            DIGCF_DEFAULT = (1 << 0),

            /// <summary>WinSDK: SetupAPI.h</summary>
            DIGCF_PRESENT = (1 << 1),

            /// <summary>WinSDK: SetupAPI.h</summary>
            DIGCF_ALLCLASSES = (1 << 2),

            /// <summary>WinSDK: SetupAPI.h</summary>
            DIGCF_PROFILE = (1 << 3),

            /// <summary>WinSDK: SetupAPI.h</summary>
            DIGCF_DEVICEINTERFACE = (1 << 4),
        }

        /// <summary>WinSDK: devpropdev.h: DEVPROPKEY</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct DevPropKey
        {
            public Guid fmtid;
            public uint pid;
        }

        /// <summary>WinSDK: devpkey.h</summary>
        public static readonly DevPropKey DEVPKEY_Device_InstanceId = new DevPropKey()
        {
            fmtid = new Guid(0x78c34fc8, 0x104a, 0x4aca, 0x9e, 0xa4, 0x52, 0x4d, 0x52, 0x99, 0x6e, 0x57),
            pid = 256
        };

        /// <summary>WinSDK: devpkey.h</summary>
        public static readonly DevPropKey DEVPKEY_Device_Parent = new DevPropKey()
        {
            fmtid = new Guid(0x4340a6c5, 0x93fa, 0x4706, 0x97, 0x2c, 0x7b, 0x64, 0x80, 0x08, 0xa5, 0xa7),
            pid = 8
        };

        /// <summary>WinSDK: devpkey.h</summary>
        public static readonly DevPropKey DEVPKEY_Device_LocationInfo = new DevPropKey()
        {
            fmtid = new Guid(0xa45c254e, 0xdf1c, 0x4efd, 0x80, 0x20, 0x67, 0xd1, 0x46, 0xa8, 0x50, 0xe0),
            pid = 15
        };

        /// <summary>WinSDK: devpkey.h</summary>
        public static readonly DevPropKey DEVPKEY_Device_Address = new DevPropKey()
        {
            fmtid = new Guid(0xa45c254e, 0xdf1c, 0x4efd, 0x80, 0x20, 0x67, 0xd1, 0x46, 0xa8, 0x50, 0xe0),
            pid = 30
        };

        /// <summary>WinSDK: SetupAPI.h: SP_DEVICE_INTERFACE_DATA</summary>
        [StructLayout(LayoutKind.Sequential, Pack = 8)]
        public struct SpDeviceInterfaceData
        {
            public uint cbSize;
            public Guid interfaceClassGuid;
            public uint flags;
            private readonly UIntPtr reserved;
        }

        /// <summary>WinSDK: SetupAPI.h: SP_DEVINFO_DATA</summary>
        [StructLayout(LayoutKind.Sequential, Pack = 8)]
        public struct SpDevInfoData
        {
            public uint cbSize;
            public Guid ClassGuid;
            public uint DevInst;
            private readonly UIntPtr Reserved;
        }

        public static class NativeMethods
        {
            [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
            public static extern SafeFileHandle CreateFile(string lpFileName, [MarshalAs(UnmanagedType.U4)] FileAccess dwDesiredAccess,
                [MarshalAs(UnmanagedType.U4)] FileShare dwShareMode, IntPtr lpSecurityAttributes, [MarshalAs(UnmanagedType.U4)] FileMode dwCreationDisposition,
                [MarshalAs(UnmanagedType.U4)] FileAttributes dwFlagsAndAttributes, IntPtr hTemplateFile);

            [DllImport("kernel32.dll", SetLastError = true)]
            public static extern bool DeviceIoControl(SafeFileHandle hDevice, uint ioControlCode, IntPtr inBuffer, uint nInBufferSize,
                IntPtr outBuffer, uint nOutBufferSize, out uint pBytesReturned, IntPtr overlapped);

            [DllImport("setupapi.dll", SetLastError = true)]
            public static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);

            [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
            public static extern SafeDeviceInfoSetHandle SetupDiGetClassDevs(IntPtr ClassGuid, [MarshalAs(UnmanagedType.LPWStr)] string? Enumerator,
                IntPtr hwndParent, DiGetClassFlags Flags);

            [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
            public static extern SafeDeviceInfoSetHandle SetupDiGetClassDevs(in Guid ClassGuid, [MarshalAs(UnmanagedType.LPWStr)] string? Enumerator,
                IntPtr hwndParent, DiGetClassFlags Flags);

            [DllImport("setupapi.dll", SetLastError = true)]
            public static extern bool SetupDiEnumDeviceInfo(SafeDeviceInfoSetHandle DeviceInfoSet, uint MemberIndex, ref SpDevInfoData DeviceInfoData);

            [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
            public static extern bool SetupDiGetDeviceProperty(SafeDeviceInfoSetHandle DeviceInfoSet, in SpDevInfoData DeviceInfoData,
                in DevPropKey PropertyKey, [MarshalAs(UnmanagedType.U4)] out DevPropType PropertyType,
                byte[]? PropertyBuffer, uint PropertyBufferSize, out uint RequiredSize, uint Flags);

            [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
            public static extern bool SetupDiGetDeviceProperty(SafeDeviceInfoSetHandle DeviceInfoSet, in SpDevInfoData DeviceInfoData,
                in DevPropKey PropertyKey, [MarshalAs(UnmanagedType.U4)] out DevPropType PropertyType,
                byte[]? PropertyBuffer, uint PropertyBufferSize, IntPtr RequiredSize, uint Flags);

            [DllImport("setupapi.dll", SetLastError = true)]
            public static extern bool SetupDiEnumDeviceInterfaces(SafeDeviceInfoSetHandle hDevInfo, IntPtr devInfo,
                in Guid interfaceClassGuid, uint memberIndex, ref SpDeviceInterfaceData deviceInterfaceData);

            [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
            public static extern bool SetupDiGetDeviceInterfaceDetail(
               SafeDeviceInfoSetHandle hDevInfo, in SpDeviceInterfaceData DeviceInterfaceData, byte[]? DeviceInterfaceDetailData, uint DeviceInterfaceDetailDataSize,
               out uint RequiredSize, IntPtr DeviceInfoData);
        }

        /// <summary>WinSDK: usbiodef.h</summary>
        public static readonly Guid GUID_DEVINTERFACE_USB_HUB = new Guid(0xf18a0e88, 0xc30c, 0x11d0, 0x88, 0x15, 0x00, 0xa0, 0xc9, 0x06, 0xbe, 0xd8);

        public enum DeviceType : ushort
        {
            /// <summary>WinSDK: winioctl.h</summary>
            FILE_DEVICE_UNKNOWN = 0x0022,

            /// <summary>WinSDK: usbiodef.h</summary>
            FILE_DEVICE_USB = FILE_DEVICE_UNKNOWN,
        }

        public enum FunctionCode : ushort
        {
            /// <summary>WinSDK: usbiodef.h</summary>
            USB_GET_DESCRIPTOR_FROM_NODE_CONNECTION = 260,

            /// <summary>WinSDK: usbiodef.h</summary>
            USB_GET_NODE_CONNECTION_INFORMATION_EX = 274,
        }

        public enum MethodCode : byte
        {
            /// <summary>WinSDK: winioctl.h</summary>
            METHOD_BUFFERED = 0,
        }

        public enum AccessCode : byte
        {
            /// <summary>WinSDK: winioctl.h</summary>
            FILE_ANY_ACCESS = 0,
            FILE_READ_ACCESS,
            FILE_WRITE_ACCESS,
        }

        public enum IoControl : uint
        {
            /// <summary>WinSDK: usbioctl.h</summary>
            IOCTL_USB_GET_DESCRIPTOR_FROM_NODE_CONNECTION = (DeviceType.FILE_DEVICE_USB << 16) | (AccessCode.FILE_ANY_ACCESS << 14)
                | (FunctionCode.USB_GET_DESCRIPTOR_FROM_NODE_CONNECTION << 2) | (MethodCode.METHOD_BUFFERED),

            /// <summary>WinSDK: usbioctl.h</summary>
            IOCTL_USB_GET_NODE_CONNECTION_INFORMATION_EX = (DeviceType.FILE_DEVICE_USB << 16) | (AccessCode.FILE_ANY_ACCESS << 14)
                | (FunctionCode.USB_GET_NODE_CONNECTION_INFORMATION_EX << 2) | (MethodCode.METHOD_BUFFERED),
        }

        /// <summary>WinSDK: usbioctl.h: USB_DESCRIPTOR_REQUEST</summary>
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct UsbDescriptorRequest
        {
            public uint ConnectionIndex;
            [StructLayout(LayoutKind.Sequential, Pack = 1)]
            public struct AnonymousSetupPacket
            {
                public byte bmRequest;
                public byte bRequest;
                public ushort wValue;
                public ushort wIndex;
                public ushort wLength;
            }
            public AnonymousSetupPacket SetupPacket;
            /* UCHAR Data[0]; */
        }

        /// <summary>WinSDK: usbspec.h: USB_DEVICE_SPEED
        /// <para><seealso cref="Linux.UsbDeviceSpeed"/></para></summary>
        public enum UsbDeviceSpeed : byte
        {
            UsbLowSpeed = 0,
            UsbFullSpeed,
            UsbHighSpeed,
            UsbSuperSpeed,
        }

        /// <summary>WinSDK: usbioctl.h: USB_NODE_CONNECTION_INFORMATION_EX</summary>
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct UsbNodeConnectionInformationEx
        {
            public uint ConnectionIndex;
            public UsbDeviceDescriptor DeviceDescriptor;
            public byte CurrentConfigurationValue;
            public UsbDeviceSpeed Speed;
            public byte DeviceIsHub;
            public ushort DeviceAddress;
            public uint NumberOfOpenPipes;
            public uint ConnectionStatus;
            /* USB_PIPE_INFO PipeList[0]; */
        }
    }
}
