// SPDX-FileCopyrightText: 2020 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-2.0-only

using System;
using System.Diagnostics.CodeAnalysis;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using static UsbIpServer.Interop.UsbIp;

namespace UsbIpServer
{
    [SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Instantiated by DI")]
    sealed class Server : BackgroundService
    {
        const string SingletonMutexName = @"Global\usbip-{A8256F62-728F-49B0-82BB-E5E48F83D28F}";

        public Server(ILogger<Server> logger, IServiceScopeFactory serviceScopeFactory)
        {
            Logger = logger;
            ServiceScopeFactory = serviceScopeFactory;
        }

        readonly ILogger Logger;
        readonly IServiceScopeFactory ServiceScopeFactory;
        readonly TcpListener TcpListener = TcpListener.Create(USBIP_PORT);

        public Task Run(CancellationToken stoppingToken)
        {
            return ExecuteAsync(stoppingToken);
        }

        public static bool IsServerRunning()
        {
            using var singleton = new Mutex(true, SingletonMutexName, out var createdNew);
            return !createdNew;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var mutex = new Mutex(true, SingletonMutexName, out var createdNew);
            if (!createdNew)
            {
                throw new InvalidOperationException("Another instance is already running.");
            }

            TcpListener.Start();

            // To start, all devices should not be marked as attached.
            var devices = await ExportedDevice.GetAll(CancellationToken.None);
            foreach (var device in devices) {
                RegistryUtils.SetDeviceAsDetached(device);
            }

            RegistryUtils.StopSharingTemporaryDevices();
            
            using var cancellationTokenRegistration = stoppingToken.Register(() => TcpListener.Stop());

            while (true)
            {
                var tcpClient = await TcpListener.AcceptTcpClientAsync();
                _ = Task.Run(async () =>
                {
                    Logger.LogDebug($"new connection from {tcpClient.Client.RemoteEndPoint}");
                    try
                    {
                        using var cancellationTokenRegistration = stoppingToken.Register(() => tcpClient.Close());
                        using var serviceScope = ServiceScopeFactory.CreateScope();
                        var clientContext = serviceScope.ServiceProvider.GetRequiredService<ClientContext>();
                        clientContext.TcpClient = tcpClient;
                        var connectedClient = serviceScope.ServiceProvider.GetRequiredService<ConnectedClient>();
                        await connectedClient.RunAsync(stoppingToken);
                    }
                    finally
                    {
                        Logger.LogDebug("connection closed");
                    }
                }, stoppingToken);
            }
        }
    }
}
