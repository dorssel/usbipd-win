// SPDX-FileCopyrightText: 2021 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-2.0-only

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using static UsbIpServer.Interop.UsbIp;
using static UsbIpServer.Interop.VBoxUsb;

namespace UsbIpServer
{
    sealed class PcapNg
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

        static byte ConvertType(UsbSupTransferType type)
        {
            return type switch
            {
                UsbSupTransferType.USBSUP_TRANSFER_TYPE_ISOC => 0,
                UsbSupTransferType.USBSUP_TRANSFER_TYPE_INTR => 1,
                UsbSupTransferType.USBSUP_TRANSFER_TYPE_MSG => 2,
                UsbSupTransferType.USBSUP_TRANSFER_TYPE_BULK => 3,
                _ => throw new ArgumentOutOfRangeException(nameof(type)),
            };
        }

        public void DumpPacketNonIsoRequest(UsbIpHeaderBasic basic, UsbIpHeaderCmdSubmit cmdSubmit, ReadOnlySpan<byte> data)
        {
            if (!Enabled)
            {
                return;
            }

            var timestamp = GetTimestamp() / 10;  // in micro seconds

            using var usbmon = new BinaryWriter(new MemoryStream());
            usbmon.Write((ulong)basic.seqnum);
            usbmon.Write((byte)'S');
            usbmon.Write(ConvertType(basic.EndpointType(cmdSubmit)));
            usbmon.Write(basic.RawEndpoint());
            usbmon.Write((byte)basic.devid);
            usbmon.Write((ushort)(basic.devid >> 16));
            usbmon.Write((byte)(basic.ep == 0 ? '\0' : '-'));
            usbmon.Write((byte)(data.IsEmpty ? basic.direction == UsbIpDir.USBIP_DIR_IN ? '<' : '>' : '\0'));
            usbmon.Write(timestamp / 1000000); // seconds
            usbmon.Write((uint)(timestamp % 1000000)); // micro seconds
            usbmon.Write(-115); // -EINPROGRESS
            usbmon.Write(cmdSubmit.transfer_buffer_length); // length
            usbmon.Write(0); // actual
            if (basic.ep == 0)
            {
                usbmon.Write(cmdSubmit.setup.bmRequestType.B);
                usbmon.Write(cmdSubmit.setup.bRequest);
                usbmon.Write(cmdSubmit.setup.wValue.W);
                usbmon.Write(cmdSubmit.setup.wIndex.W);
                usbmon.Write(cmdSubmit.setup.wLength);
            }
            else
            {
                usbmon.Write(0ul); // setup == 0 for ep != 0
            }
            usbmon.Write(cmdSubmit.interval);
            usbmon.Write(0u); // start_frame == 0 for non-ISO
            usbmon.Write(cmdSubmit.transfer_flags);
            usbmon.Write(0u); // number_of_packets == 0 for non-ISO
            usbmon.Write(data);

            BlockChannel.Writer.WriteAsync(CreateEnhancedPacketBlock(usbmon)).AsTask().Wait();
        }

        public void DumpPacketNonIsoReply(UsbIpHeaderBasic basic, UsbIpHeaderCmdSubmit cmdSubmit, UsbIpHeaderRetSubmit retSubmit, ReadOnlySpan<byte> data)
        {
            if (!Enabled)
            {
                return;
            }

            var timestamp = GetTimestamp() / 10;  // in micro seconds

            using var usbmon = new BinaryWriter(new MemoryStream());
            usbmon.Write((ulong)basic.seqnum);
            usbmon.Write((byte)'C');
            usbmon.Write(ConvertType(basic.EndpointType(cmdSubmit)));
            usbmon.Write(basic.RawEndpoint());
            usbmon.Write((byte)basic.devid);
            usbmon.Write((ushort)(basic.devid >> 16));
            usbmon.Write((byte)'-');
            usbmon.Write((byte)(data.IsEmpty ? basic.direction == UsbIpDir.USBIP_DIR_IN ? '<' : '>' : '\0'));
            usbmon.Write(timestamp / 1000000); // seconds
            usbmon.Write((uint)(timestamp % 1000000)); // micro seconds
            usbmon.Write(retSubmit.status);
            usbmon.Write(retSubmit.actual_length); // length
            usbmon.Write(retSubmit.actual_length); // actual
            usbmon.Write(0ul); // setup == 0 for reply
            usbmon.Write(cmdSubmit.interval);
            usbmon.Write(0u); // start_frame == 0 for non-ISO
            usbmon.Write(cmdSubmit.transfer_flags);
            usbmon.Write(0u); // number_of_packets == 0 for non-ISO
            usbmon.Write(data);

            BlockChannel.Writer.WriteAsync(CreateEnhancedPacketBlock(usbmon)).AsTask().Wait();
        }

        public void DumpPacketIsoRequest(UsbIpHeaderBasic basic, UsbIpHeaderCmdSubmit cmdSubmit, UsbIpIsoPacketDescriptor[] packetDescriptors, ReadOnlySpan<byte> data)
        {
            if (!Enabled)
            {
                return;
            }

            var timestamp = GetTimestamp() / 10;  // in micro seconds

            using var usbmon = new BinaryWriter(new MemoryStream());
            usbmon.Write((ulong)basic.seqnum);
            usbmon.Write((byte)'S');
            usbmon.Write((byte)0); // ISO
            usbmon.Write(basic.RawEndpoint());
            usbmon.Write((byte)basic.devid);
            usbmon.Write((ushort)(basic.devid >> 16));
            usbmon.Write((byte)'-');
            usbmon.Write((byte)(data.IsEmpty ? basic.direction == UsbIpDir.USBIP_DIR_IN ? '<' : '>' : '\0'));
            usbmon.Write(timestamp / 1000000); // seconds
            usbmon.Write((uint)(timestamp % 1000000)); // micro seconds
            usbmon.Write(-115); // -EINPROGRESS
            usbmon.Write(cmdSubmit.transfer_buffer_length); // length
            usbmon.Write(data.Length + packetDescriptors.Length * 16); // actual
            usbmon.Write((uint)0); // ISO error count
            usbmon.Write((uint)packetDescriptors.Length);
            usbmon.Write(cmdSubmit.interval);
            usbmon.Write(cmdSubmit.start_frame);
            usbmon.Write(cmdSubmit.transfer_flags);
            usbmon.Write(cmdSubmit.number_of_packets);
            usbmon.Write(data);
            foreach (var packetDescriptor in packetDescriptors)
            {
                usbmon.Write(packetDescriptor.status);
                usbmon.Write(packetDescriptor.offset);
                usbmon.Write(packetDescriptor.length);
                usbmon.Write((uint)0); // padding
            }

            BlockChannel.Writer.WriteAsync(CreateEnhancedPacketBlock(usbmon)).AsTask().Wait();
        }

        public void DumpPacketIsoReply(UsbIpHeaderBasic basic, UsbIpHeaderCmdSubmit cmdSubmit, UsbIpHeaderRetSubmit retSubmit, UsbIpIsoPacketDescriptor[] packetDescriptors, ReadOnlySpan<byte> data)
        {
            if (!Enabled)
            {
                return;
            }

            var timestamp = GetTimestamp() / 10;  // in micro seconds

            using var usbmon = new BinaryWriter(new MemoryStream());
            usbmon.Write((ulong)basic.seqnum);
            usbmon.Write((byte)'C');
            usbmon.Write((byte)0); // ISO
            usbmon.Write(basic.RawEndpoint());
            usbmon.Write((byte)basic.devid);
            usbmon.Write((ushort)(basic.devid >> 16));
            usbmon.Write((byte)'-');
            usbmon.Write((byte)(data.IsEmpty ? basic.direction == UsbIpDir.USBIP_DIR_IN ? '<' : '>' : '\0'));
            usbmon.Write(timestamp / 1000000); // seconds
            usbmon.Write((uint)(timestamp % 1000000)); // micro seconds
            usbmon.Write(retSubmit.status);
            usbmon.Write(retSubmit.actual_length); // length
            usbmon.Write(data.Length + packetDescriptors.Length * 16); // actual
            usbmon.Write((uint)retSubmit.error_count); // ISO error count
            usbmon.Write((uint)packetDescriptors.Length);
            usbmon.Write(cmdSubmit.interval);
            usbmon.Write(cmdSubmit.start_frame);
            usbmon.Write(cmdSubmit.transfer_flags);
            usbmon.Write(cmdSubmit.number_of_packets);
            var actualOffset = 0u;
            foreach (var packetDescriptor in packetDescriptors)
            {
                // NOTE: Usbmon on Linux gets this wrong. On input, the actual_offset needs to be calculated.
                usbmon.Write(packetDescriptor.status);
                usbmon.Write(basic.direction == UsbIpDir.USBIP_DIR_IN ? actualOffset : packetDescriptor.offset);
                usbmon.Write(packetDescriptor.actual_length);
                usbmon.Write((uint)0); // padding
                actualOffset += packetDescriptor.actual_length;
            }
            usbmon.Write(data);

            BlockChannel.Writer.WriteAsync(CreateEnhancedPacketBlock(usbmon)).AsTask().Wait();
        }

        async Task PacketWriterAsync(CancellationToken cancellationToken)
        {
            try
            {
                await Stream.WriteAsync(CreateSectionHeaderBlock(), CancellationToken.None);
                await Stream.WriteAsync(CreateInterfaceDescriptionBlock(), CancellationToken.None);
                bool needFlush = true;
                while (true)
                {
                    using var flushTimer = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    if (needFlush)
                    {
                        flushTimer.CancelAfter(TimeSpan.FromSeconds(5));
                    }
                    try
                    {
                        await BlockChannel.Reader.WaitToReadAsync(flushTimer.Token);
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
            return TimestampBase + (ulong)Stopwatch.ElapsedTicks * 10000000 / (ulong)Stopwatch.Frequency;
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
            AddOption(block, code, new byte[] { value });
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
            block.Write((ushort)1); // major pcapng version
            block.Write((ushort)0); // minor pcapng version
            block.Write(0xffffffffffffffff); // unspecified section size
            AddOption(block, 3, $"{Environment.OSVersion.VersionString}"); // shb_os
            AddOption(block, 4, $"{Program.Product} {GitVersionInformation.InformationalVersion}"); // shb_userappl
            return FinishBlock(block);
        }

        byte[] CreateInterfaceDescriptionBlock()
        {
            using var block = CreateBlock(1);
            block.Write((ushort)220); // LINKTYPE_USB_LINUX_MMAPPED
            block.Write((ushort)0); // reserved
            block.Write(SnapLength); // snaplen (0 == unlimited)
            AddOption(block, 2, "USBIP"); // if_name
            AddOption(block, 9, 7); // if_tsresol, 10^-7 s == 100 ns
            return FinishBlock(block);
        }

        byte[] CreateEnhancedPacketBlock(BinaryWriter usbmon)
        {
            var timestamp = GetTimestamp();

            usbmon.Flush();
            var data = ((MemoryStream)usbmon.BaseStream).ToArray();
            var captureLength = ((SnapLength == 0) || (data.Length < SnapLength)) ? data.Length : (int)SnapLength;

            using var block = CreateBlock(6);
            block.Write(0); // interface ID
            block.Write((uint)(timestamp >> 32));
            block.Write((uint)timestamp);
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
            block.Write((uint)(timestamp >> 32));
            block.Write((uint)timestamp);
            AddOption(block, 2, BitConverter.GetBytes((uint)(TimestampBase >> 32)).Concat(BitConverter.GetBytes((uint)TimestampBase)).ToArray()); // isb_starttime
            AddOption(block, 3, BitConverter.GetBytes((uint)(timestamp >> 32)).Concat(BitConverter.GetBytes((uint)timestamp)).ToArray()); // isb_endtime
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
            block.Seek(4, SeekOrigin.Begin);
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
}
