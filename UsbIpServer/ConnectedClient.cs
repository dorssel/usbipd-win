// SPDX-FileCopyrightText: 2020 Frans van Dorsselaer, Microsoft Corporation
//
// SPDX-License-Identifier: GPL-2.0-only

using System;
using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Windows.Win32;
using Windows.Win32.Devices.DeviceAndDriverInstallation;
using Windows.Win32.Foundation;

using static UsbIpServer.Interop.UsbIp;
using static UsbIpServer.Interop.VBoxUsb;

namespace UsbIpServer
{
    [SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Instantiated by DI")]
    sealed class ConnectedClient
    {
        public ConnectedClient(ILogger<ConnectedClient> logger, RegistryWatcher registryWatcher, ClientContext clientContext, IServiceProvider serviceProvider)
        {
            Logger = logger;
            ClientContext = clientContext;
            ServiceProvider = serviceProvider;
            RegistryWatcher = registryWatcher;

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

        public async Task RunAsync(CancellationToken cancellationToken)
        {
            var opCode = await RecvOpCodeAsync(cancellationToken);
            Logger.Debug($"Received opcode: {opCode}");
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
                await Stream.ReadExactlyAsync(buf, cancellationToken);
                if (!BusId.TryParse(Encoding.UTF8.GetString(buf.TakeWhile(b => b != 0).ToArray()), out busId))
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
                try
                {
                    // This enables exporting integrated USB devices (e.g. built-in webcams).
                    // VBoxMon will try to cycle the USB port, but sometimes this is not enough.
                    // In such cases, Windows will not detect the device change and will not load the VBoxUsb driver.
                    // As a workaround, we disable/enable the original device, which has the same effect:
                    // Windows will re-enumerate the device and load the VBoxUsb the driver.
                    // If VBoxUsbMon was able to do its normal port cycle command, this extra enable/disable
                    // will fail as the original device is already gone. This is fine, as either way the VBoxUsb
                    // driver will take effect.
                    // We ignore any errors here; if both methods fail the error will be reported by ClaimDevice().
                    ConfigurationManager.RestartDevice(exportedDevice.Path);
                }
                catch (ConfigurationManagerException) { }
                ClientContext.AttachedDevice = await mon.ClaimDevice(exportedDevice);

                HCMNOTIFICATION notification = default;
                Logger.ClientAttach(ClientContext.ClientAddress, exportedDevice.BusId, exportedDevice.Path);
                try
                {
                    status = Status.ST_DEV_ERR;
                    var cfg = new byte[1] { 0 };
                    await ClientContext.AttachedDevice.IoControlAsync(SUPUSB_IOCTL.USB_SET_CONFIG, cfg, null);

                    status = Status.ST_OK;
                    await SendOpCodeAsync(OpCode.OP_REP_IMPORT, Status.ST_OK);
                    exportedDevice.Serialize(Stream, false);

                    // setup token to free device
                    using var attachedClientTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    void cancelAction()
                    {
                        attachedClientTokenSource.Cancel();
                    }
                    RegistryWatcher.WatchDevice(busId, cancelAction);

                    // Detect device removal.
                    unsafe
                    {
                        CM_NOTIFY_FILTER filter = new()
                        {
                            cbSize = (uint)Marshal.SizeOf<CM_NOTIFY_FILTER>(),
                            FilterType = CM_NOTIFY_FILTER_TYPE.CM_NOTIFY_FILTER_TYPE_DEVICEHANDLE,
                            u = {
                                DeviceHandle =
                                {
                                    hTarget = ClientContext.AttachedDevice.DangerousGetHandle(),
                                },
                            },
                        };
                        PInvoke.CM_Register_Notification(filter, null, (notify, context, action, eventData, eventDataSize) =>
                        {
                            switch (action)
                            {
                                case CM_NOTIFY_ACTION.CM_NOTIFY_ACTION_DEVICEREMOVEPENDING:
                                case CM_NOTIFY_ACTION.CM_NOTIFY_ACTION_DEVICEREMOVECOMPLETE:
                                    Logger.Debug("Device removal detected");
                                    attachedClientTokenSource.Cancel();
                                    break;
                            }
                            return (uint)WIN32_ERROR.ERROR_SUCCESS;
                        }, out var nintNotification).ThrowOnError(nameof(PInvoke.CM_Register_Notification));
                        notification = (HCMNOTIFICATION)nintNotification;
                    }

                    var attachedClientToken = attachedClientTokenSource.Token;
                    RegistryUtils.SetDeviceAsAttached(exportedDevice, ClientContext.ClientAddress);

                    await ServiceProvider.GetRequiredService<AttachedClient>().RunAsync(attachedClientToken);
                }
                finally
                {
                    RegistryWatcher.StopWatchingDevice(busId);
                    if (notification != default)
                    {
                        PInvoke.CM_Unregister_Notification(notification);
                    }
                    RegistryUtils.SetDeviceAsDetached(exportedDevice);

                    Logger.ClientDetach(ClientContext.ClientAddress, exportedDevice.BusId, exportedDevice.Path);
                }
            }
            catch (Exception ex)
            {
                // EndOfStream is client hang ups and OperationCanceled is detachments
                if (!(ex is EndOfStreamException || ex is OperationCanceledException))
                {
                    Logger.ClientError(ex);
                }

#pragma warning disable CA1508 // Avoid dead conditional code (false positive)
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
            await Stream.ReadExactlyAsync(buf, cancellationToken);

            // marshal and validate
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
