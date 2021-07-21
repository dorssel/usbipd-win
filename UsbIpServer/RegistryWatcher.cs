// SPDX-FileCopyrightText: Microsoft Corporation
//
// SPDX-License-Identifier: GPL-2.0-only

using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Threading;

namespace UsbIpServer
{
    sealed public class RegistryWatcher : IDisposable
    {
        private readonly ManagementEventWatcher? watcher;

        private readonly Dictionary<string, Action> devices = new Dictionary<string, Action>();

        public RegistryWatcher()
        {
            var query = @"SELECT * FROM RegistryTreeChangeEvent " +
                @"WHERE Hive='HKEY_LOCAL_MACHINE' " +
                @"AND RootPath='SOFTWARE'";

            watcher = new ManagementEventWatcher(query);
            watcher.EventArrived +=
                new EventArrivedEventHandler(HandleEvent);
            watcher.Start();
        }

        private async void HandleEvent(object sender, EventArrivedEventArgs e)
        {
            // something changed in the registry, so check if we should unbind device
            var connectedDevices = await ExportedDevice.GetAll(CancellationToken.None);
            var devicesToUnbind = connectedDevices.Where(x => !RegistryUtils.IsDeviceAvailable(x.BusId));
            foreach (var device in devicesToUnbind)
            {
                if (devices.ContainsKey(device.BusId))
                {
                    devices[device.BusId]();
                    StopWatchingDevice(device.BusId);
                }
            }
        }

        public void WatchDevice(string busId, Action cancellationAction)
        {
            devices[busId] = cancellationAction;
        }

        public void StopWatchingDevice(string busId)
        {
            devices.Remove(busId);
        }

        public void Dispose()
        {
            watcher?.Dispose();
        }
    }
}
