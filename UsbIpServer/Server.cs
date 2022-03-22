// SPDX-FileCopyrightText: 2020 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-2.0-only

using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Windows.Win32;
using Windows.Win32.Security;

using static UsbIpServer.Interop.UsbIp;

namespace UsbIpServer
{
    sealed class Server : BackgroundService
    {
        public const string SingletonMutexName = @"Global\usbipd-{A8256F62-728F-49B0-82BB-E5E48F83D28F}";

        public Server(ILogger<Server> logger, IServiceScopeFactory serviceScopeFactory, PcapNg _)
        {
            Logger = logger;
            ServiceScopeFactory = serviceScopeFactory;
        }

        readonly ILogger Logger;
        readonly IServiceScopeFactory ServiceScopeFactory;
        readonly TcpListener TcpListener = TcpListener.Create(USBIP_PORT);

        public static bool IsRunning()
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

        static void EnablePrivilege(string name)
        {
            var tokenPrivileges = new TOKEN_PRIVILEGES
            {
                PrivilegeCount = 1,
            };
            PInvoke.LookupPrivilegeValue(null, name, out tokenPrivileges.Privileges[0].Luid).ThrowOnError(nameof(PInvoke.LookupPrivilegeValue));
            tokenPrivileges.Privileges[0].Attributes = TOKEN_PRIVILEGES_ATTRIBUTES.SE_PRIVILEGE_ENABLED;

            using var currentProcess = PInvoke.GetCurrentProcess_SafeHandle();
            PInvoke.OpenProcessToken(currentProcess, TOKEN_ACCESS_MASK.TOKEN_ADJUST_PRIVILEGES, out var token).ThrowOnError(nameof(PInvoke.OpenProcessToken));
            try
            {
                unsafe
                {
                    PInvoke.AdjustTokenPrivileges(token, false, tokenPrivileges, 0, null, null).ThrowOnError(nameof(PInvoke.AdjustTokenPrivileges));
                }
            }
            finally
            {
                token.Dispose();
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            Logger.Debug(GitVersionInformation.InformationalVersion);

            // Cleanup any left-overs in case the previous instance crashed.
            RegistryUtils.SetAllDevicesAsDetached();

            // Non-interactive services have this disabled by default.
            // We require it so the ConfigurationManager can change the driver.
            EnablePrivilege("SeLoadDriverPrivilege");

            TcpListener.Start();
            while (true)
            {
                var tcpClient = await TcpListener.AcceptTcpClientAsync(stoppingToken);
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
