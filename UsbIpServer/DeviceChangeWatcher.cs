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
                Interlocked.CompareExchange(ref lastKnownBusIds, busIds, null);
            });

            watcher = new ManagementEventWatcher(query);
            watcher.EventArrived += HandleEvent;
            watcher.Start();
        }

        async void HandleEvent(object sender, EventArrivedEventArgs e)
        {
            var removedDevices = await GetRemovedDevicesAsync(CancellationToken.None);

            foreach (var device in removedDevices)
            {
                if (removalActions.ContainsKey(device))
                {
                    removalActions[device]();
                    StopWatchingDevice(device);
                }
            }
        }

        public void WatchForDeviceRemoval(BusId busId, Action removalAction)
        {
            removalActions[busId] = removalAction;
        }

        public void StopWatchingDevice(BusId busId)
        {
            removalActions.Remove(busId);
        }

        bool IsDisposed;
        public void Dispose()
        {
            if (!IsDisposed)
            {
                watcher.EventArrived -= HandleEvent;
                watcher.Dispose();
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
