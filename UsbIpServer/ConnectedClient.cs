// SPDX-FileCopyrightText: 2020 Frans van Dorsselaer, Microsoft Corporation
//
// SPDX-License-Identifier: GPL-2.0-only

using System;
using System.Buffers.Binary;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using static UsbIpServer.Interop.UsbIp;
using static UsbIpServer.Interop.VBoxUsb;
using static UsbIpServer.Interop.WinSDK;
using static UsbIpServer.Tools;

namespace UsbIpServer
{
    [SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Instantiated by DI")]
    sealed class ConnectedClient
    {
        public ConnectedClient(ILogger<ConnectedClient> logger, RegistryWatcher registryWatcher, DeviceChangeWatcher deviceChangeWatcher, ClientContext clientContext, IServiceProvider serviceProvider)
        {
            Logger = logger;
            ClientContext = clientContext;
            ServiceProvider = serviceProvider;
            RegistryWatcher = registryWatcher;
            DeviceChangeWatcher = deviceChangeWatcher;

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
        readonly RegistryWatcher RegistryWatcher;
        readonly DeviceChangeWatcher DeviceChangeWatcher;

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

        static async Task<ExportedDevice[]> GetSharedDevicesAsync(CancellationToken cancellationToken)
        {
            if (RegistryUtils.HasWriteAccess())
            {
                return (await ExportedDevice.GetAll(cancellationToken))
               .Where(x => RegistryUtils.IsDeviceShared(x))
               .ToArray();
            }

            return await ExportedDevice.GetAll(cancellationToken);
        }

        async Task HandleRequestDeviceListAsync(CancellationToken cancellationToken)
        {
            var exportedDevices = await GetSharedDevicesAsync(cancellationToken);

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
            BusId busId;
            {
                var buf = new byte[SYSFS_BUS_ID_SIZE];
                await RecvExactSizeAsync(Stream, buf, cancellationToken);
                if (!BusId.TryParse(Encoding.UTF8.GetString(buf).TrimEnd('\0'), out busId))
                {
                    await SendOpCodeAsync(OpCode.OP_REP_IMPORT, Status.ST_NODEV);
                    return;
                }
            }

            // whatever happens, try to report "something" to the client
            var status = Status.ST_ERROR;

            try
            {
                var exportedDevices = await GetSharedDevicesAsync(cancellationToken);
                var exportedDevice = exportedDevices.SingleOrDefault(x => x.BusId == busId);
                if (exportedDevice is null)
                {
                    await SendOpCodeAsync(OpCode.OP_REP_IMPORT, Status.ST_NODEV);
                    return;
                }

                if (RegistryUtils.IsDeviceAttached(exportedDevice))
                {
                    await SendOpCodeAsync(OpCode.OP_REP_IMPORT, Status.ST_DEV_BUSY);
                    return;
                }

                status = Status.ST_NA;
                using var mon = new VBoxUsbMon();
                await mon.CheckVersion();
                await mon.AddFilter(exportedDevice);
                await mon.RunFilters();
                {
                    // This enables exporting integrated USB devices (e.g. built-in webcams).
                    // VBoxMon will try to unplug/plug the device, but integrated USB devices are usually
                    // marked as not-removable. This means that Windows will not load the VBoxUSB driver.
                    // As a workaround, we tell the hub to powercycle the port, which has the same effect:
                    // Windows will re-enumerate the device and pick up the driver.
                    // If VBoxMon *is* able to do its normal unplug/plug cycle, then the port cycle command
                    // will probably fail due to a race condition. This is fine, as either way the VBoxUSB
                    // driver will take effect.
                    // We ignore any errors here; if both methods fail the error will be reported by
                    // ClaimDevice();
                    using var hubFile = new DeviceFile(exportedDevice.HubPath);
                    using var cancellationTokenRegistration = cancellationToken.Register(() => hubFile.Dispose());

                    var data = new UsbCyclePortParams() { ConnectionIndex = busId.Port };
                    var buf = StructToBytes(data);
                    try
                    {
                        await hubFile.IoControlAsync(IoControl.IOCTL_USB_HUB_CYCLE_PORT, buf, buf);
                    }
                    catch (Win32Exception) { }
                }
                ClientContext.AttachedDevice = await mon.ClaimDevice(exportedDevice);

                Logger.LogInformation(LogEvents.ClientAttach, $"Client {ClientContext.ClientAddress} claimed device at {exportedDevice.BusId} ({exportedDevice.Path}).");
                try
                {
                    ClientContext.ConfigurationDescriptors = exportedDevice.ConfigurationDescriptors;

                    status = Status.ST_DEV_ERR;
                    var cfg = new byte[1] { 0 };
                    await ClientContext.AttachedDevice.IoControlAsync(SUPUSB_IOCTL.USB_SET_CONFIG, cfg, null);

                    status = Status.ST_OK;
                    await SendOpCodeAsync(OpCode.OP_REP_IMPORT, Status.ST_OK);
                    exportedDevice.Serialize(Stream, false);

                    // setup token to free device
                    using var attachedClientTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    void cancelAction() {
                        attachedClientTokenSource.Cancel();
                    }
                    RegistryWatcher.WatchDevice(busId, cancelAction);
                    DeviceChangeWatcher.WatchForDeviceRemoval(busId, cancelAction);
                    var attachedClientToken = attachedClientTokenSource.Token;
                    RegistryUtils.SetDeviceAsAttached(exportedDevice, ClientContext.ClientAddress);

                    await ServiceProvider.GetRequiredService<AttachedClient>().RunAsync(attachedClientToken);
                }
                finally
                {
                    RegistryWatcher.StopWatchingDevice(busId);
                    DeviceChangeWatcher.StopWatchingDevice(busId);
                    RegistryUtils.SetDeviceAsDetached(exportedDevice);

                    Logger.LogInformation(LogEvents.ClientDetach, $"Client {ClientContext.ClientAddress} released device at {exportedDevice.BusId} ({exportedDevice.Path}).");
                }
            }
            catch (Exception ex)
            {
                // EndOfStream is client hang ups and OperationCanceled is detachments
                if (!(ex is EndOfStreamException || ex is OperationCanceledException))
                {
                    Logger.LogError(LogEvents.ClientError, $"An exception occurred while communicating with the client: {ex}");
                }
                
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
