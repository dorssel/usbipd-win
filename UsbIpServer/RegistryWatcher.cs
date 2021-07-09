using System;
using System.Management;

namespace UsbIpServer
{
    sealed public class RegistryWatcher : IDisposable
    {
        private ManagementEventWatcher? watcher;

        public void AddHandler(EventHandler eventHandler)
        {
            var query = @"SELECT * FROM RegistryTreeChangeEvent " +
                @"WHERE Hive='HKEY_LOCAL_MACHINE' " +
                @"AND RootPath='SOFTWARE'";

            watcher = new ManagementEventWatcher(query);
            watcher.EventArrived +=
                new EventArrivedEventHandler(eventHandler);
            watcher.Start();
        }

        public void Dispose()
        {
            watcher?.Dispose();
        }
    }
}
