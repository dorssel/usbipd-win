// SPDX-FileCopyrightText: 2020 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-2.0-only

using System;
using System.Runtime.InteropServices;
using Windows.Win32;

namespace UsbIpServer.Interop
{
    static class VBoxUsb
    {
        /// <summary>VBoxUsb: usblib-win.h: USBSUP_CLAIMDEV</summary>
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct UsbSupClaimDev
        {
            readonly byte bInterfaceNumber;
            [MarshalAs(UnmanagedType.U1)]
            public bool fClaimed;
        }

        public const string StubHardwareId = "VID_80EE&PID_CAFE";

        /// <summary>VBoxUsb: usblib-win.h</summary>
        public static readonly Guid GUID_CLASS_VBOXUSB = new(0x873fdf, 0xCAFE, 0x80EE, 0xaa, 0x5e, 0x0, 0xc0, 0x4f, 0xb1, 0x72, 0xb);

        /// <summary>VBoxUsb: usblib-win.h</summary>
        public enum SUPUSB_IOCTL : uint
        {
            GET_DEVICE = (PInvoke.FILE_DEVICE_UNKNOWN << 16) | (PInvoke.FILE_WRITE_ACCESS << 14) | (0x603 << 2) | (PInvoke.METHOD_BUFFERED),
            SEND_URB = (PInvoke.FILE_DEVICE_UNKNOWN << 16) | (PInvoke.FILE_WRITE_ACCESS << 14) | (0x607 << 2) | (PInvoke.METHOD_BUFFERED),
            USB_RESET = (PInvoke.FILE_DEVICE_UNKNOWN << 16) | (PInvoke.FILE_WRITE_ACCESS << 14) | (0x608 << 2) | (PInvoke.METHOD_BUFFERED),
            USB_SELECT_INTERFACE = (PInvoke.FILE_DEVICE_UNKNOWN << 16) | (PInvoke.FILE_WRITE_ACCESS << 14) | (0x609 << 2) | (PInvoke.METHOD_BUFFERED),
            USB_SET_CONFIG = (PInvoke.FILE_DEVICE_UNKNOWN << 16) | (PInvoke.FILE_WRITE_ACCESS << 14) | (0x60a << 2) | (PInvoke.METHOD_BUFFERED),
            USB_CLAIM_DEVICE = (PInvoke.FILE_DEVICE_UNKNOWN << 16) | (PInvoke.FILE_WRITE_ACCESS << 14) | (0x60b << 2) | (PInvoke.METHOD_BUFFERED),
            USB_RELEASE_DEVICE = (PInvoke.FILE_DEVICE_UNKNOWN << 16) | (PInvoke.FILE_WRITE_ACCESS << 14) | (0x60c << 2) | (PInvoke.METHOD_BUFFERED),
            IS_OPERATIONAL = (PInvoke.FILE_DEVICE_UNKNOWN << 16) | (PInvoke.FILE_WRITE_ACCESS << 14) | (0x60d << 2) | (PInvoke.METHOD_BUFFERED),
            USB_CLEAR_ENDPOINT = (PInvoke.FILE_DEVICE_UNKNOWN << 16) | (PInvoke.FILE_WRITE_ACCESS << 14) | (0x60e << 2) | (PInvoke.METHOD_BUFFERED),
            GET_VERSION = (PInvoke.FILE_DEVICE_UNKNOWN << 16) | (PInvoke.FILE_WRITE_ACCESS << 14) | (0x60f << 2) | (PInvoke.METHOD_BUFFERED),
            USB_ABORT_ENDPOINT = (PInvoke.FILE_DEVICE_UNKNOWN << 16) | (PInvoke.FILE_WRITE_ACCESS << 14) | (0x610 << 2) | (PInvoke.METHOD_BUFFERED),
        }

        /// <summary>VBoxUsb: usblib-win.h</summary>
        public const uint USBDRV_MAJOR_VERSION = 5;
        /// <summary>VBoxUsb: usblib-win.h</summary>
        public const uint USBDRV_MINOR_VERSION = 0;

        /// <summary>VBoxUsb: usblib-win.h: USBSUP_VERSION</summary>
        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        public struct UsbSupVersion
        {
            public uint major;
            public uint minor;
        }

        /// <summary>VBoxUsb: usblib-win.h: USBSUP_TRANSFER_TYPE</summary>
        public enum UsbSupTransferType : uint
        {
            USBSUP_TRANSFER_TYPE_CTRL = 0,
            USBSUP_TRANSFER_TYPE_ISOC,
            USBSUP_TRANSFER_TYPE_BULK,
            USBSUP_TRANSFER_TYPE_INTR,
            USBSUP_TRANSFER_TYPE_MSG,
        };

        /// <summary>VBoxUsb: usblib-win.h: USBSUP_DIRECTION</summary>
        public enum UsbSupDirection : uint
        {
            USBSUP_DIRECTION_SETUP = 0,
            USBSUP_DIRECTION_IN,
            USBSUP_DIRECTION_OUT,
        }

        /// <summary>VBoxUsb: usblib-win.h: USBSUP_XFER_FLAG</summary>
        [Flags]
        public enum UsbSupXferFlags : uint
        {
            USBSUP_FLAG_NONE = 0,
            USBSUP_FLAG_SHORT_OK = 1 << 0,
        }

        /// <summary>VBoxUsb: usblib-win.h: USBSUP_ERROR</summary>
        public enum UsbSupError : uint
        {
            USBSUP_XFER_OK = 0,
            USBSUP_XFER_STALL,
            USBSUP_XFER_DNR,
            USBSUP_XFER_CRC,
            USBSUP_XFER_NAC,
            USBSUP_XFER_UNDERRUN,
            USBSUP_XFER_OVERRUN,
        }

        /// <summary>VBoxUsb: usblib-win.h: USBSUP_ISOCPKT</summary>
        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        public struct UsbSupIsoPkt
        {
            public ushort cb;     /* [in/out] packet size/size transferred */
            public ushort off;    /* [in] offset of packet in buffer */
            public UsbSupError stat;   /* [out] packet status */
        }

        /// <summary>VBoxUsb: usblib-win.h: USBSUP_URB</summary>
        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        public struct UsbSupUrb
        {
            public UsbSupTransferType type;           /* [in] USBSUP_TRANSFER_TYPE_XXX */
            public uint ep;             /* [in] index to dev->pipe */
            public UsbSupDirection dir;            /* [in] USBSUP_DIRECTION_XXX */
            public UsbSupXferFlags flags;          /* [in] USBSUP_FLAG_XXX */
            public UsbSupError error;          /* [out] USBSUP_XFER_XXX */
            public ulong len;            /* [in/out] may change */
            public IntPtr buf;           /* [in/out] depends on dir */
            public uint numIsoPkts;     /* [in] number of isochronous packets (8 max) */
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
            public UsbSupIsoPkt[] aIsoPkts;    /* [in/out] isochronous packet descriptors */
        }

        /// <summary>VBoxUsb: usblib-win.h: USBSUP_SET_CONFIG</summary>
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct UsbSupSetConfig
        {
            public byte bConfigurationValue;
        }

        /// <summary>VBoxUsb: usblib-win.h: USBSUP_SELECT_INTERFACE</summary>
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct UsbSupSelectInterface
        {
            public byte bInterfaceNumber;
            public byte bAlternateSetting;
        }

        /// <summary>VBoxUsb: usblib-win.h: USBSUP_CLEAR_ENDPOINT</summary>
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct UsbSupClearEndpoint
        {
            public byte bEndpoint;
        }
    }
}
