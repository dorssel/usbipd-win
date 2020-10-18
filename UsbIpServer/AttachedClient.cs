using Microsoft.Extensions.Logging;
using System;
using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using static UsbIpServer.Interop.Usb;
using static UsbIpServer.Interop.UsbIp;
using static UsbIpServer.Interop.VBoxUsb;
using static UsbIpServer.Tools;

namespace UsbIpServer
{
    [SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Instantiated by DI")]
    sealed class AttachedClient
    {
        public AttachedClient(ILogger<ConnectedClient> logger, ClientContext clientContext)
        {
            Logger = logger;
            ClientContext = clientContext;

            var tcpClient = clientContext.TcpClient;
            Stream = tcpClient.GetStream();

            Device = clientContext.AttachedDevice ?? throw new ArgumentException($"{nameof(ClientContext.AttachedDevice)} == null");
            ConfigurationDescriptors = clientContext.ConfigurationDescriptors ?? throw new ArgumentException($"{nameof(ClientContext.ConfigurationDescriptors)} == null");

            tcpClient.NoDelay = true;
        }

        readonly ILogger Logger;
        readonly ClientContext ClientContext;
        readonly NetworkStream Stream;
        readonly DeviceFile Device;
        readonly UsbConfigurationDescriptors ConfigurationDescriptors;

        async Task HandleSubmitAsync(UsbIpHeaderBasic basic, UsbIpHeaderCmdSubmit submit, CancellationToken cancellationToken)
        {
            var transferType = ConfigurationDescriptors.GetEndpointType(basic.ep, basic.direction == UsbIpDir.USBIP_DIR_IN);

            var urb = new UsbSupUrb()
            {
                ep = basic.ep,
                dir = (basic.direction == UsbIpDir.USBIP_DIR_IN) ? UsbSupDirection.USBSUP_DIRECTION_IN : UsbSupDirection.USBSUP_DIRECTION_OUT,
                flags = (basic.direction == UsbIpDir.USBIP_DIR_IN) ? UsbSupXferFlags.USBSUP_FLAG_SHORT_OK : UsbSupXferFlags.USBSUP_FLAG_NONE,
                error = UsbSupError.USBSUP_XFER_OK,
                len = submit.transfer_buffer_length,
                numIsoPkts = (uint)submit.number_of_packets,
                aIsoPkts = new UsbSupIsoPkt[8],
            };

            var payloadOffset = 0;
            switch (transferType)
            {
                case UsbEndpointType.USB_ENDPOINT_TYPE_CONTROL:
                    urb.type = UsbSupTransferType.USBSUP_TRANSFER_TYPE_MSG;
                    payloadOffset = Marshal.SizeOf<UsbDefaultPipeSetupPacket>();
                    urb.len += (uint)payloadOffset;
                    break;
                case UsbEndpointType.USB_ENDPOINT_TYPE_BULK:
                    urb.type = UsbSupTransferType.USBSUP_TRANSFER_TYPE_BULK;
                    break;
                case UsbEndpointType.USB_ENDPOINT_TYPE_INTERRUPT:
                    throw new NotImplementedException("USB_ENDPOINT_TYPE_INTERRUPT");
                    // TODO: requires queuing reuests and handling USBIP_CMD_UNLINK
                    // urb.type = UsbSupTransferType.USBSUP_TRANSFER_TYPE_INTR;
                    // break;
                case UsbEndpointType.USB_ENDPOINT_TYPE_ISOCHRONOUS:
                    throw new NotImplementedException("USB_ENDPOINT_TYPE_ISOCHRONOUS");
            }

            var bytes = new byte[Marshal.SizeOf<UsbSupUrb>()];
            var buf = new byte[urb.len];

            if (transferType == UsbEndpointType.USB_ENDPOINT_TYPE_CONTROL)
            {
                StructToBytes(submit.setup, buf, 0);
            }

            if (basic.direction == UsbIpDir.USBIP_DIR_OUT)
            {
                await RecvExactSizeAsync(Stream, buf.AsMemory()[payloadOffset..], cancellationToken);
            }

            if (submit.number_of_packets != 0)
            {
                // TODO: ISO transfers
                throw new NotImplementedException("ISO transfers");
            }

            if ((basic.ep == 0) && (submit.setup.bmRequestType == 0) && (submit.setup.bRequest == UsbRequest.SET_CONFIGURATION))
            {
                // VBoxUsb needs this to get the endpoint handles
                var configurationValue = (byte)submit.setup.wValue;
                Logger.LogTrace($"Trapped SET_CONFIGURATION {configurationValue}");
                await Device.IoControlAsync(IoControl.SUPUSB_IOCTL_USB_SET_CONFIG, new byte[] { configurationValue }, null);
                ConfigurationDescriptors.SetConfiguration(configurationValue);
            }
            else
            {
                Logger.LogTrace($"{submit.setup.bmRequestType} {submit.setup.bRequest} {submit.setup.wValue} {submit.setup.wIndex} {submit.setup.wLength}");
                var gc = GCHandle.Alloc(buf, GCHandleType.Pinned);
                try
                {
                    urb.buf = gc.AddrOfPinnedObject();
                    StructToBytes(urb, bytes, 0);
                    await Device.IoControlAsync(IoControl.SUPUSB_IOCTL_SEND_URB, bytes, bytes);
                    BytesToStruct(bytes, 0, out urb);
                }
                finally
                {
                    gc.Free();
                }
            }

            basic.command = UsbIpCmd.USBIP_RET_SUBMIT;
            var retSubmit = new UsbIpHeaderRetSubmit()
            {
                status = (int)urb.error,
                actual_length = (int)urb.len,
                start_frame = submit.start_frame,
                number_of_packets = (int)urb.numIsoPkts,
                error_count = 0,
            };

            if (transferType == UsbEndpointType.USB_ENDPOINT_TYPE_CONTROL)
            {
                retSubmit.actual_length = (retSubmit.actual_length > payloadOffset) ? (retSubmit.actual_length - payloadOffset) : 0;
            }

            Logger.LogTrace($"actual: {retSubmit.actual_length}");
            var retBuf = new byte[48 /* sizeof(usbip_header) */];
            BinaryPrimitives.WriteUInt32BigEndian(retBuf.AsSpan(0), (uint)basic.command);
            BinaryPrimitives.WriteUInt32BigEndian(retBuf.AsSpan(4), basic.seqnum);
            BinaryPrimitives.WriteUInt32BigEndian(retBuf.AsSpan(8), basic.devid);
            BinaryPrimitives.WriteUInt32BigEndian(retBuf.AsSpan(12), (uint)basic.direction);
            BinaryPrimitives.WriteUInt32BigEndian(retBuf.AsSpan(16), basic.ep);
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
                            setup = BytesToStruct<UsbDefaultPipeSetupPacket>(buf, 40),
                        };
                        Logger.LogTrace($"USBIP_CMD_SUBMIT, seqnum={basic.seqnum}, flags={submit.transfer_flags}, length={submit.transfer_buffer_length}, ep={basic.ep}");
                        await HandleSubmitAsync(basic, submit, cancellationToken);
                        break;
                    case UsbIpCmd.USBIP_CMD_UNLINK:
#if true
                        throw new NotImplementedException();
#else
                        Logger.LogTrace($"USBIP_CMD_UNLINK, {basic.seqnum}");
                        var cmdUnlink = new UsbIpHeaderCmdUnlink
                        {
                            seqnum = BinaryPrimitives.ReadUInt32BigEndian(buf.AsSpan(20)),
                        };
                        // just return success, TODO
                        basic.command = UsbIpCmd.USBIP_RET_UNLINK;
                        var retUnlink = new UsbIpHeaderRetUnlink
                        {
                            status = 0,
                        };
                        var retBuf = new byte[48 /* sizeof(usbip_header) */];
                        BinaryPrimitives.WriteUInt32BigEndian(retBuf.AsSpan(0), (uint)basic.command);
                        BinaryPrimitives.WriteUInt32BigEndian(retBuf.AsSpan(4), basic.seqnum);
                        BinaryPrimitives.WriteUInt32BigEndian(retBuf.AsSpan(8), basic.devid);
                        BinaryPrimitives.WriteUInt32BigEndian(retBuf.AsSpan(12), (uint)basic.direction);
                        BinaryPrimitives.WriteUInt32BigEndian(retBuf.AsSpan(16), basic.ep);
                        BinaryPrimitives.WriteInt32BigEndian(retBuf.AsSpan(20), retUnlink.status);
                        await Stream.WriteAsync(retBuf.AsMemory(), cancellationToken);
                        break;
#endif
                    default:
                        throw new ProtocolViolationException($"unknown UsbIpCmd {basic.command}");
                }
            }
        }
    }
}
