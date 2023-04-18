// SPDX-FileCopyrightText: 2020 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

using System;
using System.Runtime.InteropServices;
using Windows.Win32;
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
    public struct USB_DESCRIPTOR_REQUEST
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
    /// NOTE: CsWin32 gets this one wrong; its version is too long (probably due to PipeList[0]).
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct USB_NODE_CONNECTION_INFORMATION_EX
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

    /// <summary>WinSDK: setupapi.h: ERROR_NO_DRIVER_SELECTED</summary>
    public const uint ERROR_NO_DRIVER_SELECTED = PInvoke.APPLICATION_ERROR_MASK | PInvoke.ERROR_SEVERITY_ERROR | 0x203;
}
