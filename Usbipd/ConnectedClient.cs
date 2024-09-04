// SPDX-FileCopyrightText: 2020 Frans van Dorsselaer
// SPDX-FileCopyrightText: Microsoft Corporation
//
// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using Usbipd.Automation;
using Windows.Win32;
using Windows.Win32.Devices.DeviceAndDriverInstallation;
using Windows.Win32.Foundation;

using static Usbipd.Interop.UsbIp;

namespace Usbipd;

sealed class ConnectedClient(ILogger<ConnectedClient> logger, ClientContext clientContext, IServiceProvider serviceProvider)
{
    readonly ILogger Logger = logger;
    readonly ClientContext ClientContext = clientContext;
    readonly IServiceProvider ServiceProvider = serviceProvider;
    readonly NetworkStream Stream = clientContext.TcpClient.GetStream();

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        try
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
        catch (Exception ex)
        {
            // EndOfStream is client hang ups and OperationCanceled is detachments;
            // neither are an error but part of normal functionality.
            if (!(ex is EndOfStreamException || ex is OperationCanceledException))
            {
                Logger.ClientError(ex);
            }
            throw;
        }
    }

    static IEnumerable<UsbDevice> GetDevicesAvailableForClient(IPAddress ipAddress)
    {
        return UsbDevice.GetAll().Where(device =>
        {
            if (!device.BusId.HasValue)
            {
                // The device is not currently plugged in.
                return false;
            }
            if (device.BusId.Value.IsIncompatibleHub)
            {
                // The hub the device is plugged into is incompatible.
                return false;
            }
            if (device.Guid.HasValue)
            {
                // Device is already bound, so available for attach.
                return true;
            }
            return Policy.IsAutoBindAllowed(device, ipAddress);
        });
    }

    async Task HandleRequestDeviceListAsync(CancellationToken cancellationToken)
    {
        var exportedDevices = new List<ExportedDevice>();
        foreach (var device in GetDevicesAvailableForClient(ClientContext.ClientAddress))
        {
            try
            {
                exportedDevices.Add(await ExportedDevice.GetExportedDevice(device, cancellationToken));
            }
            catch (ConfigurationManagerException) { }
            catch (Win32Exception) { }
        }

        await SendOpCodeAsync(OpCode.OP_REP_DEVLIST, Status.ST_OK);

        // reply count
        var buf = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(buf, (uint)exportedDevices.Count);
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
            await Stream.ReadMessageAsync(buf, cancellationToken);
            if (!BusId.TryParse(Encoding.UTF8.GetString(buf.TakeWhile(b => b != 0).ToArray()), out busId))
            {
                await SendOpCodeAsync(OpCode.OP_REP_IMPORT, Status.ST_NODEV);
                return;
            }
        }

        // whatever happens, try to report "something" to the client
        var status = Status.ST_ERROR;

        VBoxUsbMon? mon = null;
        try
        {
            {
                // Auto-bind if necessary *and* allowed.
                var bindDevice = GetDevicesAvailableForClient(ClientContext.ClientAddress).SingleOrDefault(d => d.BusId.HasValue && d.BusId.Value == busId);
                if (bindDevice is null)
                {
                    await SendOpCodeAsync(OpCode.OP_REP_IMPORT, Status.ST_NODEV);
                    return;
                }
                if (!bindDevice.Guid.HasValue)
                {
                    // The device is not currently bound, but it is allowed by the policy. Auto-bind it now...
                    Logger.AutoBind(ClientContext.ClientAddress, busId, bindDevice.InstanceId);
                    RegistryUtils.Persist(bindDevice.InstanceId, bindDevice.Description);
                }
            }

            var device = RegistryUtils.GetBoundDevices().SingleOrDefault(d => d.BusId.HasValue && d.BusId.Value == busId);
            if (device is null)
            {
                await SendOpCodeAsync(OpCode.OP_REP_IMPORT, Status.ST_NODEV);
                return;
            }
            Debug.Assert(device.BusId.HasValue);
            Debug.Assert(device.Guid.HasValue);

            if (device.IPAddress is not null)
            {
                await SendOpCodeAsync(OpCode.OP_REP_IMPORT, Status.ST_DEV_BUSY);
                return;
            }

            var exportedDevice = await ExportedDevice.GetExportedDevice(device, cancellationToken);

            status = Status.ST_NA;

            ulong filterId = 0;
            try
            {
                // We use the modern way to restart the device, which works much better than the obsolete VBoxUsbMon port cycling.
                using var restartingDevice = new ConfigurationManager.RestartingDevice(device.InstanceId);
                if (!device.IsForced)
                {
                    mon = new VBoxUsbMon();
                    var version = await mon.GetVersion();
                    if (!VBoxUsbMon.IsVersionSupported(version))
                    {
                        throw new NotSupportedException($"VBoxUsbMon version {version.major}.{version.minor} is not supported.");
                    }
                    filterId = await mon.AddFilter(exportedDevice);
                }
            }
            catch (ConfigurationManagerException ex) when (ex.ConfigRet == CONFIGRET.CR_REMOVE_VETOED)
            {
                // The host is actively using the device.
                status = Status.ST_DEV_BUSY;
                throw;
            }

            status = Status.ST_DEV_ERR;
            var sw = new Stopwatch();
            sw.Start();
            (var vboxDevice, ClientContext.AttachedDevice) = await VBoxUsb.ClaimDevice(device.BusId.Value);
            sw.Stop();
            Logger.Debug($"Claiming took {sw.ElapsedMilliseconds} ms");
            ClientContext.AttachedBusId = device.BusId;

            CM_Unregister_NotificationSafeHandle? notification = null;
            Logger.ClientAttach(ClientContext.ClientAddress, busId, device.InstanceId);
            try
            {
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
                unsafe // DevSkim: ignore DS172412
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
                                PInvoke.SetEvent((HANDLE)(nint)context);
                                break;
                        }
                        return (uint)WIN32_ERROR.ERROR_SUCCESS;
                    }, out notification).ThrowOnError(nameof(PInvoke.CM_Register_Notification));
                }

                // Detect unbind.
                using var attachedKey = RegistryUtils.SetDeviceAsAttached(device.Guid.Value, device.BusId.Value, ClientContext.ClientAddress, vboxDevice.InstanceId);
                var lresult = PInvoke.RegNotifyChangeKeyValue(attachedKey.Handle, false, Windows.Win32.System.Registry.REG_NOTIFY_FILTER.REG_NOTIFY_THREAD_AGNOSTIC, cancelEvent.SafeWaitHandle, true);
                if (lresult != WIN32_ERROR.ERROR_SUCCESS)
                {
                    throw new Win32Exception((int)lresult, nameof(PInvoke.RegNotifyChangeKeyValue));
                }

                await ServiceProvider.GetRequiredService<AttachedClient>().RunAsync(attachedClientTokenSource.Token);
            }
            finally
            {
                notification?.Dispose();

                RegistryUtils.SetDeviceAsDetached(device.Guid.Value);

                ClientContext.AttachedDevice.Dispose();

                Logger.ClientDetach(ClientContext.ClientAddress, busId, device.InstanceId);

                try
                {
                    // We use the modern way to restart the device, which works much better than the obsolete VBoxUsbMon port cycling.
                    using var restartingDevice = new ConfigurationManager.RestartingDevice(vboxDevice.DeviceNode);
                    if (mon is not null)
                    {
                        await mon.RemoveFilter(filterId);
                    }
                }
                catch (ConfigurationManagerException) { }
            }
        }
        catch
        {
            if (status != Status.ST_OK)
            {
                try
                {
                    // We'll do our best to report back to the client, but if that
                    // fails we'll throw the *original* exception.
                    await SendOpCodeAsync(OpCode.OP_REP_IMPORT, status);
                }
#pragma warning disable CA1031 // Do not catch general exception types
                catch { }
#pragma warning restore CA1031 // Do not catch general exception types
            }
            throw;
        }
        finally
        {
            mon?.Dispose();
        }
    }

    async Task<OpCode> RecvOpCodeAsync(CancellationToken cancellationToken)
    {
        var buf = new byte[8];
        await Stream.ReadMessageAsync(buf, cancellationToken);

        // marshal and validate
        var version = BinaryPrimitives.ReadUInt16BigEndian(buf.AsSpan(0));
        if (version != USBIP_VERSION)
        {
            throw new ProtocolViolationException($"USB/IP protocol version mismatch: expected {USBIP_VERSION.UsbIpVersionToVersion()}, got {version.UsbIpVersionToVersion()}");
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
