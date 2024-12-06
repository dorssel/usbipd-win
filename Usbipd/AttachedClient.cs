// SPDX-FileCopyrightText: 2020 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using Usbipd.Automation;
using static Usbipd.Interop.Linux;
using static Usbipd.Interop.UsbIp;

namespace Usbipd;

sealed class AttachedClient
{
    public AttachedClient(ILoggerFactory loggerFactory, ClientContext clientContext, PcapNg pcap)
    {
        LoggerFactory = loggerFactory;
        ClientContext = clientContext;
        Pcap = pcap;
        BusId = (BusId)clientContext.AttachedBusId!;

        var tcpClient = clientContext.TcpClient;
        Stream = tcpClient.GetStream();

        tcpClient.NoDelay = true;
    }

    readonly ILoggerFactory LoggerFactory;
    readonly ClientContext ClientContext;
    readonly PcapNg Pcap;
    readonly BusId BusId;
    readonly NetworkStream Stream;
    readonly Channel<RequestReply> ReplyChannel = Channel.CreateUnbounded<RequestReply>();

    readonly Dictionary<byte, AttachedEndpoint> AttachedEndpoints = [];

    AttachedEndpoint GetAttachedEndpoint(byte rawEndpoint, CancellationToken cancellationToken)
    {
        if (!AttachedEndpoints.TryGetValue(rawEndpoint, out var attachedEndpoint))
        {
            attachedEndpoint = new AttachedEndpoint(LoggerFactory.CreateLogger($"{ClientContext.AttachedBusId!.Value}.{rawEndpoint & 0x0f}"), ClientContext,
                Pcap, rawEndpoint, ReplyChannel, cancellationToken);
            AttachedEndpoints.Add(rawEndpoint, attachedEndpoint);
        }
        return attachedEndpoint;
    }
    /// <summary>
    /// Mapping from USBIP seqnum to raw USB endpoint number.
    /// Used for UNLINK.
    /// </summary>
    readonly ConcurrentDictionary<uint, byte> PendingSubmits = [];

    // UNLINK strategy
    // ===============
    //
    // UNLINK serves two purposes, which go hand-in-hand on Linux, but not on Windows.
    // 1) It indicates that the client no longer is interested in the result. If UNLINK wins
    //    from SUBMIT completion, then the client no longer wants the the SUBMIT reply.
    //    So, either of the following is the case:
    //    a) After receiving UNLINK, we reply that UNLINKing was successful and never send a SUBMIT reply.
    //       This is preferred, as this is what the client wants. Or,
    //    b) SUBMIT completion won the race and the SUBMIT reply is followed by an unsuccessful ("too late") UNLINK reply.
    //    This is all handled by this AttachedClient class. Our reply writer keeps track of pending submits
    //    and follows either path a or b.
    //    See: https://docs.kernel.org/usb/usbip_protocol.html
    // 2) The URB should be canceled (with a race condition of it already being completed, of course).
    //    On Linux, this is handled alongside with (1), but the VBoxUSB driver cannot cancel individual
    //    URBs; it can only abort entire endpoints, which cancels all URBs for that endpoint at once.
    //    This is very different from Linux; it is handled by the AttachedEndpoint class.

#pragma warning disable IDE1006 // Naming Styles
    readonly record struct PendingUnlink(uint unlink_seqnum, uint submit_seqnum);
#pragma warning restore IDE1006 // Naming Styles
    readonly ConcurrentQueue<PendingUnlink> PendingUnlinks = new();

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        _ = Task.Run(async () =>
        {
            // This task multiplexes all the replies.
            while (!cancellationToken.IsCancellationRequested)
            {
                var reply = await ReplyChannel.Reader.ReadAsync(cancellationToken);
                {
                    // We prefer UNLINK to win the race, so drain the UNLINK queue first.
                    while (PendingUnlinks.TryDequeue(out var unlink))
                    {
                        // Determine whether we won (i.e., we are in time to UNLINK), or lost (i.e., the SUBMIT reply was already sent).
                        var won = PendingSubmits.TryRemove(unlink.submit_seqnum, out var _);
                        var header = new UsbIpHeader
                        {
                            basic = new()
                            {
                                command = UsbIpCmd.USBIP_RET_UNLINK,
                                seqnum = unlink.unlink_seqnum,
                            },
                            ret_unlink = new()
                            {
                                // A bit weird: if UNLINK *wins*, then we return the error ECONNRESET,
                                // but if we *lose*, then we return SUCCESS. Oh well, that's what the specs say...
                                status = -(int)(won ? Errno.ECONNRESET : Errno.SUCCESS),
                            },
                        };
                        Pcap.DumpPacketUnlink(BusId, true, header);
                        await Stream.WriteAsync(header.ToBytes(), cancellationToken);
                    }
                    // Only write the reply if it was an actual SUBMIT request that was still pending after processing UNLINK.
                    // All dummy UNLINK replies from the reader and all SUBMIT replies for already UNLINKed URBs are simply dropped.
                    if (PendingSubmits.TryRemove(reply.Seqnum, out var _))
                    {
                        await Stream.WriteAsync(reply.Bytes, cancellationToken);
                    }
                }
            }
        }, cancellationToken);

        while (!cancellationToken.IsCancellationRequested)
        {
            var header = await Stream.ReadUsbIpHeaderAsync(cancellationToken);
            switch (header.basic.command)
            {
                case UsbIpCmd.USBIP_CMD_SUBMIT:
                    {
                        // We relay this to the actual endpoint, as requests *and* replies need to remain ordered per endpoint
                        // (but different endpoints may interleave their results).
                        if (!PendingSubmits.TryAdd(header.basic.seqnum, header.basic.RawEndpoint()))
                        {
                            throw new ProtocolViolationException($"duplicate sequence number {header.basic.seqnum}");
                        }
                        var attachedEndpoint = GetAttachedEndpoint(header.basic.RawEndpoint(), cancellationToken);
                        await attachedEndpoint.HandleSubmitAsync(header.basic, header.cmd_submit, cancellationToken);
                    }
                    break;
                case UsbIpCmd.USBIP_CMD_UNLINK:
                    {
                        Pcap.DumpPacketUnlink(BusId, false, header);
                        // Queue the unlink so it will be handled by the writer first (we prefer the UNLINK to win the race).
                        PendingUnlinks.Enqueue(new(header.basic.seqnum, header.cmd_unlink.seqnum));
                        // We cancel the URB if it still pending.
                        if (PendingSubmits.TryGetValue(header.cmd_unlink.seqnum, out var rawEndpoint))
                        {
                            var attachedEndpoint = GetAttachedEndpoint(rawEndpoint, cancellationToken);
                            await attachedEndpoint.HandleUnlinkAsync();
                        }
                        // Note that this is just a dummy reply. The actual reply itself is generated by the unlink handler in the writer task.
                        // This is necessary, as only the writer is able to resolve the race between SUBMIT completion and UNLINK.
                        // This dummy reply is just to wake up the writer.
                        await ReplyChannel.Writer.WriteAsync(new(header.basic.seqnum, []), cancellationToken);
                    }
                    break;
                case UsbIpCmd.USBIP_RET_SUBMIT:
                case UsbIpCmd.USBIP_RET_UNLINK:
                default:
                    throw new ProtocolViolationException($"unknown UsbIpCmd {header.basic.command}");
            }
        }
    }
}
