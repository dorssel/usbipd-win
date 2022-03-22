// SPDX-FileCopyrightText: 2020 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-2.0-only

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Windows.Win32;
using Windows.Win32.Devices.Usb;

using static UsbIpServer.Interop.Linux;
using static UsbIpServer.Interop.UsbIp;
using static UsbIpServer.Interop.VBoxUsb;
using static UsbIpServer.Tools;

namespace UsbIpServer
{
    sealed class AttachedClient
    {
        public AttachedClient(ILogger<AttachedClient> logger, ClientContext clientContext, PcapNg pcap)
        {
            Logger = logger;
            ClientContext = clientContext;
            Pcap = pcap;

            var tcpClient = clientContext.TcpClient;
            Stream = tcpClient.GetStream();

            Device = clientContext.AttachedDevice ?? throw new ArgumentException($"{nameof(ClientContext.AttachedDevice)} is null");

            tcpClient.NoDelay = true;
        }

        readonly ILogger Logger;
        readonly ClientContext ClientContext;
        readonly PcapNg Pcap;
        readonly NetworkStream Stream;
        readonly Channel<byte[]> ReplyChannel = Channel.CreateUnbounded<byte[]>();
        readonly DeviceFile Device;

        /// <summary>
        /// Mapping from USBIP seqnum to raw USB endpoint number.
        /// Used for UNLINK.
        /// </summary>
        readonly ConcurrentDictionary<uint, byte> PendingSubmits = new();

        /// <summary>
        /// Mapping from endpoint to its channel for ordered replies.
        /// </summary>
        readonly ConcurrentDictionary<byte, ChannelWriter<Task<byte[]>>> EndpointChannels = new();

        /// <summary>
        /// Returns the channel writer for the given raw endpoint number.
        /// </summary>
        ChannelWriter<Task<byte[]>> GetEndpointWriter(byte rawEndpoint, CancellationToken cancellationToken)
        {
            return EndpointChannels.GetOrAdd(rawEndpoint, (_) =>
            {
                var channel = Channel.CreateUnbounded<Task<byte[]>>();
                Task.Run(async () =>
                {
                    // This task ensures that all replies for this specific endpoint are
                    // returned in the same order as the requests.
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        var nextEndpointTask = await channel.Reader.ReadAsync(cancellationToken);
                        var nextEndpointReply = await nextEndpointTask;
                        // This multiplexes the replies for this endpoint with the other endpoints.
                        await ReplyChannel.Writer.WriteAsync(nextEndpointReply);
                    }
                }, cancellationToken);
                return channel.Writer;
            });
        }

        async Task HandleSubmitIsochronousAsync(UsbIpHeaderBasic basic, UsbIpHeaderCmdSubmit submit, CancellationToken cancellationToken)
        {
            var buf = new byte[submit.transfer_buffer_length];
            if (basic.direction == UsbIpDir.USBIP_DIR_OUT)
            {
                await Stream.ReadExactlyAsync(buf, cancellationToken);
            }

            var packetDescriptors = await Stream.ReadUsbIpIsoPacketDescriptorsAsync(submit.number_of_packets, cancellationToken);
            if (packetDescriptors.Any((d) => d.length > ushort.MaxValue))
            {
                // VBoxUSB uses ushort for length, and that is fine as none of the current
                // USB standards support larger ISO packets sizes. This is just a sanity check.
                throw new ProtocolViolationException("ISO packet too big");
            }
            if (packetDescriptors.Sum((d) => d.length) != submit.transfer_buffer_length)
            {
                // USBIP requires the packets in the data buffer to be sequential without any padding.
                throw new ProtocolViolationException($"cumulative lengths of ISO packets does not match transfer_buffer_length");
            }

            // Everything has been read and validated, now process...

            Pcap.DumpPacketIsoRequest(basic, submit, packetDescriptors, basic.direction == UsbIpDir.USBIP_DIR_OUT ? buf : ReadOnlySpan<byte>.Empty);

            // To support UNLINK, we must be able to abort the pipe that is used for this URB.
            // We need the raw USB endpoint number, i.e. including the high bit for input pipes.
            if (!PendingSubmits.TryAdd(basic.seqnum, basic.RawEndpoint()))
            {
                throw new ProtocolViolationException($"duplicate sequence number {basic.seqnum}");
            }

            // VBoxUSB only excepts up to 8 iso packets per ioctl, so we may have to split
            // the request into multiple ioctls.
            List<Task> ioctls = new();

            // Input or output, single or multiple URBs, exceptions or not, this buffer must be locked until after all ioctls have completed.
            var gcHandle = GCHandle.Alloc(buf, GCHandleType.Pinned);
            try
            {
                // Now queue as many ioctls as required, each ioctl covering as many iso packets as will fit:
                // up to 8 ISO packets per URB, or less if the offset does not fit into an ushort anymore.
                var isoIndex = 0;
                var urbBufOffset = 0;
                while (isoIndex < submit.number_of_packets)
                {
                    var urbIsoOffset = isoIndex;
                    var urb = new UsbSupUrb()
                    {
                        ep = basic.ep,
                        type = UsbSupTransferType.USBSUP_TRANSFER_TYPE_ISOC,
                        dir = (basic.direction == UsbIpDir.USBIP_DIR_IN) ? UsbSupDirection.USBSUP_DIRECTION_IN : UsbSupDirection.USBSUP_DIRECTION_OUT,
                        flags = UsbSupXferFlags.USBSUP_FLAG_NONE,
                        error = UsbSupError.USBSUP_XFER_OK,
                        len = 0,
                        buf = gcHandle.AddrOfPinnedObject() + urbBufOffset,
                        numIsoPkts = 0,
                        aIsoPkts = new UsbSupIsoPkt[8],
                    };

                    while (isoIndex < submit.number_of_packets // there are more iso packets in the original request
                        && urb.numIsoPkts < urb.aIsoPkts.Length // and more will actually fit in this URB
                        && urb.len <= ushort.MaxValue) // and the next URB-relative offset will fit in ushort
                    {
                        urb.aIsoPkts[urb.numIsoPkts].cb = (ushort)packetDescriptors[isoIndex].length;
                        urb.aIsoPkts[urb.numIsoPkts].off = (ushort)urb.len;
                        urb.len += urb.aIsoPkts[urb.numIsoPkts].cb;
                        urb.numIsoPkts++;
                        isoIndex++;
                    }

                    // No more iso packets will fit in this ioctl, or this was all of them, but we do have at least one.
                    var bytes = new byte[Marshal.SizeOf<UsbSupUrb>()];
                    StructToBytes(urb, bytes);
                    // Note that we are adding the continuation task, not the actual ioctl.
                    ioctls.Add(Device.IoControlAsync(SUPUSB_IOCTL.SEND_URB, bytes, bytes).ContinueWith((task, state) =>
                    {
                        BytesToStruct(bytes, out urb);

                        for (var i = 0; i < urb.numIsoPkts; ++i)
                        {
                            packetDescriptors[urbIsoOffset + i].actual_length = urb.aIsoPkts[i].cb;
                            packetDescriptors[urbIsoOffset + i].status = (uint)-(int)ConvertError(urb.aIsoPkts[i].stat);
                        }
                    }, cancellationToken, TaskScheduler.Default));

                    urbBufOffset += (int)urb.len;
                }

                // Continue when all ioctls *and* their continuations have been completed.
                var replyTask = Task.WhenAll(ioctls).ContinueWith(byte[] (task, _) =>
                {
                    // The pending request is now completed; no need to support UNLINK any longer.
                    PendingSubmits.TryRemove(basic.seqnum, out var _);

                    var header = new UsbIpHeader
                    {
                        basic = new()
                        {
                            command = UsbIpCmd.USBIP_RET_SUBMIT,
                            seqnum = basic.seqnum,
                        },
                        ret_submit = new()
                        {
                            status = -(int)Errno.SUCCESS,
                            actual_length = (int)packetDescriptors.Sum((pd) => pd.actual_length),
                            start_frame = submit.start_frame,
                            number_of_packets = submit.number_of_packets,
                            error_count = packetDescriptors.Count((d) => d.status != -(int)Errno.SUCCESS),
                        },
                    };

                    var retBuf = buf;
                    if ((basic.direction == UsbIpDir.USBIP_DIR_IN) && (header.ret_submit.actual_length != submit.transfer_buffer_length))
                    {
                        // USBIP requires us to transfer the actual data without padding.
                        retBuf = new byte[header.ret_submit.actual_length];
                        var sourceOffset = 0;
                        var destinationOffset = 0;
                        foreach (var pd in packetDescriptors)
                        {
                            buf.AsSpan(sourceOffset, (int)pd.actual_length).CopyTo(retBuf.AsSpan(destinationOffset));
                            sourceOffset += (int)pd.length;
                            destinationOffset += (int)pd.actual_length;
                        }
                    }

                    Pcap.DumpPacketIsoReply(basic, submit, header.ret_submit, packetDescriptors, basic.direction == UsbIpDir.USBIP_DIR_IN ? retBuf.AsSpan(0, header.ret_submit.actual_length) : ReadOnlySpan<byte>.Empty);
                    using var replyStream = new MemoryStream();
                    replyStream.Write(header.ToBytes());
                    if (basic.direction == UsbIpDir.USBIP_DIR_IN)
                    {
                        replyStream.Write(retBuf);
                    }
                    replyStream.Write(packetDescriptors.ToBytes());
                    return replyStream.ToArray();
                }, cancellationToken, TaskScheduler.Default);

                // Now we queue the task that creates the response, so that all replies for a single
                // endpoint remain ordered.

                var endpointWriter = GetEndpointWriter(basic.RawEndpoint(), cancellationToken);
                await endpointWriter.WriteAsync(replyTask, cancellationToken);

                // We return to the caller, so that the next request can be handled. As a result, multiple requests
                // can be outstanding (either for the same, or for multiple endpoints). Requests are completed
                // asynchronously and in any order, but the replies for each endpoint are sent to the client in original order.
            }
            finally
            {
                _ = Task.WhenAll(ioctls).ContinueWith((task) =>
                {
                    gcHandle.Free();
                }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }

        async Task HandleSubmitAsync(UsbIpHeaderBasic basic, UsbIpHeaderCmdSubmit submit, CancellationToken cancellationToken)
        {
            if (basic.EndpointType(submit) == UsbSupTransferType.USBSUP_TRANSFER_TYPE_ISOC)
            {
                await HandleSubmitIsochronousAsync(basic, submit, cancellationToken);
                return;
            }

            var urb = new UsbSupUrb()
            {
                type = basic.EndpointType(submit),
                ep = basic.ep,
                dir = (basic.direction == UsbIpDir.USBIP_DIR_IN) ? UsbSupDirection.USBSUP_DIRECTION_IN : UsbSupDirection.USBSUP_DIRECTION_OUT,
                flags = (basic.direction == UsbIpDir.USBIP_DIR_IN)
                    ? (((submit.transfer_flags & 1) != 0) ? UsbSupXferFlags.USBSUP_FLAG_NONE : UsbSupXferFlags.USBSUP_FLAG_SHORT_OK)
                    : UsbSupXferFlags.USBSUP_FLAG_NONE,
                error = UsbSupError.USBSUP_XFER_OK,
                len = submit.transfer_buffer_length,
                numIsoPkts = 0, // number_of_packets == 0 here
                aIsoPkts = new UsbSupIsoPkt[8], // unused, but must be present for VBoxUsb
            };

            var requestLength = submit.transfer_buffer_length;
            var payloadOffset = 0;
            if (urb.type == UsbSupTransferType.USBSUP_TRANSFER_TYPE_MSG)
            {
                payloadOffset = Marshal.SizeOf<USB_DEFAULT_PIPE_SETUP_PACKET>();
                urb.len += (uint)payloadOffset;
            }

            var bytes = new byte[Marshal.SizeOf<UsbSupUrb>()];
            var buf = new byte[urb.len];

            if (urb.type == UsbSupTransferType.USBSUP_TRANSFER_TYPE_MSG)
            {
                StructToBytes(submit.setup, buf);
            }

            if (basic.direction == UsbIpDir.USBIP_DIR_OUT)
            {
                await Stream.ReadExactlyAsync(buf.AsMemory()[payloadOffset..], cancellationToken);
            }

            // We now have received the entire SUBMIT request:
            // - If the request is "special" (reconfig, clear), then we will handle it immediately and await the result.
            //   This means no further requests will be read until the special request has completed.
            // - Otherwise, we will start a new task so that the receiver can continue.
            //   This means multiple URBs can be outstanding awaiting completion.
            //   The pending URBs can be completed out of order, but for each endpoint the replies must be sent in order.

            Pcap.DumpPacketNonIsoRequest(basic, submit, basic.direction == UsbIpDir.USBIP_DIR_OUT ? buf.AsSpan(payloadOffset) : ReadOnlySpan<byte>.Empty);

            Task ioctl;
            var pending = false;

            if ((basic.ep == 0)
                && (submit.setup.bmRequestType.B == PInvoke.BMREQUEST_TO_DEVICE)
                && (submit.setup.bRequest == PInvoke.USB_REQUEST_SET_CONFIGURATION))
            {
                // VBoxUsb needs this to get the endpoint handles
                var setConfig = new UsbSupSetConfig()
                {
                    bConfigurationValue = submit.setup.wValue.Anonymous.LowByte,
                };
                Logger.Debug($"Trapped SET_CONFIGURATION: {setConfig.bConfigurationValue}");
                await Device.IoControlAsync(SUPUSB_IOCTL.USB_SET_CONFIG, StructToBytes(setConfig), null);
                ioctl = Task.CompletedTask;
            }
            else if ((basic.ep == 0)
                && (submit.setup.bmRequestType.B == PInvoke.BMREQUEST_TO_INTERFACE)
                && (submit.setup.bRequest == PInvoke.USB_REQUEST_SET_INTERFACE))
            {
                // VBoxUsb needs this to get the endpoint handles
                var selectInterface = new UsbSupSelectInterface()
                {
                    bInterfaceNumber = submit.setup.wIndex.Anonymous.LowByte,
                    bAlternateSetting = submit.setup.wValue.Anonymous.LowByte,
                };
                Logger.Debug($"Trapped SET_INTERFACE: {selectInterface.bInterfaceNumber} -> {selectInterface.bAlternateSetting}");
                await Device.IoControlAsync(SUPUSB_IOCTL.USB_SELECT_INTERFACE, StructToBytes(selectInterface), null);
                ioctl = Task.CompletedTask;
            }
            else if ((basic.ep == 0)
                && (submit.setup.bmRequestType.B == PInvoke.BMREQUEST_TO_ENDPOINT)
                && (submit.setup.bRequest == PInvoke.USB_REQUEST_CLEAR_FEATURE)
                && (submit.setup.wValue.W == PInvoke.USB_FEATURE_ENDPOINT_STALL))
            {
                // VBoxUsb needs this to notify the host controller
                var clearEndpoint = new UsbSupClearEndpoint()
                {
                    bEndpoint = submit.setup.wIndex.Anonymous.LowByte,
                };
                Logger.Debug($"Trapped CLEAR_FEATURE: {clearEndpoint.bEndpoint}");
                await Device.IoControlAsync(SUPUSB_IOCTL.USB_CLEAR_ENDPOINT, StructToBytes(clearEndpoint), null);
                ioctl = Task.CompletedTask;
            }
            else
            {
                // To support UNLINK, we must be able to abort the pipe that is used for this URB.
                // We need the raw USB endpoint number, i.e. including the high bit for input pipes.
                if (!PendingSubmits.TryAdd(basic.seqnum, basic.RawEndpoint()))
                {
                    throw new ProtocolViolationException($"duplicate sequence number {basic.seqnum}");
                }
                pending = true;

                // Input or output, exceptions or not, this buffer must be locked until after the ioctl has completed.
                var gcHandle = GCHandle.Alloc(buf, GCHandleType.Pinned);
                try
                {
                    urb.buf = gcHandle.AddrOfPinnedObject();
                    StructToBytes(urb, bytes);
                    ioctl = Device.IoControlAsync(SUPUSB_IOCTL.SEND_URB, bytes, bytes);
                }
                catch
                {
                    gcHandle.Free();
                    throw;
                }
                _ = ioctl.ContinueWith((task) =>
                {
                    gcHandle.Free();
                }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }

            // At this point we have initiated the ioctl (and possibly awaited it for special cases).
            // Now we schedule a continuation to create the response once the ioctl completes.

            var replyTask = ioctl.ContinueWith(byte[] (task, _) =>
            {
                if (pending)
                {
                    // The pending request is now completed; no need to support UNLINK any longer.
                    PendingSubmits.Remove(basic.seqnum, out var _);
                    BytesToStruct(bytes, out urb);
                }

                if ((basic.ep == 0)
                    && (submit.setup.bmRequestType.B == (PInvoke.BMREQUEST_TO_DEVICE | (PInvoke.BMREQUEST_DEVICE_TO_HOST << 7)))
                    && (submit.setup.bRequest == PInvoke.USB_REQUEST_GET_DESCRIPTOR)
                    && (submit.setup.wValue.Anonymous.HiByte == PInvoke.USB_CONFIGURATION_DESCRIPTOR_TYPE))
                {
                    try
                    {
                        var configuration = BytesToStruct<USB_CONFIGURATION_DESCRIPTOR>(buf.AsSpan(payloadOffset));
                        if ((configuration.bDescriptorType == PInvoke.USB_CONFIGURATION_DESCRIPTOR_TYPE)
                            && ((configuration.bmAttributes & PInvoke.USB_CONFIG_REMOTE_WAKEUP) == PInvoke.USB_CONFIG_REMOTE_WAKEUP))
                        {
                            Logger.Debug("Masked USB_CONFIG_REMOTE_WAKEUP");
                            configuration.bmAttributes &= unchecked((byte)~PInvoke.USB_CONFIG_REMOTE_WAKEUP);
                            StructToBytes(configuration, buf.AsSpan(payloadOffset));
                        }
                    }
#pragma warning disable CA1031 // Do not catch general exception types
                    catch { }
#pragma warning restore CA1031 // Do not catch general exception types
                }

                var header = new UsbIpHeader
                {
                    basic = new()
                    {
                        command = UsbIpCmd.USBIP_RET_SUBMIT,
                        seqnum = basic.seqnum,
                    },
                    ret_submit = new()
                    {
                        status = -(int)ConvertError(urb.error),
                        actual_length = (int)urb.len,
                        start_frame = 0, // shall be 0 for non-ISO
                        number_of_packets = unchecked((int)0xffffffff), // shall be 0xffffffff for non-ISO
                        error_count = 0,
                    },
                };

                if (urb.type == UsbSupTransferType.USBSUP_TRANSFER_TYPE_MSG)
                {
                    header.ret_submit.actual_length = (header.ret_submit.actual_length > payloadOffset) ? (header.ret_submit.actual_length - payloadOffset) : 0;
                }

                if (urb.error != UsbSupError.USBSUP_XFER_OK)
                {
                    Logger.Debug($"{urb.error} -> {ConvertError(urb.error)} -> {header.ret_submit.status}");
                }

                Pcap.DumpPacketNonIsoReply(basic, submit, header.ret_submit, basic.direction == UsbIpDir.USBIP_DIR_IN ? buf.AsSpan(payloadOffset, header.ret_submit.actual_length) : ReadOnlySpan<byte>.Empty);
                using var replyStream = new MemoryStream();
                replyStream.Write(header.ToBytes());
                if (basic.direction == UsbIpDir.USBIP_DIR_IN)
                {
                    replyStream.Write(buf.AsSpan(payloadOffset, header.ret_submit.actual_length));
                }
                return replyStream.ToArray();
            }, cancellationToken, TaskScheduler.Default);

            // Now we queue the task that creates the response, so that all replies for a single
            // endpoint remain ordered.

            var endpointWriter = GetEndpointWriter(basic.RawEndpoint(), cancellationToken);
            await endpointWriter.WriteAsync(replyTask, cancellationToken);

            // We return to the caller, so that the next request can be handled. As a result, multiple requests
            // can be outstanding (either for the same, or for multiple endpoints). Requests are completed
            // asynchronously and in any order, but the replies for each endpoint are sent to the client in original order.
        }

        async Task HandleUnlinkAsync(UsbIpHeaderBasic basic, UsbIpHeaderCmdUnlink unlink, CancellationToken cancellationToken)
        {
            // We are synchronous with the receiver.

            var pending = PendingSubmits.TryGetValue(unlink.seqnum, out var rawEndpoint);
            Logger.Trace($"Unlinking {unlink.seqnum}, pending = {pending}, pending count = {PendingSubmits.Count}");

            if (pending)
            {
                // VBoxUSB does not support canceling ioctls, so we will abort the pipe, which effectively cancels all URBs to that endpoint.
                // This is OK, since Linux will normally unlink all URBs anyway in quick succession.
                var clearEndpoint = new UsbSupClearEndpoint()
                {
                    bEndpoint = rawEndpoint,
                };
                // Just like for CLEAR_FEATURE, we are going to wait until this finishes,
                // in order to avoid races with subsequent SUBMIT to the same endpoint.
                Logger.Trace($"Aborting endpoint {rawEndpoint}");
                await Device.IoControlAsync(SUPUSB_IOCTL.USB_ABORT_ENDPOINT, StructToBytes(clearEndpoint), null);
            }

            var header = new UsbIpHeader
            {
                basic = new()
                {
                    command = UsbIpCmd.USBIP_RET_UNLINK,
                    seqnum = basic.seqnum,
                },
                ret_submit = new()
                {
                    status = -(int)Errno.SUCCESS,
                },
            };

            if (pending)
            {
                // We need to queue this on the same endpoint that the UNLINK was for, such
                // that the reply of the aborted request gets sent first.
                var endpointWriter = GetEndpointWriter(rawEndpoint, cancellationToken);
                await endpointWriter.WriteAsync(Task.FromResult(header.ToBytes()), cancellationToken);
            }
            else
            {
                // We didn't actually need to abort anything, so we can write the reply immediately.
                await ReplyChannel.Writer.WriteAsync(header.ToBytes(), cancellationToken);
            }
        }

        public async Task RunAsync(CancellationToken cancellationToken)
        {
            _ = Task.Run(async () =>
            {
                // This task multiplexes all the replies.
                while (!cancellationToken.IsCancellationRequested)
                {
                    var nextReply = await ReplyChannel.Reader.ReadAsync(cancellationToken);
                    await Stream.WriteAsync(nextReply, cancellationToken);
                }
            }, cancellationToken);

            while (true)
            {
                var header = await Stream.ReadUsbIpHeaderAsync(cancellationToken);
                switch (header.basic.command)
                {
                    case UsbIpCmd.USBIP_CMD_SUBMIT:
                        await HandleSubmitAsync(header.basic, header.cmd_submit, cancellationToken);
                        break;
                    case UsbIpCmd.USBIP_CMD_UNLINK:
                        await HandleUnlinkAsync(header.basic, header.cmd_unlink, cancellationToken);
                        break;
                    default:
                        throw new ProtocolViolationException($"unknown UsbIpCmd {header.basic.command}");
                }
            }
        }
    }
}
