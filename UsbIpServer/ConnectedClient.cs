// SPDX-FileCopyrightText: 2020 Frans van Dorsselaer
// SPDX-FileCopyrightText: Microsoft Corporation
//
// SPDX-License-Identifier: GPL-2.0-only

using System;
using System.Buffers.Binary;
using System.ComponentModel;
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
    sealed class ConnectedClient
    {
        public ConnectedClient(ILogger<ConnectedClient> logger, ClientContext clientContext, IServiceProvider serviceProvider)
        {
            Logger = logger;
            ClientContext = clientContext;
            ServiceProvider = serviceProvider;

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
                ulong filterId;
                {
                    try
                    {
                        // VBoxUsbMon SUPUSBFLT_IOCTL_RUN_FILTERS is not potent enough as it only cycles the port.
                        // Instead, we mark the device for removal, add the filter, and then mark the device ready again.
                        using var restartingDevice = new ConfigurationManager.RestartingDevice(exportedDevice.InstanceId);
                        filterId = await mon.AddFilter(exportedDevice);
                    }
                    catch (ConfigurationManagerException ex) when (ex.ConfigRet == CONFIGRET.CR_REMOVE_VETOED)
                    {
                        // The host is actively using the device.
                        status = Status.ST_DEV_BUSY;
                        throw;
                    }
                }
                (var vboxDevice, ClientContext.AttachedDevice) = await mon.ClaimDevice(exportedDevice);

                HCMNOTIFICATION notification = default;
                Logger.ClientAttach(ClientContext.ClientAddress, exportedDevice.BusId, exportedDevice.InstanceId);
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
                    using var cancelEvent = new AutoResetEvent(false);
                    ThreadPool.RegisterWaitForSingleObject(cancelEvent, (state, timedOut) =>
                    {
                        Logger.Debug("Unbind or unplug while attached");
                        try
                        {
                            attachedClientTokenSource.Cancel();
                        }
                        catch (ObjectDisposedException) { }
                    }, null, Timeout.Infinite, true);

                    // Detect unplug.
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
                        PInvoke.CM_Register_Notification(filter, (void*)cancelEvent.SafeWaitHandle.DangerousGetHandle(), static (notify, context, action, eventData, eventDataSize) =>
                        {
                            switch (action)
                            {
                                case CM_NOTIFY_ACTION.CM_NOTIFY_ACTION_DEVICEREMOVEPENDING:
                                case CM_NOTIFY_ACTION.CM_NOTIFY_ACTION_DEVICEREMOVECOMPLETE:
                                    PInvoke.SetEvent((HANDLE)(IntPtr)context);
                                    break;
                            }
                            return (uint)WIN32_ERROR.ERROR_SUCCESS;
                        }, out var nintNotification).ThrowOnError(nameof(PInvoke.CM_Register_Notification));
                        notification = (HCMNOTIFICATION)nintNotification;
                    }

                    // Detect unbind.
                    using var attachedKey = RegistryUtils.SetDeviceAsAttached(exportedDevice, ClientContext.ClientAddress);
                    var lresult = PInvoke.RegNotifyChangeKeyValue(attachedKey.Handle, false, Windows.Win32.System.Registry.REG_NOTIFY_FILTER.REG_NOTIFY_THREAD_AGNOSTIC, cancelEvent.SafeWaitHandle, true);
                    if (lresult != (int)WIN32_ERROR.ERROR_SUCCESS)
                    {
                        throw new Win32Exception(lresult, nameof(PInvoke.RegNotifyChangeKeyValue));
                    }

                    await ServiceProvider.GetRequiredService<AttachedClient>().RunAsync(attachedClientTokenSource.Token);
                }
                finally
                {
                    if (notification != default)
                    {
                        PInvoke.CM_Unregister_Notification(notification);
                    }
                    RegistryUtils.SetDeviceAsDetached(exportedDevice);

                    ClientContext.AttachedDevice.Dispose();

                    Logger.ClientDetach(ClientContext.ClientAddress, exportedDevice.BusId, exportedDevice.InstanceId);

                    try
                    {
                        // This solves the cases where VBoxUsbMon does not properly hand back the device to the host.
                        // Instead, we mark the device for removal, add the filter, and then mark the device ready again.
                        using var restartingDevice = new ConfigurationManager.RestartingDevice(vboxDevice.DeviceNode);
                        await mon.RemoveFilter(filterId);
                    }
                    catch (ConfigurationManagerException) { }
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
