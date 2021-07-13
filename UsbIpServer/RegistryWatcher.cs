using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;

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

        private void HandleEvent(object sender, EventArrivedEventArgs e)
        {
            // something change in the registry, so check if we should unbind device
            var devicesToUnbind = RegistryUtils.GetRegistryDevices().Where(x => !x.IsAvailable);
            foreach (var device in devicesToUnbind)
            {
                if (devices.ContainsKey(device.BusId))
                {
                    devices[device.BusId]();
                    devices.Remove(device.BusId);
                }
            }
        }

        public void WatchDevice(string busId, Action cancellationAction)
        {
            devices[busId] = cancellationAction;
        }

        public void Dispose()
        {
            watcher?.Dispose();
        }
    }
}
