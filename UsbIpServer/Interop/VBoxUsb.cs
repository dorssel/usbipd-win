// SPDX-FileCopyrightText: 2020 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-2.0-only

using System;
using System.Runtime.InteropServices;
using static UsbIpServer.Interop.WinSDK;

namespace UsbIpServer.Interop
{
    static class VBoxUsb

    {
        /// <summary>VBoxUsb: usbfilter.h</summary>
        public const uint USBFILTER_MAGIC = 0x19670408;

        /// <summary>VBoxUsb: usbfilter.h: USBFILTERIDX</summary>
        public enum UsbFilterType : uint
        {
            INVALID = 0,
            FIRST,
            ONESHOT_IGNORE = FIRST,
            ONESHOT_CAPTURE,
            IGNORE,
            CAPTURE,
            END,
        }

        /// <summary>VBoxUsb: usbfilter.h: USBFILTERMATCH</summary>
        public enum UsbFilterMatch : ushort
        {
            INVALID = 0,
            IGNORE,
            PRESENT,
            NUM_FIRST,
            NUM_EXACT = NUM_FIRST,
            NUM_EXACT_NP,
            NUM_EXPRESSION,
            NUM_EXPRESSION_NP,
            NUM_LAST = NUM_EXPRESSION_NP,
            STR_FIRST,
            STR_EXACT = STR_FIRST,
            STR_EXACT_NP,
            STR_PATTERN,
            STR_PATTERN_NP,
            STR_LAST = STR_PATTERN_NP,
            END
        }

        /// <summary>VBoxUsb: usbfilter.h: USBFILTERIDX</summary>
        public enum UsbFilterIdx : uint
        {
            VENDOR_ID = 0,
            PRODUCT_ID,
            DEVICE_REV,
            DEVICE = DEVICE_REV,
            DEVICE_CLASS,
            DEVICE_SUB_CLASS,
            DEVICE_PROTOCOL,
            BUS,
            PORT,
            MANUFACTURER_STR,
            PRODUCT_STR,
            SERIAL_NUMBER_STR,
            END
        }

        /// <summary>VBoxUsb: usbfilter.h: USBFILTERFIELD</summary>
        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        public struct UsbFilterField
        {
            public UsbFilterMatch enmMatch;
            public ushort u16Value;
        }

        /// <summary>VBoxUsb: usbfilter.h: USBFILTER</summary>
        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        public struct UsbFilter
        {
            uint u32Magic;
            UsbFilterType enmType;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)UsbFilterIdx.END)]
            public UsbFilterField[] aFields;
            readonly uint offCurEnd;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
            byte[] achStrTab;

            public static UsbFilter Create(UsbFilterType type)
            {
                var result = new UsbFilter()
                {
                    u32Magic = USBFILTER_MAGIC,
                    enmType = type,
                    aFields = new UsbFilterField[(int)UsbFilterIdx.END],
                    achStrTab = new byte[256],
                };
                for (var i = 0; i < (int)UsbFilterIdx.END; ++i)
                {
                    result.aFields[i].enmMatch = UsbFilterMatch.IGNORE;
                }
                return result;
            }

            public void SetMatch(UsbFilterIdx index, UsbFilterMatch match, ushort value)
            {
                aFields[(int)index].enmMatch = match;
                aFields[(int)index].u16Value = value;
            }
        }

        /// <summary>VBoxUsb: usblib-win.h: USBSUP_FLTADDOUT</summary>
        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        public struct UsbSupFltAddOut
        {
            public ulong uId;
            public int rc;
        }

        /// <summary>VBoxUsb: usblib-win.h: USBSUP_GETDEV</summary>
        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        public struct UsbSupGetDev
        {
            public IntPtr hDevice;
        };

        /// <summary>VBoxUsb: usb.h: USBDEVICESTATE</summary>
        public enum UsbDeviceState : uint
        {
            INVALID = 0,
            UNSUPPORTED,
            USED_BY_HOST,
            USED_BY_HOST_CAPTURABLE,
            UNUSED,
            HELD_BY_PROXY,
            USED_BY_GUEST,
        }

        /// <summary>VBoxUsb: usblib-win.h: USBSUP_GETDEV_MON</summary>
        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        public struct UsbSupGetDevMon
        {
            public UsbDeviceState enmState;
        }

        /// <summary>VBoxUsb: usblib-win.h: USBSUP_CLAIMDEV</summary>
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct UsbSupClaimDev
        {
            readonly byte bInterfaceNumber;
            [MarshalAs(UnmanagedType.U1)]
            public bool fClaimed;
        }

        /// <summary>VBoxUsb: usblib-win.h</summary>
        public const string USBMON_DEVICE_NAME = @"\\.\VBoxUSBMon";

        /// <summary>VBoxUsb: usblib-win.h</summary>
        public static readonly Guid GUID_CLASS_VBOXUSB = new(0x873fdf, 0xCAFE, 0x80EE, 0xaa, 0x5e, 0x0, 0xc0, 0x4f, 0xb1, 0x72, 0xb);

        public enum IoControl : uint
        {
            /// <summary>VBoxUsb: usblib-win.h</summary>
            SUPUSBFLT_IOCTL_GET_VERSION = (DeviceType.FILE_DEVICE_UNKNOWN << 16) | (AccessCode.FILE_WRITE_ACCESS << 14) | (0x610 << 2) | (MethodCode.METHOD_BUFFERED),
            /// <summary>VBoxUsb: usblib-win.h</summary>
            SUPUSBFLT_IOCTL_ADD_FILTER = (DeviceType.FILE_DEVICE_UNKNOWN << 16) | (AccessCode.FILE_WRITE_ACCESS << 14) | (0x611 << 2) | (MethodCode.METHOD_BUFFERED),
            /// <summary>VBoxUsb: usblib-win.h</summary>
            SUPUSBFLT_IOCTL_REMOVE_FILTER = (DeviceType.FILE_DEVICE_UNKNOWN << 16) | (AccessCode.FILE_WRITE_ACCESS << 14) | (0x612 << 2) | (MethodCode.METHOD_BUFFERED),
            /// <summary>VBoxUsb: usblib-win.h</summary>
            SUPUSBFLT_IOCTL_RUN_FILTERS = (DeviceType.FILE_DEVICE_UNKNOWN << 16) | (AccessCode.FILE_WRITE_ACCESS << 14) | (0x615 << 2) | (MethodCode.METHOD_BUFFERED),
            /// <summary>VBoxUsb: usblib-win.h</summary>
            SUPUSBFLT_IOCTL_GET_DEVICE = (DeviceType.FILE_DEVICE_UNKNOWN << 16) | (AccessCode.FILE_WRITE_ACCESS << 14) | (0x617 << 2) | (MethodCode.METHOD_BUFFERED),

            /// <summary>VBoxUsb: usblib-win.h</summary>
            SUPUSB_IOCTL_GET_DEVICE = (DeviceType.FILE_DEVICE_UNKNOWN << 16) | (AccessCode.FILE_WRITE_ACCESS << 14) | (0x603 << 2) | (MethodCode.METHOD_BUFFERED),
            /// <summary>VBoxUsb: usblib-win.h</summary>
            SUPUSB_IOCTL_SEND_URB = (DeviceType.FILE_DEVICE_UNKNOWN << 16) | (AccessCode.FILE_WRITE_ACCESS << 14) | (0x607 << 2) | (MethodCode.METHOD_BUFFERED),
            /// <summary>VBoxUsb: usblib-win.h</summary>
            SUPUSB_IOCTL_USB_RESET = (DeviceType.FILE_DEVICE_UNKNOWN << 16) | (AccessCode.FILE_WRITE_ACCESS << 14) | (0x608 << 2) | (MethodCode.METHOD_BUFFERED),
            /// <summary>VBoxUsb: usblib-win.h</summary>
            SUPUSB_IOCTL_USB_SELECT_INTERFACE = (DeviceType.FILE_DEVICE_UNKNOWN << 16) | (AccessCode.FILE_WRITE_ACCESS << 14) | (0x609 << 2) | (MethodCode.METHOD_BUFFERED),
            /// <summary>VBoxUsb: usblib-win.h</summary>
            SUPUSB_IOCTL_USB_SET_CONFIG = (DeviceType.FILE_DEVICE_UNKNOWN << 16) | (AccessCode.FILE_WRITE_ACCESS << 14) | (0x60a << 2) | (MethodCode.METHOD_BUFFERED),
            /// <summary>VBoxUsb: usblib-win.h</summary>
            SUPUSB_IOCTL_USB_CLAIM_DEVICE = (DeviceType.FILE_DEVICE_UNKNOWN << 16) | (AccessCode.FILE_WRITE_ACCESS << 14) | (0x60b << 2) | (MethodCode.METHOD_BUFFERED),
            /// <summary>VBoxUsb: usblib-win.h</summary>
            SUPUSB_IOCTL_USB_RELEASE_DEVICE = (DeviceType.FILE_DEVICE_UNKNOWN << 16) | (AccessCode.FILE_WRITE_ACCESS << 14) | (0x60c << 2) | (MethodCode.METHOD_BUFFERED),
            /// <summary>VBoxUsb: usblib-win.h</summary>
            SUPUSB_IOCTL_IS_OPERATIONAL = (DeviceType.FILE_DEVICE_UNKNOWN << 16) | (AccessCode.FILE_WRITE_ACCESS << 14) | (0x60d << 2) | (MethodCode.METHOD_BUFFERED),
            /// <summary>VBoxUsb: usblib-win.h</summary>
            SUPUSB_IOCTL_USB_CLEAR_ENDPOINT = (DeviceType.FILE_DEVICE_UNKNOWN << 16) | (AccessCode.FILE_WRITE_ACCESS << 14) | (0x60e << 2) | (MethodCode.METHOD_BUFFERED),
            /// <summary>VBoxUsb: usblib-win.h</summary>
            SUPUSB_IOCTL_GET_VERSION = (DeviceType.FILE_DEVICE_UNKNOWN << 16) | (AccessCode.FILE_WRITE_ACCESS << 14) | (0x60f << 2) | (MethodCode.METHOD_BUFFERED),
        }

        /// <summary>VBoxUsb: usblib-win.h</summary>
        public const uint USBMON_MAJOR_VERSION = 5;
        /// <summary>VBoxUsb: usblib-win.h</summary>
        public const uint USBMON_MINOR_VERSION = 0;
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
