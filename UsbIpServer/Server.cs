/*
    usbipd-win
    Copyright (C) 2020  Frans van Dorsselaer

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program.  If not, see <https://www.gnu.org/licenses/>.
*/

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
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

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var singleton = new Mutex(true, SingletonMutexName, out var createdNew);
            if (!createdNew)
            {
                throw new InvalidOperationException("Another instance is already running");
            }

            TcpListener.Start();

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
