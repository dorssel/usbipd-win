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
    /// usbioctl.h is not yet scraped by win32metadata.
    /// See: https://github.com/microsoft/win32metadata/issues/691
    /// </summary>
    static class WinSDK
    {
        public enum IoControl : uint
        {
            /// <summary>WinSDK: usbioctl.h</summary>
            IOCTL_USB_GET_DESCRIPTOR_FROM_NODE_CONNECTION = (PInvoke.FILE_DEVICE_USB << 16) | (PInvoke.FILE_ANY_ACCESS << 14)
                | (PInvoke.USB_GET_DESCRIPTOR_FROM_NODE_CONNECTION << 2) | (PInvoke.METHOD_BUFFERED),

            /// <summary>WinSDK: usbioctl.h</summary>
            IOCTL_USB_GET_NODE_CONNECTION_INFORMATION_EX = (PInvoke.FILE_DEVICE_USB << 16) | (PInvoke.FILE_ANY_ACCESS << 14)
                | (PInvoke.USB_GET_NODE_CONNECTION_INFORMATION_EX << 2) | (PInvoke.METHOD_BUFFERED),

            /// <summary>WinSDK: usbioctl.h</summary>
            IOCTL_USB_GET_NODE_CONNECTION_INFORMATION_EX_V2 = (PInvoke.FILE_DEVICE_USB << 16) | (PInvoke.FILE_ANY_ACCESS << 14)
                | (PInvoke.USB_GET_NODE_CONNECTION_INFORMATION_EX_V2 << 2) | (PInvoke.METHOD_BUFFERED),

            /// <summary>WinSDK: usbioctl.h</summary>
            IOCTL_USB_HUB_CYCLE_PORT = (PInvoke.FILE_DEVICE_USB << 16) | (PInvoke.FILE_ANY_ACCESS << 14)
                | (PInvoke.USB_HUB_CYCLE_PORT << 2) | (PInvoke.METHOD_BUFFERED),
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

        /// <summary>WinSDK: usbioctl.h: USB_CYCLE_PORT_PARAMS</summary>
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct UsbCyclePortParams
        {
            public uint ConnectionIndex;
            public uint StatusReturned;
        }

        /// <summary>WinSDK: setupapi.h: ERROR_NO_DRIVER_SELECTED</summary>
        public const uint ERROR_NO_DRIVER_SELECTED = PInvoke.APPLICATION_ERROR_MASK | PInvoke.ERROR_SEVERITY_ERROR | 0x203;
    }
}
