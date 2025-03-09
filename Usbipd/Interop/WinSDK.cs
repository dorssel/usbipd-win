// SPDX-FileCopyrightText: 2020 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

using System.Runtime.InteropServices;
using Windows.Win32.Devices.Usb;

namespace Usbipd.Interop;

/// <summary>
/// The remaining Windows SDK stuff that is not (yet?) exposed by CsWin32. Bitfields, for example.
/// </summary>
static class WinSDK
{
    /// <summary>WinSDK: usbioctl.h: USB_DESCRIPTOR_REQUEST</summary>
    /// NOTE: CsWin32 gets this one wrong; its version is too long by one byte (probably due to Data[0]).
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct USB_DESCRIPTOR_REQUEST
    {
        public uint ConnectionIndex;
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        internal struct AnonymousSetupPacket
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
    /// NOTE: CsWin32 gets this one wrong; its version is too long (probably due to PipeList[0]).
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct USB_NODE_CONNECTION_INFORMATION_EX
    {
        public uint ConnectionIndex;
        public USB_DEVICE_DESCRIPTOR DeviceDescriptor;
        public byte CurrentConfigurationValue;
        /// <summary><see cref="USB_DEVICE_SPEED"/> as a <see cref="byte"/></summary>
        public byte Speed;
        public byte DeviceIsHub;
        public ushort DeviceAddress;
        public uint NumberOfOpenPipes;
        public uint ConnectionStatus;
        /* USB_PIPE_INFO PipeList[0]; */
    }
}
