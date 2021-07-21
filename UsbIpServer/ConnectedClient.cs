// SPDX-FileCopyrightText: 2020 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-2.0-only

using System;
using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using UsbIpServer.Interop;
using static UsbIpServer.Interop.UsbIp;
using static UsbIpServer.Tools;

namespace UsbIpServer
{
    [SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Instantiated by DI")]
    sealed class ConnectedClient
    {
        public ConnectedClient(ILogger<ConnectedClient> logger, RegistryWatcher watcher, ClientContext clientContext, IServiceProvider serviceProvider)
        {
            Logger = logger;
            ClientContext = clientContext;
            ServiceProvider = serviceProvider;
            Watcher = watcher;

            var client = clientContext.TcpClient;
            client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
            client.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, 1);
            client.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, 5);
            client.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, 10);

            Stream = client.GetStream();
        }

        readonly ILogger Logger;
        readonly ClientContext ClientContext;
        readonly IServiceProvider ServiceProvider;
        readonly NetworkStream Stream;
        readonly RegistryWatcher Watcher;

        public async Task RunAsync(CancellationToken cancellationToken)
        {
            var opCode = await RecvOpCodeAsync(cancellationToken);
            Logger.LogDebug($"Received opcode: {opCode}");
            switch (opCode)
            {
                case OpCode.OP_REQ_DEVLIST:
                    await HandleRequestDeviceListAsync(cancellationToken);
                    break;
                case OpCode.OP_REQ_IMPORT:
                    await HandleRequestImportAsync(cancellationToken);
                    break;
                default:
                    throw new ProtocolViolationException($"unexpected opcode {opCode}");
            }
        }

        static async Task<ExportedDevice[]> GetAvailableDevicesAsync(CancellationToken cancellationToken)
        {
            if (RegistryUtils.HasRegistryAccess())
            {
                return (await ExportedDevice.GetAll(cancellationToken))
               .Where(x => RegistryUtils.IsDeviceAvailable(x.BusId))
               .ToArray();
            }

            return await ExportedDevice.GetAll(cancellationToken);
        }

        async Task HandleRequestDeviceListAsync(CancellationToken cancellationToken)
        {
            var exportedDevices = await GetAvailableDevicesAsync(cancellationToken);

            await SendOpCodeAsync(OpCode.OP_REP_DEVLIST, Status.ST_OK);

            // reply count
            var buf = new byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(buf, (uint)exportedDevices.Length);
            await Stream.WriteAsync(buf, cancellationToken);

            foreach (var exportedDevice in exportedDevices)
            {
                exportedDevice.Serialize(Stream, true);
            }
        }

        async Task HandleRequestImportAsync(CancellationToken cancellationToken)
        {
            var buf = new byte[SYSFS_BUS_ID_SIZE];
            await RecvExactSizeAsync(Stream, buf, cancellationToken);
            var busid = Encoding.UTF8.GetString(buf).TrimEnd('\0');
            var status = Status.ST_ERROR;

            try
            {
                var exportedDevices = await GetAvailableDevicesAsync(cancellationToken);

                status = Status.ST_NODEV;
                var exportedDevice = exportedDevices.Single(x => x.BusId == busid);

                status = Status.ST_NA;
                using var mon = new VBoxUsbMon();
                await mon.CheckVersion();
                await mon.AddFilter(exportedDevice);
                await mon.RunFilters();
                status = Status.ST_DEV_BUSY;
                ClientContext.AttachedDevice = await mon.ClaimDevice(exportedDevice);

                Logger.LogInformation(LogEvents.ClientAttach, $"Client {ClientContext.TcpClient.Client.RemoteEndPoint} claimed device at {exportedDevice.BusId} ({exportedDevice.Path}).");
                try
                {
                    ClientContext.ConfigurationDescriptors = exportedDevice.ConfigurationDescriptors;

                    status = Status.ST_DEV_ERR;
                    var cfg = new byte[1] { 0 };
                    await ClientContext.AttachedDevice.IoControlAsync(VBoxUsb.IoControl.SUPUSB_IOCTL_USB_SET_CONFIG, cfg, null);

                    status = Status.ST_OK;
                    await SendOpCodeAsync(OpCode.OP_REP_IMPORT, Status.ST_OK);
                    exportedDevice.Serialize(Stream, false);

                    // setup token to free device
                    using var attachedClientTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    Watcher.WatchDevice(busid, () => attachedClientTokenSource.Cancel());
                    var attachedClientToken = attachedClientTokenSource.Token;
                    await ServiceProvider.GetRequiredService<AttachedClient>().RunAsync(attachedClientToken);
                }
                finally
                {
                    Watcher.StopWatchingDevice(busid);
                    Logger.LogInformation(LogEvents.ClientDetach, $"Client {ClientContext.TcpClient.Client.RemoteEndPoint} released device at {exportedDevice.BusId} ({exportedDevice.Path}).");
                }
            }
            catch
            {
#pragma warning disable CA1508 // Avoid dead conditional code (false possitive)
                if (status != Status.ST_OK)
#pragma warning restore CA1508 // Avoid dead conditional code
                {
                    await SendOpCodeAsync(OpCode.OP_REP_IMPORT, status);
                }
                throw;
            }
        }

        async Task<OpCode> RecvOpCodeAsync(CancellationToken cancellationToken)
        {
            var buf = new byte[8];
            await RecvExactSizeAsync(Stream, buf, cancellationToken);

            // unmarshal and validate
            var version = BinaryPrimitives.ReadUInt16BigEndian(buf.AsSpan(0));
            if (version != USBIP_VERSION)
            {
                throw new ProtocolViolationException($"version mismatch: expected {USBIP_VERSION >> 8}.{USBIP_VERSION & 0xff}, got {version >> 8}.{version & 0xff}");
            }

            var opCode = (OpCode)BinaryPrimitives.ReadUInt16BigEndian(buf.AsSpan(2));
            if (!Enum.IsDefined(typeof(OpCode), opCode))
            {
                throw new ProtocolViolationException($"illegal opcode: {(ushort)opCode}");
            }

            var status = (Status)BinaryPrimitives.ReadUInt32BigEndian(buf.AsSpan(4));
            if (!Enum.IsDefined(typeof(Status), status))
            {
                throw new ProtocolViolationException($"illegal status: {status}");
            }
            if (status != Status.ST_OK)
            {
                throw new ProtocolViolationException($"error status at peer: {status}");
            }

            return opCode;
        }

        async Task SendOpCodeAsync(OpCode opCode, Status status)
        {
            var buf = new byte[8];

            // marshal
            BinaryPrimitives.WriteUInt16BigEndian(buf, USBIP_VERSION);
            BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(2), (ushort)opCode);
            BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(4), (uint)status);

            await Stream.WriteAsync(buf);
        }
    }
}
