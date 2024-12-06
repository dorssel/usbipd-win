// SPDX-FileCopyrightText: 2020 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Windows.Win32.Devices.Usb;

namespace Usbipd.Interop;

static class UsbIp
{
    /// <summary>
    /// UsbIp: tools/configure.ac
    /// <para/>
    /// See <see href="https://www.kernel.org/doc/html/latest/usb/usbip_protocol.html">USB/IP protocol</see>
    /// and <see cref="Tools.UsbIpVersionToVersion(ushort)"/>.
    /// </summary>
    public const ushort USBIP_VERSION = 0x0111;

    /// <summary>UsbIp: tools/usbip_common.h</summary>
    public const uint SYSFS_PATH_MAX = 256;

    /// <summary>UsbIp: tools/usbip_common.h</summary>
    public const uint SYSFS_BUS_ID_SIZE = 32;

    /// <summary>UsbIp: tools/usbip_network.c: usbip_port</summary>
    public const ushort USBIP_PORT = 3240;

    internal enum OpCode : ushort
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

    internal enum Status : uint
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

    internal enum UsbIpCmd : uint
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

    internal enum UsbIpDir : uint
    {
        /// <summary>UsbIp: drivers/usbip_common.h</summary>
        USBIP_DIR_OUT = 0,
        /// <summary>UsbIp: drivers/usbip_common.h</summary>
        USBIP_DIR_IN,
    }

    /// <summary>UsbIp: drivers/usbip_common.h: usbip_header_basic</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct UsbIpHeaderBasic
    {
        public UsbIpCmd command;
        public uint seqnum;
        public uint devid;
        public UsbIpDir direction;
        public uint ep;
    }

    /// <summary>UsbIp: drivers/usbip_common.h: usbip_header_cmd_submit</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct UsbIpHeaderCmdSubmit
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
    internal struct UsbIpHeaderRetSubmit
    {
        public int status;
        public int actual_length;
        public int start_frame;
        public int number_of_packets;
        public int error_count;
    }

    /// <summary>UsbIp: drivers/usbip_common.h: usbip_header_cmd_unlink</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct UsbIpHeaderCmdUnlink
    {
        public uint seqnum;
    }

    /// <summary>UsbIp: drivers/usbip_common.h: usbip_header_ret_unlink</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct UsbIpHeaderRetUnlink
    {
        public int status;
    }

    /// <summary>UsbIp: drivers/usbip_common.h: usbip_header</summary>
    [StructLayout(LayoutKind.Explicit, Pack = 1)]
    internal struct UsbIpHeader
    {
        [FieldOffset(0)]
        public UsbIpHeaderBasic basic; // renamed, 'base' is a keyword in C#

        [FieldOffset(20)]
        public UsbIpHeaderCmdSubmit cmd_submit;
        [FieldOffset(20)]
        public UsbIpHeaderRetSubmit ret_submit;
        [FieldOffset(20)]
        public UsbIpHeaderCmdUnlink cmd_unlink;
        [FieldOffset(20)]
        public UsbIpHeaderRetUnlink ret_unlink;
    }

    /// <summary>UsbIp: drivers/usbip_common.h: usbip_iso_packet_descriptor</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct UsbIpIsoPacketDescriptor
    {
        public uint offset;
        public uint length;
        public uint actual_length;
        public uint status;
    }

    static void ReverseEndianness(this Span<int> values)
    {
        foreach (ref var i in values)
        {
            i = BinaryPrimitives.ReverseEndianness(i);
        }
    }

    static void ReverseEndianness(this ref UsbIpHeader header)
    {
        // The first 40 bytes of UsbIpHeader are always 10 4-byte integers.
        // nosemgrep: csharp.lang.security.memory.memory-marshal-create-span.memory-marshal-create-span
        MemoryMarshal.Cast<UsbIpHeader, int>(MemoryMarshal.CreateSpan(ref header, 1))[0..10].ReverseEndianness();
    }

    /// <summary>
    /// Read a native header from a big endian stream.
    /// </summary>
    internal static async Task<UsbIpHeader> ReadUsbIpHeaderAsync(this Stream stream, CancellationToken cancellationToken)
    {
        var bytes = new byte[Unsafe.SizeOf<UsbIpHeader>()];
        await stream.ReadMessageAsync(bytes, cancellationToken);
        MemoryMarshal.AsRef<UsbIpHeader>(bytes).ReverseEndianness();
        return MemoryMarshal.AsRef<UsbIpHeader>(bytes);
    }

    /// <summary>
    /// Read native descriptors from a big endian stream.
    /// </summary>
    internal static async Task<UsbIpIsoPacketDescriptor[]> ReadUsbIpIsoPacketDescriptorsAsync(this Stream stream, int count,
        CancellationToken cancellationToken)
    {
        var bytes = new byte[count * Unsafe.SizeOf<UsbIpIsoPacketDescriptor>()];
        await stream.ReadMessageAsync(bytes, cancellationToken);
        MemoryMarshal.Cast<byte, int>(bytes).ReverseEndianness();
        return MemoryMarshal.Cast<byte, UsbIpIsoPacketDescriptor>(bytes).ToArray();
    }

    /// <summary>
    /// Marshal the native header to a big endian byte array.
    /// </summary>
    internal static byte[] ToBytes(this in UsbIpHeader header)
    {
        var bytes = new byte[Unsafe.SizeOf<UsbIpHeader>()];
        MemoryMarshal.Write(bytes, header);
        MemoryMarshal.AsRef<UsbIpHeader>(bytes).ReverseEndianness();
        return bytes;
    }

    /// <summary>
    /// Marshal the native descriptors to a big endian byte array.
    /// </summary>
    internal static byte[] ToBytes(this UsbIpIsoPacketDescriptor[] descriptors)
    {
        var bytes = MemoryMarshal.Cast<UsbIpIsoPacketDescriptor, byte>(descriptors).ToArray();
        MemoryMarshal.Cast<byte, int>(bytes).ReverseEndianness();
        return bytes;
    }
}
