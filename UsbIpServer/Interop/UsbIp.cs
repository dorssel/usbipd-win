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

using System.Runtime.InteropServices;
using static UsbIpServer.Interop.Usb;

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

            public UsbDefaultPipeSetupPacket setup;
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
