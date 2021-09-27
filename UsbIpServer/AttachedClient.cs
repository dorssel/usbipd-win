// SPDX-FileCopyrightText: 2020 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-2.0-only

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
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
    [SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Instantiated by DI")]
    sealed class AttachedClient
    {
        public AttachedClient(ILogger<AttachedClient> logger, ClientContext clientContext)
        {
            Logger = logger;
            ClientContext = clientContext;

            var tcpClient = clientContext.TcpClient;
            Stream = tcpClient.GetStream();

            Device = clientContext.AttachedDevice ?? throw new ArgumentException($"{nameof(ClientContext.AttachedDevice)} is null");
            ConfigurationDescriptors = clientContext.ConfigurationDescriptors ?? throw new ArgumentException($"{nameof(ClientContext.ConfigurationDescriptors)} is null");

            tcpClient.NoDelay = true;
        }

        readonly ILogger Logger;
        readonly ClientContext ClientContext;
        readonly NetworkStream Stream;
        readonly DeviceFile Device;
        readonly UsbConfigurationDescriptors ConfigurationDescriptors;
        readonly SemaphoreSlim WriteMutex = new(1);
        readonly object PendingSubmitsLock = new();
        /// <summary>
        /// Mapping from USBIP seqnum to raw USB endpoint number.
        /// </summary>
        readonly SortedDictionary<uint, byte> PendingSubmits = new();

        async Task HandleSubmitIsochronousAsync(UsbIpHeaderBasic basic, UsbIpHeaderCmdSubmit submit, CancellationToken cancellationToken)
        {
            var buf = new byte[submit.transfer_buffer_length];
            if (basic.direction == UsbIpDir.USBIP_DIR_OUT)
            {
                await RecvExactSizeAsync(Stream, buf, cancellationToken);
            }

            var isoPacketDescriptorSizeItemSize = Marshal.SizeOf<UsbIpIsoPacketDescriptor>();
            var isoBuf = new byte[submit.number_of_packets * isoPacketDescriptorSizeItemSize];
            await RecvExactSizeAsync(Stream, isoBuf, cancellationToken);
            var packetDescriptors = new UsbIpIsoPacketDescriptor[submit.number_of_packets];
            for (var i = 0; i < packetDescriptors.Length; ++i)
            {
                packetDescriptors[i].offset = BinaryPrimitives.ReadUInt32BigEndian(isoBuf.AsSpan(i * isoPacketDescriptorSizeItemSize));
                packetDescriptors[i].length = BinaryPrimitives.ReadUInt32BigEndian(isoBuf.AsSpan(i * isoPacketDescriptorSizeItemSize + 4));
                packetDescriptors[i].actual_length = BinaryPrimitives.ReadUInt32BigEndian(isoBuf.AsSpan(i * isoPacketDescriptorSizeItemSize + 8));
                packetDescriptors[i].status = BinaryPrimitives.ReadUInt32BigEndian(isoBuf.AsSpan(i * isoPacketDescriptorSizeItemSize + 12));
            }

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

            lock (PendingSubmitsLock)
            {
                // To support UNLINK, we must be able to abort the pipe that is used for this URB.
                // We need the raw USB endpoint number, i.e. including the high bit for input pipes.
                if (!PendingSubmits.TryAdd(basic.seqnum, (byte)(basic.ep | (basic.direction == UsbIpDir.USBIP_DIR_IN ? 0x80u : 0x00u))))
                {
                    throw new ProtocolViolationException($"duplicate sequence number {basic.seqnum}");
                }
                Logger.LogTrace($"Scheduled seqnum={basic.seqnum}, pending count = {PendingSubmits.Count}");
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
                _ = Task.WhenAll(ioctls).ContinueWith(async (task, state) =>
                {
                    using var writeLock = await Lock.CreateAsync(WriteMutex, cancellationToken);

                    // Now we are synchronous with the sender.

                    lock (PendingSubmitsLock)
                    {
                        // We are racing with possible UNLINK commands.
                        if (!PendingSubmits.Remove(basic.seqnum))
                        {
                            // Apparently, the client has already UNLINK-ed (canceled) the request; we're done.
                            Logger.LogTrace($"Completed seqnum={basic.seqnum} after UNLINK, pending count = {PendingSubmits.Count}");
                            return;
                        }
                    }

                    var retSubmit = new UsbIpHeaderRetSubmit()
                    {
                        status = -(int)Errno.SUCCESS,
                        actual_length = (int)packetDescriptors.Sum((pd) => pd.actual_length),
                        start_frame = submit.start_frame,
                        number_of_packets = submit.number_of_packets,
                        error_count = 0,
                    };

                    Logger.LogTrace($"ISO: error_count: {retSubmit.error_count}, actual_length: {retSubmit.actual_length}");

                    var retTransfer = buf;
                    if ((basic.direction == UsbIpDir.USBIP_DIR_IN) && (retSubmit.actual_length != submit.transfer_buffer_length))
                    {
                        // USBIP requires us to transfer the actual data without padding.
                        // So, in case we have a short read  we need to copy the data to a new "byte-stuffed" buffer.
                        retTransfer = new byte[retSubmit.actual_length];
                        var expectedOffset = 0;
                        var retOffset = 0;
                        for (var i = 0; i < packetDescriptors.Length; ++i)
                        {
                            if ((int)packetDescriptors[i].status != -(int)Errno.SUCCESS)
                            {
                                retSubmit.error_count++;
                            }
                            buf.AsSpan(expectedOffset, (int)packetDescriptors[i].actual_length).CopyTo(retTransfer.AsSpan(retOffset, (int)packetDescriptors[i].actual_length));
                            expectedOffset += (int)packetDescriptors[i].length;
                            retOffset += (int)packetDescriptors[i].actual_length;
                        }
                    }

                    var retBuf = new byte[48 /* sizeof(usbip_header) */];
                    BinaryPrimitives.WriteUInt32BigEndian(retBuf.AsSpan(0), (uint)UsbIpCmd.USBIP_RET_SUBMIT);
                    BinaryPrimitives.WriteUInt32BigEndian(retBuf.AsSpan(4), basic.seqnum);
                    BinaryPrimitives.WriteUInt32BigEndian(retBuf.AsSpan(8), 0);
                    BinaryPrimitives.WriteUInt32BigEndian(retBuf.AsSpan(12), 0);
                    BinaryPrimitives.WriteUInt32BigEndian(retBuf.AsSpan(16), 0);
                    BinaryPrimitives.WriteInt32BigEndian(retBuf.AsSpan(20), retSubmit.status);
                    BinaryPrimitives.WriteInt32BigEndian(retBuf.AsSpan(24), retSubmit.actual_length);
                    BinaryPrimitives.WriteInt32BigEndian(retBuf.AsSpan(28), retSubmit.start_frame);
                    BinaryPrimitives.WriteInt32BigEndian(retBuf.AsSpan(32), retSubmit.number_of_packets);
                    BinaryPrimitives.WriteInt32BigEndian(retBuf.AsSpan(36), retSubmit.error_count);
                    for (var i = 0; i < packetDescriptors.Length; ++i)
                    {
                        BinaryPrimitives.WriteUInt32BigEndian(isoBuf.AsSpan(i * isoPacketDescriptorSizeItemSize), packetDescriptors[i].offset);
                        BinaryPrimitives.WriteUInt32BigEndian(isoBuf.AsSpan(i * isoPacketDescriptorSizeItemSize + 4), packetDescriptors[i].length);
                        BinaryPrimitives.WriteUInt32BigEndian(isoBuf.AsSpan(i * isoPacketDescriptorSizeItemSize + 8), packetDescriptors[i].actual_length);
                        BinaryPrimitives.WriteUInt32BigEndian(isoBuf.AsSpan(i * isoPacketDescriptorSizeItemSize + 12), packetDescriptors[i].status);
                    }

                    await Stream.WriteAsync(retBuf, cancellationToken);
                    if (basic.direction == UsbIpDir.USBIP_DIR_IN)
                    {
                        await Stream.WriteAsync(retTransfer, cancellationToken);
                    }
                    await Stream.WriteAsync(isoBuf, cancellationToken);
                }, cancellationToken, TaskScheduler.Default);
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
            // We are synchronous with the receiver.

            var transferType = ConfigurationDescriptors.GetEndpointType(basic.ep, basic.direction == UsbIpDir.USBIP_DIR_IN);
            if (transferType == Constants.USB_ENDPOINT_TYPE_ISOCHRONOUS)
            {
                await HandleSubmitIsochronousAsync(basic, submit, cancellationToken);
                return;
            }

            var urb = new UsbSupUrb()
            {
                ep = basic.ep,
                dir = (basic.direction == UsbIpDir.USBIP_DIR_IN) ? UsbSupDirection.USBSUP_DIRECTION_IN : UsbSupDirection.USBSUP_DIRECTION_OUT,
                flags = (basic.direction == UsbIpDir.USBIP_DIR_IN)
                    ? (((submit.transfer_flags & 1) != 0) ? UsbSupXferFlags.USBSUP_FLAG_NONE : UsbSupXferFlags.USBSUP_FLAG_SHORT_OK)
                    : UsbSupXferFlags.USBSUP_FLAG_NONE,
                error = UsbSupError.USBSUP_XFER_OK,
                len = submit.transfer_buffer_length,
                numIsoPkts = (uint)submit.number_of_packets,
                aIsoPkts = new UsbSupIsoPkt[8],
            };

            var requestLength = submit.transfer_buffer_length;
            var payloadOffset = 0;
            switch (transferType)
            {
                case Constants.USB_ENDPOINT_TYPE_CONTROL:
                    urb.type = UsbSupTransferType.USBSUP_TRANSFER_TYPE_MSG;
                    payloadOffset = Marshal.SizeOf<USB_DEFAULT_PIPE_SETUP_PACKET>();
                    urb.len += (uint)payloadOffset;
                    break;
                case Constants.USB_ENDPOINT_TYPE_BULK:
                    urb.type = UsbSupTransferType.USBSUP_TRANSFER_TYPE_BULK;
                    break;
                case Constants.USB_ENDPOINT_TYPE_INTERRUPT:
                    urb.type = UsbSupTransferType.USBSUP_TRANSFER_TYPE_INTR;
                    break;
                default:
                    throw new UnexpectedResultException($"unknown endpoint type {transferType}");
            }

            var bytes = new byte[Marshal.SizeOf<UsbSupUrb>()];
            var buf = new byte[urb.len];

            if (transferType == Constants.USB_ENDPOINT_TYPE_CONTROL)
            {
                StructToBytes(submit.setup, buf);
            }

            if (basic.direction == UsbIpDir.USBIP_DIR_OUT)
            {
                await RecvExactSizeAsync(Stream, buf.AsMemory()[payloadOffset..], cancellationToken);
            }

            // We now have received the entire SUBMIT request:
            // - If the request is "special" (reconfig, clear), then we will handle it immediately and await the result.
            //   This means no further requests will be read until the special request has completed.
            // - Otherwise, we will start a new task so that the receiver can continue.
            //   This means multiple URBs can be outstanding awaiting completion.
            //   The pending URBs can be completed out of order, but the replies must be sent atomically.

            Task ioctl;
            // Boxed into a class, so we can use Interlocked with null.
            object? gcHandle = null;
            var pending = false;

            if ((basic.ep == 0)
                && (submit.setup.bmRequestType.B == Constants.BMREQUEST_TO_DEVICE)
                && (submit.setup.bRequest == Constants.USB_REQUEST_SET_CONFIGURATION))
            {
                // VBoxUsb needs this to get the endpoint handles
                var setConfig = new UsbSupSetConfig()
                {
                    bConfigurationValue = submit.setup.wValue.Anonymous.LowByte,
                };
                Logger.LogDebug($"Trapped SET_CONFIGURATION: {setConfig.bConfigurationValue}");
                await Device.IoControlAsync(SUPUSB_IOCTL.USB_SET_CONFIG, StructToBytes(setConfig), null);
                ioctl = Task.CompletedTask;
                ConfigurationDescriptors.SetConfiguration(setConfig.bConfigurationValue);
            }
            else if ((basic.ep == 0)
                && (submit.setup.bmRequestType.B == Constants.BMREQUEST_TO_INTERFACE)
                && (submit.setup.bRequest == Constants.USB_REQUEST_SET_INTERFACE))
            {
                // VBoxUsb needs this to get the endpoint handles
                var selectInterface = new UsbSupSelectInterface()
                {
                    bInterfaceNumber = submit.setup.wIndex.Anonymous.LowByte,
                    bAlternateSetting = submit.setup.wValue.Anonymous.LowByte,
                };
                Logger.LogDebug($"Trapped SET_INTERFACE: {selectInterface.bInterfaceNumber} -> {selectInterface.bAlternateSetting}");
                await Device.IoControlAsync(SUPUSB_IOCTL.USB_SELECT_INTERFACE, StructToBytes(selectInterface), null);
                ioctl = Task.CompletedTask;
                ConfigurationDescriptors.SetInterface(selectInterface.bInterfaceNumber, selectInterface.bAlternateSetting);
            }
            else if ((basic.ep == 0)
                && (submit.setup.bmRequestType.B == Constants.BMREQUEST_TO_ENDPOINT)
                && (submit.setup.bRequest == Constants.USB_REQUEST_CLEAR_FEATURE)
                && (submit.setup.wValue.W == 0))
            {
                // VBoxUsb needs this to notify the host controller
                var clearEndpoint = new UsbSupClearEndpoint()
                {
                    bEndpoint = submit.setup.wIndex.Anonymous.LowByte,
                };
                Logger.LogDebug($"Trapped CLEAR_FEATURE: {clearEndpoint.bEndpoint}");
                await Device.IoControlAsync(SUPUSB_IOCTL.USB_CLEAR_ENDPOINT, StructToBytes(clearEndpoint), null);
                ioctl = Task.CompletedTask;
            }
            else
            {
                if (transferType == Constants.USB_ENDPOINT_TYPE_CONTROL)
                {
                    Logger.LogTrace($"{submit.setup.bmRequestType.B} {submit.setup.bRequest} {submit.setup.wValue.W} {submit.setup.wIndex.W} {submit.setup.wLength}");
                }
                lock (PendingSubmitsLock)
                {
                    // To support UNLINK, we must be able to abort the pipe that is used for this URB.
                    // We need the raw USB endpoint number, i.e. including the high bit for input pipes.
                    if (!PendingSubmits.TryAdd(basic.seqnum, (byte)(basic.ep | (basic.direction == UsbIpDir.USBIP_DIR_IN ? 0x80u : 0x00u))))
                    {
                        throw new ProtocolViolationException($"duplicate sequence number {basic.seqnum}");
                    }
                    Logger.LogTrace($"Scheduled seqnum={basic.seqnum}, pending count = {PendingSubmits.Count}");
                }
                pending = true;
                // We need to keep the pinning alive until after the ioctl completes, but we must
                // also free it whatever happens.
                Interlocked.Exchange(ref gcHandle, GCHandle.Alloc(buf, GCHandleType.Pinned));
                try
                {
                    urb.buf = ((GCHandle?)gcHandle)!.Value.AddrOfPinnedObject();
                    StructToBytes(urb, bytes);
                    ioctl = Device.IoControlAsync(SUPUSB_IOCTL.SEND_URB, bytes, bytes);
                    _ = ioctl.ContinueWith((task) =>
                    {
                        // This acts as a "finally" clause *after* the ioctl completes.
                        ((GCHandle?)Interlocked.Exchange(ref gcHandle, null))?.Free();
                        return Task.CompletedTask;
                    }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                }
                catch
                {
#pragma warning disable CA1508 // Avoid dead conditional code (false positive)
                    ((GCHandle?)Interlocked.Exchange(ref gcHandle, null))?.Free();
#pragma warning restore CA1508 // Avoid dead conditional code
                    throw;
                }
            }

            // At this point we have initiated the ioctl (and possibly awaited it for special cases).
            // Now we schedule a continuation to write the reponse once the ioctl completes.
            // This is fire-and-forget; we'll return to the caller so it can already receive the next request.

            _ = ioctl.ContinueWith(async (task, state) =>
            {
                using var writeLock = await Lock.CreateAsync(WriteMutex, cancellationToken);

                // Now we are synchronous with the sender.

                if (pending)
                {
                    lock (PendingSubmitsLock)
                    {
                        // We are racing with possible UNLINK commands.
                        if (!PendingSubmits.Remove(basic.seqnum))
                        {
                            // Apparently, the client has already UNLINK-ed (canceled) the request; we're done.
                            Logger.LogTrace($"Completed seqnum={basic.seqnum} after UNLINK, pending count = {PendingSubmits.Count}");
                            return;
                        }
                    }
                    BytesToStruct(bytes, out urb);
                }

                var retSubmit = new UsbIpHeaderRetSubmit()
                {
                    status = -(int)ConvertError(urb.error),
                    actual_length = (int)urb.len,
                    start_frame = submit.start_frame,
                    number_of_packets = (int)urb.numIsoPkts,
                    error_count = 0,
                };

                if (transferType == Constants.USB_ENDPOINT_TYPE_CONTROL)
                {
                    retSubmit.actual_length = (retSubmit.actual_length > payloadOffset) ? (retSubmit.actual_length - payloadOffset) : 0;
                }

                if (urb.error != UsbSupError.USBSUP_XFER_OK)
                {
                    Logger.LogDebug($"{urb.error} -> {ConvertError(urb.error)} -> {retSubmit.status}");
                }
                Logger.LogTrace($"actual: {retSubmit.actual_length}, requested: {requestLength}");

                var retBuf = new byte[48 /* sizeof(usbip_header) */];
                BinaryPrimitives.WriteUInt32BigEndian(retBuf.AsSpan(0), (uint)UsbIpCmd.USBIP_RET_SUBMIT);
                BinaryPrimitives.WriteUInt32BigEndian(retBuf.AsSpan(4), basic.seqnum);
                BinaryPrimitives.WriteUInt32BigEndian(retBuf.AsSpan(8), 0);
                BinaryPrimitives.WriteUInt32BigEndian(retBuf.AsSpan(12), 0);
                BinaryPrimitives.WriteUInt32BigEndian(retBuf.AsSpan(16), 0);
                BinaryPrimitives.WriteInt32BigEndian(retBuf.AsSpan(20), retSubmit.status);
                BinaryPrimitives.WriteInt32BigEndian(retBuf.AsSpan(24), retSubmit.actual_length);
                BinaryPrimitives.WriteInt32BigEndian(retBuf.AsSpan(28), retSubmit.start_frame);
                BinaryPrimitives.WriteInt32BigEndian(retBuf.AsSpan(32), retSubmit.number_of_packets);
                BinaryPrimitives.WriteInt32BigEndian(retBuf.AsSpan(36), retSubmit.error_count);
                await Stream.WriteAsync(retBuf, cancellationToken);
                if (basic.direction == UsbIpDir.USBIP_DIR_IN)
                {
                    await Stream.WriteAsync(buf.AsMemory(payloadOffset, retSubmit.actual_length), cancellationToken);
                }
            }, cancellationToken, TaskScheduler.Default);
        }

        async Task HandleUnlinkAsync(UsbIpHeaderBasic basic, UsbIpHeaderCmdUnlink unlink, CancellationToken cancellationToken)
        {
            // We are synchronous with the receiver.

            var pending = false;
            byte endpoint;

            lock (PendingSubmitsLock)
            {
                pending = PendingSubmits.Remove(unlink.seqnum, out endpoint);
                Logger.LogTrace($"Unlinking {unlink.seqnum}, pending = {pending}, pending count = {PendingSubmits.Count}");
            }

            if (pending)
            {
                // VBoxUSB does not support canceling ioctls, so we will abort the pipe, which effectively cancels all URBs to that endpoint.
                // This is OK, since Linux will normally unlink all URBs anyway in quick succession.
                var clearEndpoint = new UsbSupClearEndpoint()
                {
                    bEndpoint = endpoint,
                };
                // Just like for CLEAR_FEATURE, we are going to wait until this finishes,
                // in order to avoid races with subsequent SUBMIT to the same endpoint.
                Logger.LogTrace($"Aborting endpoint {endpoint}");
                await Device.IoControlAsync(SUPUSB_IOCTL.USB_ABORT_ENDPOINT, StructToBytes(clearEndpoint), null);
            }

            using var writeLock = await Lock.CreateAsync(WriteMutex, cancellationToken);

            // Now we are synchronous with the sender.

            var retUnlink = new UsbIpHeaderRetUnlink()
            {
                status = -(int)(pending ? Interop.Linux.Errno.ECONNRESET : Interop.Linux.Errno.SUCCESS),
            };
            var retBuf = new byte[48 /* sizeof(usbip_header) */];
            BinaryPrimitives.WriteUInt32BigEndian(retBuf.AsSpan(0), (uint)UsbIpCmd.USBIP_RET_UNLINK);
            BinaryPrimitives.WriteUInt32BigEndian(retBuf.AsSpan(4), basic.seqnum);
            BinaryPrimitives.WriteUInt32BigEndian(retBuf.AsSpan(8), 0);
            BinaryPrimitives.WriteUInt32BigEndian(retBuf.AsSpan(12), 0);
            BinaryPrimitives.WriteUInt32BigEndian(retBuf.AsSpan(16), 0);
            BinaryPrimitives.WriteInt32BigEndian(retBuf.AsSpan(20), retUnlink.status);
            await Stream.WriteAsync(retBuf.AsMemory(), cancellationToken);
        }

        public async Task RunAsync(CancellationToken cancellationToken)
        {
            while (true)
            {
                var buf = new byte[48 /* sizeof(usbip_header) */];
                await RecvExactSizeAsync(Stream, buf, cancellationToken);
                var basic = new UsbIpHeaderBasic
                {
                    command = (UsbIpCmd)BinaryPrimitives.ReadUInt32BigEndian(buf),
                    seqnum = BinaryPrimitives.ReadUInt32BigEndian(buf.AsSpan(4)),
                    devid = BinaryPrimitives.ReadUInt32BigEndian(buf.AsSpan(8)),
                    direction = (UsbIpDir)BinaryPrimitives.ReadUInt32BigEndian(buf.AsSpan(12)),
                    ep = BinaryPrimitives.ReadUInt32BigEndian(buf.AsSpan(16)),
                };

                switch (basic.command)
                {
                    case UsbIpCmd.USBIP_CMD_SUBMIT:
                        var submit = new UsbIpHeaderCmdSubmit
                        {
                            transfer_flags = BinaryPrimitives.ReadUInt32BigEndian(buf.AsSpan(20)),
                            transfer_buffer_length = BinaryPrimitives.ReadUInt32BigEndian(buf.AsSpan(24)),
                            start_frame = BinaryPrimitives.ReadInt32BigEndian(buf.AsSpan(28)),
                            number_of_packets = BinaryPrimitives.ReadInt32BigEndian(buf.AsSpan(32)),
                            interval = BinaryPrimitives.ReadInt32BigEndian(buf.AsSpan(36)),
                            setup = BytesToStruct<USB_DEFAULT_PIPE_SETUP_PACKET>(buf.AsSpan(40)),
                        };
                        Logger.LogTrace($"USBIP_CMD_SUBMIT, seqnum={basic.seqnum}, flags={submit.transfer_flags}, length={submit.transfer_buffer_length}, ep={basic.ep}");
                        await HandleSubmitAsync(basic, submit, cancellationToken);
                        break;
                    case UsbIpCmd.USBIP_CMD_UNLINK:
                        var unlink = new UsbIpHeaderCmdUnlink
                        {
                            seqnum = BinaryPrimitives.ReadUInt32BigEndian(buf.AsSpan(20)),
                        };
                        Logger.LogTrace($"USBIP_CMD_UNLINK, seqnum={basic.seqnum}, unlink_seqnum={unlink.seqnum}");
                        await HandleUnlinkAsync(basic, unlink, cancellationToken);
                        break;
                    default:
                        throw new ProtocolViolationException($"unknown UsbIpCmd {basic.command}");
                }
            }
        }
    }
}
