// SPDX-FileCopyrightText: 2021 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;
using Usbipd.Automation;
using static Usbipd.Interop.UsbIp;
using static Usbipd.Interop.VBoxUsb;

namespace Usbipd;

sealed partial class PcapNg
    : IDisposable
{
    public PcapNg(IConfiguration config, ILogger<PcapNg> logger)
    {
        Logger = logger;

        var path = config["usbipd:PcapNg:Path"];
        Logger.Debug($"usbipd:PcapNg:Path = '{path}'");
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        var snapLength = config["usbipd:PcapNg:SnapLength"];
        if (uint.TryParse(snapLength, out SnapLength))
        {
            // The absolute minimum, just the URB without any data.
            if (SnapLength < 64)
            {
                SnapLength = 64;
            }
            else if (SnapLength > int.MaxValue)
            {
                SnapLength = int.MaxValue;
            }
        }
        Logger.Debug($"usbipd:PcapNg:SnapLength = {(SnapLength == 0 ? "unlimited" : SnapLength)}");

        try
        {
            Stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
            Enabled = true;
            PacketWriterTask = Task.Run(() => PacketWriterAsync(Cancellation.Token));
            TimestampBase = (ulong)(DateTime.UtcNow.Ticks - DateTime.UnixEpoch.Ticks);
            Stopwatch.Start();
        }
        catch (IOException ex)
        {
            logger.InternalError("Unable to start capture.", ex);
        }
    }

    internal static byte ConvertType(UsbSupTransferType type)
    {
        return type switch
        {
            UsbSupTransferType.USBSUP_TRANSFER_TYPE_ISOC => 0,
            UsbSupTransferType.USBSUP_TRANSFER_TYPE_INTR => 1,
            UsbSupTransferType.USBSUP_TRANSFER_TYPE_MSG => 2,
            UsbSupTransferType.USBSUP_TRANSFER_TYPE_BULK => 3,
            UsbSupTransferType.USBSUP_TRANSFER_TYPE_CTRL or _ => throw new ArgumentOutOfRangeException(nameof(type)),
        };
    }

    public void DumpPacketNonIsoRequest(UsbIpHeaderBasic basic, UsbIpHeaderCmdSubmit cmdSubmit, ReadOnlySpan<byte> data)
    {
        if (!Enabled)
        {
            return;
        }

        var timestamp = GetTimestamp() / 10;  // in micro seconds

        using var usbMon = new BinaryWriter(new MemoryStream());
        usbMon.Write((ulong)basic.seqnum);
        usbMon.Write((byte)'S');
        usbMon.Write(ConvertType(basic.EndpointType(cmdSubmit)));
        usbMon.Write(basic.RawEndpoint());
        usbMon.Write(unchecked((byte)basic.devid));
        usbMon.Write((ushort)(basic.devid >> 16));
        usbMon.Write((byte)(basic.ep == 0 ? '\0' : '-'));
        usbMon.Write((byte)(data.IsEmpty ? basic.direction == UsbIpDir.USBIP_DIR_IN ? '<' : '>' : '\0'));
        usbMon.Write(timestamp / 1000000); // seconds
        usbMon.Write((uint)(timestamp % 1000000)); // micro seconds
        usbMon.Write(-115); // -EINPROGRESS
        usbMon.Write(cmdSubmit.transfer_buffer_length); // length
        usbMon.Write(0); // actual
        if (basic.ep == 0)
        {
            usbMon.Write(cmdSubmit.setup.bmRequestType.B);
            usbMon.Write(cmdSubmit.setup.bRequest);
            usbMon.Write(cmdSubmit.setup.wValue.W);
            usbMon.Write(cmdSubmit.setup.wIndex.W);
            usbMon.Write(cmdSubmit.setup.wLength);
        }
        else
        {
            usbMon.Write(0ul); // setup == 0 for ep != 0
        }
        usbMon.Write(cmdSubmit.interval);
        usbMon.Write(0u); // start_frame == 0 for non-ISO
        usbMon.Write(cmdSubmit.transfer_flags);
        usbMon.Write(0u); // number_of_packets == 0 for non-ISO
        usbMon.Write(data);

        BlockChannel.Writer.WriteAsync(CreateEnhancedPacketBlock(0, usbMon)).AsTask().Wait();
    }

    public void DumpPacketNonIsoReply(UsbIpHeaderBasic basic, UsbIpHeaderCmdSubmit cmdSubmit, UsbIpHeaderRetSubmit retSubmit, ReadOnlySpan<byte> data)
    {
        if (!Enabled)
        {
            return;
        }

        var timestamp = GetTimestamp() / 10;  // in micro seconds

        using var usbMon = new BinaryWriter(new MemoryStream());
        usbMon.Write((ulong)basic.seqnum);
        usbMon.Write((byte)'C');
        usbMon.Write(ConvertType(basic.EndpointType(cmdSubmit)));
        usbMon.Write(basic.RawEndpoint());
        usbMon.Write(unchecked((byte)basic.devid));
        usbMon.Write((ushort)(basic.devid >> 16));
        usbMon.Write((byte)'-');
        usbMon.Write((byte)(data.IsEmpty ? basic.direction == UsbIpDir.USBIP_DIR_IN ? '<' : '>' : '\0'));
        usbMon.Write(timestamp / 1000000); // seconds
        usbMon.Write((uint)(timestamp % 1000000)); // micro seconds
        usbMon.Write(retSubmit.status);
        usbMon.Write(retSubmit.actual_length); // length
        usbMon.Write(retSubmit.actual_length); // actual
        usbMon.Write(0ul); // setup == 0 for reply
        usbMon.Write(cmdSubmit.interval);
        usbMon.Write(0u); // start_frame == 0 for non-ISO
        usbMon.Write(cmdSubmit.transfer_flags);
        usbMon.Write(0u); // number_of_packets == 0 for non-ISO
        usbMon.Write(data);

        BlockChannel.Writer.WriteAsync(CreateEnhancedPacketBlock(0, usbMon)).AsTask().Wait();
    }

    public void DumpPacketIsoRequest(UsbIpHeaderBasic basic, UsbIpHeaderCmdSubmit cmdSubmit, UsbIpIsoPacketDescriptor[] packetDescriptors,
        ReadOnlySpan<byte> data)
    {
        if (!Enabled)
        {
            return;
        }

        var timestamp = GetTimestamp() / 10;  // in micro seconds

        using var usbMon = new BinaryWriter(new MemoryStream());
        usbMon.Write((ulong)basic.seqnum);
        usbMon.Write((byte)'S');
        usbMon.Write((byte)0); // ISO
        usbMon.Write(basic.RawEndpoint());
        usbMon.Write(unchecked((byte)basic.devid));
        usbMon.Write((ushort)(basic.devid >> 16));
        usbMon.Write((byte)'-');
        usbMon.Write((byte)(data.IsEmpty ? basic.direction == UsbIpDir.USBIP_DIR_IN ? '<' : '>' : '\0'));
        usbMon.Write(timestamp / 1000000); // seconds
        usbMon.Write((uint)(timestamp % 1000000)); // micro seconds
        usbMon.Write(-115); // -EINPROGRESS
        usbMon.Write(cmdSubmit.transfer_buffer_length); // length
        usbMon.Write(data.Length + (packetDescriptors.Length * 16)); // actual
        usbMon.Write((uint)0); // ISO error count
        usbMon.Write((uint)packetDescriptors.Length);
        usbMon.Write(cmdSubmit.interval);
        usbMon.Write(cmdSubmit.start_frame);
        usbMon.Write(cmdSubmit.transfer_flags);
        usbMon.Write(cmdSubmit.number_of_packets);
        usbMon.Write(data);
        foreach (var packetDescriptor in packetDescriptors)
        {
            usbMon.Write(packetDescriptor.status);
            usbMon.Write(packetDescriptor.offset);
            usbMon.Write(packetDescriptor.length);
            usbMon.Write((uint)0); // padding
        }

        BlockChannel.Writer.WriteAsync(CreateEnhancedPacketBlock(0, usbMon)).AsTask().Wait();
    }

    public void DumpPacketIsoReply(UsbIpHeaderBasic basic, UsbIpHeaderCmdSubmit cmdSubmit, UsbIpHeaderRetSubmit retSubmit,
        UsbIpIsoPacketDescriptor[] packetDescriptors, ReadOnlySpan<byte> data)
    {
        if (!Enabled)
        {
            return;
        }

        var timestamp = GetTimestamp() / 10;  // in micro seconds

        using var usbMon = new BinaryWriter(new MemoryStream());
        usbMon.Write((ulong)basic.seqnum);
        usbMon.Write((byte)'C');
        usbMon.Write((byte)0); // ISO
        usbMon.Write(basic.RawEndpoint());
        usbMon.Write(unchecked((byte)basic.devid));
        usbMon.Write((ushort)(basic.devid >> 16));
        usbMon.Write((byte)'-');
        usbMon.Write((byte)(data.IsEmpty ? basic.direction == UsbIpDir.USBIP_DIR_IN ? '<' : '>' : '\0'));
        usbMon.Write(timestamp / 1000000); // seconds
        usbMon.Write((uint)(timestamp % 1000000)); // micro seconds
        usbMon.Write(retSubmit.status);
        usbMon.Write(retSubmit.actual_length); // length
        usbMon.Write(data.Length + (packetDescriptors.Length * 16)); // actual
        usbMon.Write((uint)retSubmit.error_count); // ISO error count
        usbMon.Write((uint)packetDescriptors.Length);
        usbMon.Write(cmdSubmit.interval);
        usbMon.Write(cmdSubmit.start_frame);
        usbMon.Write(cmdSubmit.transfer_flags);
        usbMon.Write(cmdSubmit.number_of_packets);
        var actualOffset = 0u;
        foreach (var packetDescriptor in packetDescriptors)
        {
            // NOTE: UsbMon on Linux gets this wrong. On input, the actual_offset needs to be calculated.
            usbMon.Write(packetDescriptor.status);
            usbMon.Write(basic.direction == UsbIpDir.USBIP_DIR_IN ? actualOffset : packetDescriptor.offset);
            usbMon.Write(packetDescriptor.actual_length);
            usbMon.Write((uint)0); // padding
            actualOffset += packetDescriptor.actual_length;
        }
        usbMon.Write(data);

        BlockChannel.Writer.WriteAsync(CreateEnhancedPacketBlock(0, usbMon)).AsTask().Wait();
    }

    static void UpdateChecksum(ref ushort checksum, byte[] data, int offset)
    {
        var v = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset, 2));
        unchecked
        {
            if ((uint)checksum + v > ushort.MaxValue)
            {
                ++checksum;
            }
            checksum += v;
        }
    }

    static readonly IPEndPoint hostFakeIpv4 = new(IPAddress.Parse("10.0.0.0"), USBIP_PORT);
    static ushort deviceFakePort = 0x8000;
    static readonly byte[] pseudoHeader = [0, 6, 0, 20 + 48];


    public void DumpPacketUnlink(BusId busId, bool reply, UsbIpHeader header)
    {
        if (!Enabled)
        {
            return;
        }

        var deviceFakeIpv4 = new IPEndPoint(IPAddress.Parse($"10.0.{unchecked((byte)busId.Bus)}.{unchecked((byte)busId.Port)}"), deviceFakePort);
        ++deviceFakePort;
        if (deviceFakePort < 0x8000)
        {
            deviceFakePort = 0x8000;
        }

        var source = reply ? hostFakeIpv4 : deviceFakeIpv4;
        var destination = reply ? deviceFakeIpv4 : hostFakeIpv4;

        using var ipv4 = new BinaryWriter(new MemoryStream());
        // IPv4 header
        ipv4.Write((byte)0x45); // Version 4, header_size = 5 * sizeof(uint)
        ipv4.Write((byte)0); // DSCP/ECN (unused)
        ipv4.Write(IPAddress.HostToNetworkOrder(unchecked((short)(20 + 20 + Unsafe.SizeOf<UsbIpHeader>())))); // IPv4 header + TCP header + UsbIpHeader
        ipv4.Write((ushort)0); // Id (unused)
        ipv4.Write((ushort)0); // Flags/Offset (unused)
        ipv4.Write((byte)255); // TTL
        ipv4.Write((byte)6); // protocol = TCP
        ipv4.Write((ushort)0); // checksum (filled in later)
        ipv4.Write(source.Address.GetAddressBytes()); // source IP address
        ipv4.Write(destination.Address.GetAddressBytes()); // destination IP address

        // TCP header
        ipv4.Write(IPAddress.HostToNetworkOrder(unchecked((short)source.Port))); // source port
        ipv4.Write(IPAddress.HostToNetworkOrder(unchecked((short)destination.Port))); // destination port
        ipv4.Write(0); // sequence number (unused)
        ipv4.Write(0); // ACK number (unused)
        ipv4.Write((byte)0x50); // data offset (in uint_32), 0 (reserved)
        ipv4.Write((byte)0x1b); // flags: SYN + ACK + PSH + FIN, just to indicate there is no connection tracking at all
        ipv4.Write((ushort)0xffff); // windows size
        ipv4.Write((ushort)0); // checksum (filled in later)
        ipv4.Write((ushort)0); // URG pointer (unused)

        ipv4.Write(header.ToBytes());

        ipv4.Flush();
        var bytes = ((MemoryStream)ipv4.BaseStream).ToArray();

        // IPv4 checksum
        {
            ushort checksum = 0;
            for (var offset = 0; offset < 20; offset += 2)
            {
                UpdateChecksum(ref checksum, bytes, offset);
            }
            BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(10, 2), unchecked((ushort)~checksum));
        }

        // TCP checksum
        {
            ushort checksum = 0;
            // IPv4 pseudo-header
            for (var offset = 12; offset < 20; offset += 2)
            {
                // src/dst address
                UpdateChecksum(ref checksum, bytes, offset);
            }
            for (var offset = 0; offset < pseudoHeader.Length; offset += 2)
            {
                // zeros / protocol / TCP Length
                UpdateChecksum(ref checksum, pseudoHeader, offset);
            }
            // TCP header + payload
            for (var offset = 20; offset < bytes.Length; offset += 2)
            {
                // zeros / protocol / TCP Length
                UpdateChecksum(ref checksum, bytes, offset);
            }
            BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(36, 2), unchecked((ushort)~checksum));
        }

        BlockChannel.Writer.WriteAsync(CreateEnhancedPacketBlock(1, bytes)).AsTask().Wait();
    }

    async Task PacketWriterAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Stream.WriteAsync(CreateSectionHeaderBlock(), CancellationToken.None);
            await Stream.WriteAsync(CreateInterfaceDescriptionBlockUsb(), CancellationToken.None);
            await Stream.WriteAsync(CreateInterfaceDescriptionBlockUnlink(), CancellationToken.None);
            var needFlush = true;
            while (true)
            {
                using var flushTimer = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                if (needFlush)
                {
                    flushTimer.CancelAfter(TimeSpan.FromSeconds(5));
                }
                try
                {
                    _ = await BlockChannel.Reader.WaitToReadAsync(flushTimer.Token);
                    while (BlockChannel.Reader.TryRead(out var block))
                    {
                        await Stream.WriteAsync(block, CancellationToken.None);
                        ++TotalPacketsWritten;
                        needFlush = true;
                    }
                }
                catch (OperationCanceledException)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    // The timer tripped
                    Logger.Debug("Flushing");
                    await Stream.FlushAsync(CancellationToken.None);
                    needFlush = false;
                }
            }
        }
        catch (IOException ex)
        {
            Logger.InternalError("Write failure during capture.", ex);
        }
        catch (OperationCanceledException)
        {
            try
            {
                await Stream.WriteAsync(CreateInterfaceStatisticsBlock(), CancellationToken.None);
                await Stream.FlushAsync(CancellationToken.None);
            }
            catch (IOException ex)
            {
                Logger.InternalError("Failure flushing capture.", ex);
            }
        }
        finally
        {
            Enabled = false;
            await Stream.DisposeAsync();
        }
    }

    ulong GetTimestamp()
    {
        // Units are 100 ns.
        return TimestampBase + (ulong)Stopwatch.Elapsed.Ticks;
    }

    static byte[] TimestampToBytes(ulong value)
    {
        // timestamps are written high 32-bits first, irrespective of endianness
        return [.. BitConverter.GetBytes((uint)(value >> 32)), .. BitConverter.GetBytes(unchecked((uint)value))];
    }

    static void AddOption(BinaryWriter block, ushort code, byte[] data)
    {
        Pad(block);
        block.Write(code);
        block.Write((ushort)data.Length);
        block.Write(data);
    }

    static void AddOption(BinaryWriter block, ushort code, byte value)
    {
        AddOption(block, code, [value]);
    }

    static void AddOption(BinaryWriter block, ushort code, ulong value)
    {
        AddOption(block, code, BitConverter.GetBytes(value));
    }

    static void AddOption(BinaryWriter block, ushort code, string value)
    {
        AddOption(block, code, Encoding.UTF8.GetBytes(value));
    }

    static byte[] CreateSectionHeaderBlock()
    {
        using var block = CreateBlock(0x0a0d0d0a);
        block.Write(0x1a2b3c4d); // endianness magic
        block.Write((ushort)1); // major PcapNg version
        block.Write((ushort)0); // minor PcapNg version
        block.Write(0xffffffffffffffff); // unspecified section size
        AddOption(block, 3, $"{Environment.OSVersion.VersionString}"); // shb_os
        AddOption(block, 4, $"{Program.Product} {GitVersionInformation.InformationalVersion}"); // shb_userappl
        return FinishBlock(block);
    }

    byte[] CreateInterfaceDescriptionBlockUsb()
    {
        using var block = CreateBlock(1);
        block.Write((ushort)220); // LINKTYPE_USB_LINUX_MMAPPED
        block.Write((ushort)0); // reserved
        block.Write(SnapLength); // snaplen (0 == unlimited)
        AddOption(block, 2, "USB"); // if_name
        AddOption(block, 9, 7); // if_tsresol, 10^-7 s == 100 ns
        return FinishBlock(block);
    }

    byte[] CreateInterfaceDescriptionBlockUnlink()
    {
        using var block = CreateBlock(1);
        block.Write((ushort)101); // LINKTYPE_RAW
        block.Write((ushort)0); // reserved
        block.Write(SnapLength); // snaplen (0 == unlimited)
        AddOption(block, 2, "UNLINK"); // if_name
        AddOption(block, 9, 7); // if_tsresol, 10^-7 s == 100 ns
        return FinishBlock(block);
    }

    byte[] CreateEnhancedPacketBlock(uint interfaceId, BinaryWriter usbMon)
    {
        usbMon.Flush();
        var data = ((MemoryStream)usbMon.BaseStream).ToArray();
        return CreateEnhancedPacketBlock(interfaceId, data);
    }

    byte[] CreateEnhancedPacketBlock(uint interfaceId, byte[] data)
    {
        var timestamp = GetTimestamp();

        var captureLength = ((SnapLength == 0) || (data.Length < SnapLength)) ? data.Length : (int)SnapLength;

        using var block = CreateBlock(6);
        block.Write(interfaceId);
        // timestamps are written high 32-bits first, irrespective of endianness
        block.Write(TimestampToBytes(timestamp));
        block.Write(captureLength); // captured packet length
        block.Write(data.Length); // original packet length
        block.Write(data[0..captureLength]);
        return FinishBlock(block);
    }

    byte[] CreateInterfaceStatisticsBlock()
    {
        var timestamp = GetTimestamp();

        using var block = CreateBlock(5);
        block.Write(0); // interface ID
        // timestamps are written high 32-bits first, irrespective of endianness
        block.Write(TimestampToBytes(timestamp));
        AddOption(block, 2, TimestampToBytes(TimestampBase)); // isb_starttime
        AddOption(block, 3, TimestampToBytes(timestamp)); // isb_endtime
        AddOption(block, 4, TotalPacketsWritten); // isb_ifrecv
        AddOption(block, 5, 0ul); // isb_ifdrop
        AddOption(block, 6, TotalPacketsWritten); // isb_filteraccept
        AddOption(block, 7, 0ul); // isb_osdrop
        AddOption(block, 8, TotalPacketsWritten); // isb_usrdeliv
        return FinishBlock(block);
    }

    static void Pad(BinaryWriter block)
    {
        var padding = (4 - block.Seek(0, SeekOrigin.Current)) & 3;
        if (padding != 0)
        {
            block.Write(new byte[padding]);
        }
    }

    static BinaryWriter CreateBlock(uint blockType)
    {
        var block = new BinaryWriter(new MemoryStream());
        block.Write(blockType);
        block.Write(0); // length to be replaced later
        return block;
    }

    static byte[] FinishBlock(BinaryWriter block)
    {
        Pad(block);
        block.Write(0); // opt_endofopt
        var length = (uint)block.Seek(0, SeekOrigin.Current) + 4;
        block.Write(length); // block total length
        _ = block.Seek(4, SeekOrigin.Begin);
        block.Write(length); // block total length
        block.Flush();
        var memoryStream = (MemoryStream)block.BaseStream;
        var result = memoryStream.ToArray();
        block.Close();
        return result;
    }

    bool Enabled;
    ulong TotalPacketsWritten;
    readonly ulong TimestampBase;
    readonly Stopwatch Stopwatch = new();
    readonly ILogger Logger;
    readonly Stream Stream = Stream.Null;
    readonly uint SnapLength;
    readonly Channel<byte[]> BlockChannel = Channel.CreateUnbounded<byte[]>();
    readonly CancellationTokenSource Cancellation = new();
    readonly Task PacketWriterTask = Task.CompletedTask;

    #region IDisposable

    bool IsDisposed;
    public void Dispose()
    {
        if (!IsDisposed)
        {
            Cancellation.Cancel();
            PacketWriterTask.Wait();
            Cancellation.Dispose();
            Stream.Dispose();
            IsDisposed = true;
        }
    }

    #endregion
}
