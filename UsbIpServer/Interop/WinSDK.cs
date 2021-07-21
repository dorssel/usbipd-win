// SPDX-FileCopyrightText: 2020 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-2.0-only

using System;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Devices.Usb;

namespace UsbIpServer.Interop
{
    /// <summary>
    /// The remaining Windows SDK stuff that is not yet exposed by CsWin32.
    /// Apparently, usbiodef.h and usbioctl.h are not yet scraped by win32metadata.
    /// </summary>
    static class WinSDK
    {
        /// <summary>WinSDK: usbiodef.h</summary>
        public const uint FILE_DEVICE_USB = Constants.FILE_DEVICE_UNKNOWN;

        public enum IoControl : uint
        {
            /// <summary>WinSDK: usbioctl.h</summary>
            IOCTL_USB_GET_DESCRIPTOR_FROM_NODE_CONNECTION = (FILE_DEVICE_USB << 16) | (Constants.FILE_ANY_ACCESS << 14)
                | (Constants.USB_GET_DESCRIPTOR_FROM_NODE_CONNECTION << 2) | (Constants.METHOD_BUFFERED),

            /// <summary>WinSDK: usbioctl.h</summary>
            IOCTL_USB_GET_NODE_CONNECTION_INFORMATION_EX = (FILE_DEVICE_USB << 16) | (Constants.FILE_ANY_ACCESS << 14)
                | (Constants.USB_GET_NODE_CONNECTION_INFORMATION_EX << 2) | (Constants.METHOD_BUFFERED),

            /// <summary>WinSDK: usbioctl.h</summary>
            IOCTL_USB_GET_NODE_CONNECTION_INFORMATION_EX_V2 = (FILE_DEVICE_USB << 16) | (Constants.FILE_ANY_ACCESS << 14)
                | (Constants.USB_GET_NODE_CONNECTION_INFORMATION_EX_V2 << 2) | (Constants.METHOD_BUFFERED),
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

        /// <summary>WinSDK: usbioctl.h: USB_NODE_CONNECTION_INFORMATION_EX</summary>
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct UsbNodeConnectionInformationEx
        {
            public uint ConnectionIndex;
            public USB_DEVICE_DESCRIPTOR DeviceDescriptor;
            public byte CurrentConfigurationValue;
            /// <summary><see cref="Windows.Win32.Devices.Usb.USB_DEVICE_SPEED"/> as a <see cref="byte"/></summary>
            public byte Speed;
            public byte DeviceIsHub;
            public ushort DeviceAddress;
            public uint NumberOfOpenPipes;
            public uint ConnectionStatus;
            /* USB_PIPE_INFO PipeList[0]; */
        }

        /// <summary>WinSDK: usbioctl.h: USB_PROTOCOLS</summary>
        [Flags]
        public enum UsbProtocols : uint
        {
            None = 0,
            Usb110 = (1 << 0),
            Usb200 = (1 << 1),
            Usb300 = (1 << 2),
        }

        /// <summary>WinSDK: usbioctl.h: USB_NODE_CONNECTION_INFORMATION_EX_V2_FLAGS</summary>
        [Flags]
        public enum UsbNodeConnectionInformationExV2Flags : uint
        {
            DeviceIsOperatingAtSuperSpeedOrHigher = (1 << 0),
            DeviceIsSuperSpeedCapableOrHigher = (1 << 1),
            DeviceIsOperatingAtSuperSpeedPlusOrHigher = (1 << 2),
            DeviceIsSuperSpeedPlusCapableOrHigher = (1 << 3),
        }

        /// <summary>WinSDK: usbioctl.h: USB_NODE_CONNECTION_INFORMATION_EX_V2</summary>
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct UsbNodeConnectionInformationExV2
        {
            public uint ConnectionIndex;
            public uint Length;
            public UsbProtocols SupportedUsbProtocols;
            public UsbNodeConnectionInformationExV2Flags Flags;
        }
    }
}
