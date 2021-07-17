// SPDX-FileCopyrightText: 2020 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-2.0-only

using System.Runtime.InteropServices;
using Windows.Win32.Devices.Usb;

namespace UsbIpServer.Interop
{
    static class UsbIp
    {
        /// <summary>UsbIp: tools/configure.ac</summary>
        public const ushort USBIP_VERSION = 0x0111;

        /// <summary>UsbIp: tools/usbip_common.h</summary>
        public const uint SYSFS_PATH_MAX = 256;

        /// <summary>UsbIp: tools/usbip_common.h</summary>
        public const uint SYSFS_BUS_ID_SIZE = 32;

        /// <summary>UsbIp: tools/usbip_network.c: usbip_port</summary>
        public const ushort USBIP_PORT = 3240;

        public enum OpCode : ushort
        {
            /// <summary>UsbIp: tools/usbip_network.h</summary>
            OP_REQ_DEVLIST = 0x8005,
            /// <summary>UsbIp: tools/usbip_network.h</summary>
            OP_REP_DEVLIST = 0x0005,

            /// <summary>UsbIp: tools/usbip_network.h</summary>
            OP_REQ_IMPORT = 0x8003,
            /// <summary>UsbIp: tools/usbip_network.h</summary>
            OP_REP_IMPORT = 0x0003,
        }

        public enum Status : uint
        {
            /// <summary>UsbIp: tools/usbip_common.h
            /// <para>Request Completed Successfully</para></summary>
            ST_OK = 0,
            /// <summary>UsbIp: tools/usbip_common.h
            /// <para>Request Failed</para></summary>
            ST_NA,
            /// <summary>UsbIp: tools/usbip_common.h
            /// <para>Device busy (exported)</para></summary>
            ST_DEV_BUSY,
            /// <summary>UsbIp: tools/usbip_common.h
            /// <para>Device in error state</para></summary>
            ST_DEV_ERR,
            /// <summary>UsbIp: tools/usbip_common.h
            /// <para>Device not found</para></summary>
            ST_NODEV,
            /// <summary>UsbIp: tools/usbip_common.h
            /// <para>Unexpected response</para></summary>
            ST_ERROR
        }

        public enum UsbIpCmd : uint
        {
            /// <summary>UsbIp: drivers/usbip_common.h</summary>
            USBIP_CMD_SUBMIT = 1,
            /// <summary>UsbIp: drivers/usbip_common.h</summary>
            USBIP_CMD_UNLINK,
            /// <summary>UsbIp: drivers/usbip_common.h</summary>
            USBIP_RET_SUBMIT,
            /// <summary>UsbIp: drivers/usbip_common.h</summary>
            USBIP_RET_UNLINK,
        }

        public enum UsbIpDir : uint
        {
            /// <summary>UsbIp: drivers/usbip_common.h</summary>
            USBIP_DIR_OUT = 0,
            /// <summary>UsbIp: drivers/usbip_common.h</summary>
            USBIP_DIR_IN,
        }

        /// <summary>UsbIp: drivers/usbip_common.h: usbip_header_basic</summary>
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct UsbIpHeaderBasic
        {
            public UsbIpCmd command;
            public uint seqnum;
            public uint devid;
            public UsbIpDir direction;
            public uint ep;
        }

        /// <summary>UsbIp: drivers/usbip_common.h: usbip_header_cmd_submit</summary>
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct UsbIpHeaderCmdSubmit
        {
            public uint transfer_flags;
            public uint transfer_buffer_length;
            public int start_frame;
            public int number_of_packets;
            public int interval;

            public USB_DEFAULT_PIPE_SETUP_PACKET setup;
        }

        /// <summary>UsbIp: drivers/usbip_common.h: usbip_header_ret_submit</summary>
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct UsbIpHeaderRetSubmit
        {
            public int status;
            public int actual_length;
            public int start_frame;
            public int number_of_packets;
            public int error_count;
        }

        /// <summary>UsbIp: drivers/usbip_common.h: usbip_header_cmd_unlink</summary>
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct UsbIpHeaderCmdUnlink
        {
            public uint seqnum;
        }

        /// <summary>UsbIp: drivers/usbip_common.h: usbip_header_ret_unlink</summary>
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct UsbIpHeaderRetUnlink
        {
            public int status;
        }
    }
}
