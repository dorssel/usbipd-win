// SPDX-FileCopyrightText: 2020 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-2.0-only

using System;
using System.Diagnostics.CodeAnalysis;
using System.Net;
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
        public const string SingletonMutexName = @"Global\usbipd-{A8256F62-728F-49B0-82BB-E5E48F83D28F}";

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
            try
            {
                using var mutex = Mutex.OpenExisting(SingletonMutexName);
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                // The mutex exists nevertheless.
                return true;
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                return false;
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            TcpListener.Start();

            // To start, all devices should not be marked as attached.
            var devices = await ExportedDevice.GetAll(CancellationToken.None);
            foreach (var device in devices) {
                RegistryUtils.SetDeviceAsDetached(device);
            }

            using var cancellationTokenRegistration = stoppingToken.Register(() => TcpListener.Stop());

            while (true)
            {
                var tcpClient = await TcpListener.AcceptTcpClientAsync();
                var clientAddress = (tcpClient.Client.RemoteEndPoint as IPEndPoint)!.Address;
                if (clientAddress.IsIPv4MappedToIPv6)
                {
                    clientAddress = clientAddress.MapToIPv4();
                }

                _ = Task.Run(async () =>
                {
                    Logger.Debug($"new connection from {clientAddress}");
                    try
                    {
                        using var cancellationTokenRegistration = stoppingToken.Register(() => tcpClient.Close());
                        using var serviceScope = ServiceScopeFactory.CreateScope();
                        var clientContext = serviceScope.ServiceProvider.GetRequiredService<ClientContext>();
                        clientContext.TcpClient = tcpClient;
                        clientContext.ClientAddress = clientAddress;
                        var connectedClient = serviceScope.ServiceProvider.GetRequiredService<ConnectedClient>();
                        await connectedClient.RunAsync(stoppingToken);
                    }
                    finally
                    {
                        Logger.Debug("connection closed");
                    }
                }, stoppingToken);
            }
        }
    }
}
