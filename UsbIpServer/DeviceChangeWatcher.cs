// SPDX-FileCopyrightText: Copyright (c) Microsoft Corporation
//
// SPDX-License-Identifier: GPL-2.0-only

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Management;
using System.Threading;
using System.Threading.Tasks;

namespace UsbIpServer
{
    [SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Instantiated by DI")]
    sealed class DeviceChangeWatcher : IDisposable
    {
        readonly ManagementEventWatcher watcher;
        readonly SemaphoreSlim deviceLock = new(1);
        SortedSet<BusId>? lastKnownBusIds;

        // Mapping of bus IDs to actions to take on device removal.
        readonly Dictionary<BusId, Action> removalActions = new();

        public DeviceChangeWatcher()
        {
            var query = @"SELECT * FROM Win32_SystemConfigurationChangeEvent";

            // We're not in an async context here, so start a task to initialize the
            // list of known bus IDs and then forget about it. The task won't overwrite
            // existing values if it runs too late.
            Task.Run(async () =>
            {
                var busIds = await GetAllBusIdsAsync(CancellationToken.None);

                await deviceLock.WaitAsync();
                try
                {
                    lastKnownBusIds ??= busIds;
                }
                finally
                {
                    deviceLock.Release();
                }
            });

            watcher = new ManagementEventWatcher(query);
            watcher.EventArrived += HandleEvent;
            watcher.Start();
        }

        async void HandleEvent(object sender, EventArrivedEventArgs e)
        {
            try
            {
                var actions = new List<Action>();
                await deviceLock.WaitAsync();
                try
                {
                    var removedDevices = await GetRemovedDevicesAsync(CancellationToken.None);

                    foreach (var device in removedDevices)
                    {
                        if (removalActions.ContainsKey(device))
                        {
                            actions.Add(removalActions[device]);
                            removalActions.Remove(device);
                        }
                    }
                }
                finally
                {
                    deviceLock.Release();
                }

                foreach (var action in actions)
                {
                    action.Invoke();
                }
            }
            catch (ObjectDisposedException)
            {
                // When stopping the server, which disposes this object including deviceLock,
                // events may have already been queued. We just ignore that we lost the race.
            }
        }

        public void WatchForDeviceRemoval(BusId busId, Action removalAction)
        {
            deviceLock.Wait();
            try
            {
                removalActions[busId] = removalAction;
            }
            finally
            {
                deviceLock.Release();
            }
        }

        public void StopWatchingDevice(BusId busId)
        {
            deviceLock.Wait();
            try
            {
                removalActions.Remove(busId);
            }
            finally
            {
                deviceLock.Release();
            }
        }

        bool IsDisposed;
        public void Dispose()
        {
            if (!IsDisposed)
            {
                // NOTE: Our handler may still be in a queue, so it can still be called after Dispose()
                watcher.EventArrived -= HandleEvent;
                watcher.Dispose();
                deviceLock.Dispose();
                IsDisposed = true;
            }
        }

        private async Task<SortedSet<BusId>> GetRemovedDevicesAsync(CancellationToken cancellationToken)
        {
            var newBusIds = await GetAllBusIdsAsync(cancellationToken);
            lastKnownBusIds?.ExceptWith(newBusIds);

            var removedDevices = lastKnownBusIds;
            lastKnownBusIds = newBusIds;
            return removedDevices ?? new();
        }

        private static async Task<SortedSet<BusId>> GetAllBusIdsAsync(CancellationToken cancellationToken)
        {
            return new((await ExportedDevice.GetAll(cancellationToken)).Select(device => device.BusId));
        }
    }
}
