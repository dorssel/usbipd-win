// SPDX-FileCopyrightText: 2023 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using Windows.Win32;
using Windows.Win32.Devices.Usb;

using static Usbipd.Interop.Linux;
using static Usbipd.Interop.UsbIp;
using static Usbipd.Interop.VBoxUsb;
using static Usbipd.Tools;

namespace Usbipd;

sealed class AttachedEndpoint
{
    public AttachedEndpoint(ILogger logger, ClientContext clientContext, PcapNg pcap, byte rawEndpoint, Channel<RequestReply> replyChannel, CancellationToken cancellationToken)
    {
        Logger = logger;
        Logger.Debug("Endpoint created");

        var tcpClient = clientContext.TcpClient;
        Stream = tcpClient.GetStream();
        Device = clientContext.AttachedDevice ?? throw new ArgumentException($"{nameof(ClientContext.AttachedDevice)} is null");

        Pcap = pcap;

        RawEndpoint = rawEndpoint;

        ReplyChannel = replyChannel;

        Task.Run(async () =>
        {
            // This task ensures that all SUBMIT replies for this specific endpoint are
            // returned in the same order as the requests.
            while (!cancellationToken.IsCancellationRequested)
            {
                var replyTask = await EndpointChannel.Reader.ReadAsync(cancellationToken);
                var reply = await replyTask;
                // This multiplexes the replies for this endpoint with the other endpoints.
                await ReplyChannel.Writer.WriteAsync(reply);
            }
        }, cancellationToken);
    }

    readonly ILogger Logger;
    readonly byte RawEndpoint;
    readonly PcapNg Pcap;
    readonly NetworkStream Stream;
    readonly Channel<RequestReply> ReplyChannel;
    readonly DeviceFile Device;
    long InterlockedSubmits;
    long UnlinkHoldoffCount;

    readonly Channel<Task<RequestReply>> EndpointChannel = Channel.CreateUnbounded<Task<RequestReply>>(new() { SingleWriter = true, SingleReader = true });

    async Task HandleSubmitIsochronousAsync(UsbIpHeaderBasic basic, UsbIpHeaderCmdSubmit submit, CancellationToken cancellationToken)
    {
        var buf = new byte[submit.transfer_buffer_length];
        if (basic.direction == UsbIpDir.USBIP_DIR_OUT)
        {
            await Stream.ReadMessageAsync(buf, cancellationToken);
        }

        var packetDescriptors = await Stream.ReadUsbIpIsoPacketDescriptorsAsync(submit.number_of_packets, cancellationToken);
        if (packetDescriptors.Any(d => d.length > ushort.MaxValue))
        {
            // VBoxUSB uses ushort for length, and that is fine as none of the current
            // USB standards support larger ISO packets sizes. This is just a sanity check.
            throw new ProtocolViolationException("ISO packet too big");
        }
        if (packetDescriptors.Sum(d => d.length) != submit.transfer_buffer_length)
        {
            // USBIP requires the packets in the data buffer to be sequential without any padding.
            throw new ProtocolViolationException($"cumulative lengths of ISO packets does not match transfer_buffer_length");
        }

        // Everything has been read and validated, now process...

        Pcap.DumpPacketIsoRequest(basic, submit, packetDescriptors, basic.direction == UsbIpDir.USBIP_DIR_OUT ? buf : ReadOnlySpan<byte>.Empty);

        // VBoxUSB only excepts up to 8 iso packets per ioctl, so we may have to split
        // the request into multiple ioctls.
        List<Task> ioctls = [];

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
            var replyTask = Task.WhenAll(ioctls).ContinueWith(RequestReply (task, _) =>
            {
                Interlocked.Decrement(ref InterlockedSubmits);

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
                        actual_length = (int)packetDescriptors.Sum(pd => pd.actual_length),
                        start_frame = submit.start_frame,
                        number_of_packets = submit.number_of_packets,
                        error_count = packetDescriptors.Count(d => d.status != -(int)Errno.SUCCESS),
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
                return new(basic.seqnum, replyStream.ToArray());
            }, cancellationToken, TaskScheduler.Default);

            // Now we queue the task that creates the response, so that all replies for a single
            // endpoint remain ordered.

            await EndpointChannel.Writer.WriteAsync(replyTask, cancellationToken);

            // We return to the caller, so that the next request can be handled. As a result, multiple requests
            // can be outstanding (either for the same, or for multiple endpoints). Requests are completed
            // asynchronously and in any order, but the replies for each endpoint are sent to the client in original order.
        }
        finally
        {
            _ = Task.WhenAll(ioctls).ContinueWith(task =>
            {
                gcHandle.Free();
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }

    public async Task HandleSubmitAsync(UsbIpHeaderBasic basic, UsbIpHeaderCmdSubmit submit, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref InterlockedSubmits);

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
            // USBIP documentation states that for non-ISO transfers number_of_packets shall be -1,
            // but e.g. Linux ans some clients use 0. VBoxUsb requires 0 here.
            // We simply ignore number_of_packets.
            numIsoPkts = 0,
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
            await Stream.ReadMessageAsync(buf.AsMemory()[payloadOffset..], cancellationToken);
        }

        // We now have received the entire SUBMIT request:
        // - If the request is "special" (reconfig, clear), then we will handle it immediately and await the result.
        //   This means no further requests will be read until the special request has completed.
        // - Otherwise, we will start a new task so that the receiver can continue.
        //   This means multiple URBs can be outstanding awaiting completion.
        //   The pending URBs can be completed out of order, but for each endpoint the replies must be sent in order.

        Pcap.DumpPacketNonIsoRequest(basic, submit, basic.direction == UsbIpDir.USBIP_DIR_OUT ? buf.AsSpan(payloadOffset) : ReadOnlySpan<byte>.Empty);

        Task ioctl;

        // Some special submits alter the device/driver state.
        // We need to await the completion before interpreting the next (possibly already queued) submit.

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
            _ = await Device.IoControlAsync(SUPUSB_IOCTL.USB_SET_CONFIG, StructToBytes(setConfig), null);
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
            _ = await Device.IoControlAsync(SUPUSB_IOCTL.USB_SELECT_INTERFACE, StructToBytes(selectInterface), null);
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
            _ = await Device.IoControlAsync(SUPUSB_IOCTL.USB_CLEAR_ENDPOINT, StructToBytes(clearEndpoint), null);
            ioctl = Task.CompletedTask;
        }
        else
        {
            // This is a normal URB, for which the completion is awaited asynchronously.
            if (buf.Length > 0)
            {
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
                _ = ioctl.ContinueWith(task =>
                {
                    gcHandle.Free();
                }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
            else
            {
                urb.buf = 0;
                StructToBytes(urb, bytes);
                ioctl = Device.IoControlAsync(SUPUSB_IOCTL.SEND_URB, bytes, bytes);
            }
        }

        // At this point we have initiated the ioctl (and possibly awaited it for special cases).
        // Now we schedule a continuation to create the response once the ioctl completes.

        var replyTask = ioctl.ContinueWith(RequestReply (task, _) =>
        {
            Interlocked.Decrement(ref InterlockedSubmits);

            BytesToStruct(bytes, out urb);

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
                    // USBIP documentation states that for non-ISO transfers this shall be -1.
                    // Linux accepts that, but some clients that use 0 in submit also expect 0 in return.
                    // So, we copy the request value to accommodate those while still being compliant for
                    // clients that follow the spec, i.e. if they submit -1 as documented we'll return that too.
                    number_of_packets = submit.number_of_packets,
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
            return new(basic.seqnum, replyStream.ToArray());
        }, cancellationToken, TaskScheduler.Default);

        // Now we queue the task that creates the response, so that all replies for a single
        // endpoint remain ordered.

        await EndpointChannel.Writer.WriteAsync(replyTask, cancellationToken);

        // We return to the caller, so that the next request can be handled. As a result, multiple requests
        // can be outstanding (either for the same, or for multiple endpoints). Requests are completed
        // asynchronously and in any order, but the replies for each endpoint are sent to the client in original order.
    }

    // Upon UNLINK, Linux expects the URB to actually be canceled (besides not wanting to know the SUBMIT result,
    // which is handled in AttachClient, see there).
    //
    // Windows is perfectly capable of canceling individual URBs, but the VBoxUSB driver is not. It simply does not
    // support CancelIo() for the IOCTL that submits URBs. What it *can* do is abort the entire pipe.
    // However, there are two downsides:
    // a) Abort is rather "heavy". It does more than just cancel all URBs. So, we don't want to do this if we
    //    don't have to. And certainly not more than necessary.
    // b) Linux does not expect (although it can handle it) many completion replies if it intends to cancel the URBs.
    //    What usually happens is that a Linux driver has multiple outstanding IOs for throughput. For example, on an
    //    input pipe they submit 2 to 5 read URBs. While one is completed, the next can immediately be filled while
    //    the driver processes the first URBs and "tops up" the read queue. Much like overlapped IO on Windows.
    //    Hence, a common sequence for canceling an input pipe is:
    //       SUBMIT (pending)
    //       SUBMIT (pending)
    //       UNLINK (unlinks and cancels the first)
    //       UNLINK (unlinks and cancels the second)
    //    Of course, there is a race condition. The read may actually be completed before the UNLINK is processed.
    //    In that case, Linux will log "the urb (seqnum XXXX) was already given back". This is perfectly normal.
    //    However, the most common case is that both URBs will *not* complete and be canceled and unlinked.
    //    If we would abort the pipe upon the first UNLINK received, then the second has a very high probability
    //    to complete with "aborted" before the second UNLINK request is processed. So for us, it would look like:
    //       SUBMIT (pending)
    //       SUBMIT (pending)
    //       UNLINK (unlinks the first, triggers abort)
    //       SUBMIT_REPLY (for the second, as the abort is local to Windows and very fast)
    //       UNLINK (fails with "too late", as this comes in later from remote over the TCP wire)
    //    Although this would work (i.e., it is acceptable behavior), it clutters the Linux log.
    //
    // Solution:
    // We keep a "pending submit" count for our endpoint. When we receive an UNLINK, but more submits are pending, then
    // we actually do not abort the pipe yet. We expect the other UNLINK(s) to follow shortly. We only abort the pipe
    // if we received as many UNLINKs as there are pending SUBMITs. And we don't abort at all if there are no pending
    // SUBMITs at all. The result is that the behavior on the client (Linux) side looks more like what is intended, with
    // a lot less "already given back" log lines.
    // The sequence now is:
    //       SUBMIT (pending)
    //       SUBMIT (pending)
    //       UNLINK (unlinks the first, does not trigger abort yet)
    //       UNLINK (unlinks the second, triggers abort)
    // This *does* assume that Linux will indeed UNLINK all URBs for the endpoint.
    //
    // Of course, if the SUBMIT completion wins the race, it is still possible that we abort more often than required.
    // This cannot be avoided, and is not a problem either. The normal scenario is handled optimally this way.
    // It is much better than abort for every UNLINK.

    public async Task HandleUnlinkAsync()
    {
        // NOTE: We sample the current value.
        // The interlocked PendingSubmits can only go down by completion at this time.
        // It can never go up, as all incrementors are synchronous with this thread.

        var pendingSubmits = Interlocked.Read(ref InterlockedSubmits);

        if (pendingSubmits == 0)
        {
            // Apparently, all pending submits won the race.
            Logger.Trace($"Unlinking: nothing pending");
            UnlinkHoldoffCount = 0;
            return;
        }

        // There are pending SUBMITs, but we only want to abort after receiving all UNLINKS first.
        ++UnlinkHoldoffCount;
        Logger.Trace($"Unlinking: PendingSubmits={pendingSubmits}, PendingUnlinks={UnlinkHoldoffCount}");
        if (UnlinkHoldoffCount >= pendingSubmits)
        {
            // NOTE: VBoxUSB does not support canceling individual URBs.
            var clearEndpoint = new UsbSupClearEndpoint()
            {
                bEndpoint = RawEndpoint,
            };
            // Just like for CLEAR_FEATURE, we are going to wait until this finishes,
            // in order to avoid races with subsequent SUBMIT to the same endpoint.
            Logger.Trace($"Aborting endpoint");
            await Device.IoControlAsync(SUPUSB_IOCTL.USB_ABORT_ENDPOINT, StructToBytes(clearEndpoint), null);

            UnlinkHoldoffCount = 0;
        }
    }
}
