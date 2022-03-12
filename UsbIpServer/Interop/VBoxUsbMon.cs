// SPDX-FileCopyrightText: 2020 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-2.0-only

using System.Runtime.InteropServices;
using Windows.Win32;

namespace UsbIpServer.Interop
{
    static class VBoxUsbMon

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

        public const string ServiceName = "VBoxUSBMon";

        /// <summary>VBoxUsb: usblib-win.h</summary>
        public const string USBMON_DEVICE_NAME = @"\\.\VBoxUSBMon";

        /// <summary>VBoxUsb: usblib-win.h</summary>
        public enum SUPUSBFLT_IOCTL : uint
        {
            GET_VERSION = (PInvoke.FILE_DEVICE_UNKNOWN << 16) | (PInvoke.FILE_WRITE_ACCESS << 14) | (0x610 << 2) | (PInvoke.METHOD_BUFFERED),
            ADD_FILTER = (PInvoke.FILE_DEVICE_UNKNOWN << 16) | (PInvoke.FILE_WRITE_ACCESS << 14) | (0x611 << 2) | (PInvoke.METHOD_BUFFERED),
            REMOVE_FILTER = (PInvoke.FILE_DEVICE_UNKNOWN << 16) | (PInvoke.FILE_WRITE_ACCESS << 14) | (0x612 << 2) | (PInvoke.METHOD_BUFFERED),
            RUN_FILTERS = (PInvoke.FILE_DEVICE_UNKNOWN << 16) | (PInvoke.FILE_WRITE_ACCESS << 14) | (0x615 << 2) | (PInvoke.METHOD_BUFFERED),
            GET_DEVICE = (PInvoke.FILE_DEVICE_UNKNOWN << 16) | (PInvoke.FILE_WRITE_ACCESS << 14) | (0x617 << 2) | (PInvoke.METHOD_BUFFERED),
        }

        /// <summary>VBoxUsb: usblib-win.h</summary>
        public const uint USBMON_MAJOR_VERSION = 5;
        /// <summary>VBoxUsb: usblib-win.h</summary>
        public const uint USBMON_MINOR_VERSION = 0;

        /// <summary>VBoxUsb: usblib-win.h: USBSUP_VERSION</summary>
        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        public struct UsbSupVersion
        {
            public uint major;
            public uint minor;
        }
    }
}
