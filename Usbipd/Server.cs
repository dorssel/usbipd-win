// SPDX-FileCopyrightText: 2020 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-2.0-only

using System;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Windows.Win32;
using Windows.Win32.Networking.WinSock;
using Windows.Win32.Security;

using static Usbipd.Interop.UsbIp;

namespace Usbipd;

sealed class Server : BackgroundService
{
    public const string SingletonMutexName = @"Global\usbipd-{A8256F62-728F-49B0-82BB-E5E48F83D28F}";

    public Server(ILogger<Server> logger, IConfiguration config, IServiceScopeFactory serviceScopeFactory, PcapNg _)
    {
        Logger = logger;
        Configuration = config;
        ServiceScopeFactory = serviceScopeFactory;
    }

    readonly ILogger Logger;
    readonly IConfiguration Configuration;
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
        PInvoke.LookupPrivilegeValue(null, name, out tokenPrivileges.Privileges.AsSpan()[0].Luid).ThrowOnError(nameof(PInvoke.LookupPrivilegeValue));
        tokenPrivileges.Privileges.AsSpan()[0].Attributes = TOKEN_PRIVILEGES_ATTRIBUTES.SE_PRIVILEGE_ENABLED;

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

        // All client sockets will inherit these. Formally, these options have to
        // be set before the socket reaches connected state, including Accept().
#if false
        TcpListener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
        try
        {
            // NOTE: This way of settings keepalive options only exists from Windows 10 1709 onward.
            TcpListener.Server.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, 1 /* s */);
            TcpListener.Server.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, 10 /* s */);
            TcpListener.Server.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, 5);
        }
        catch (SocketException)
#endif
        {
            // This always works (on Windows, not on Linux, but we are Windows-only anyway).
            // It is required to support pre-Windows 10 1709.
            //
            // NOTE:
            // TcpKeepAliveRetryCount cannot be configured this way at all. It is fixed at 10, so we use a default of 500 ms.
            // This ensures the best compatibility with the original values: 10 seconds delay before
            // keepalives are sent at all, followed by 5 seconds of retry.

            if (!uint.TryParse(Configuration["usbipd:TcpKeepAliveInterval"], out var tcpKeepAliveInterval))
            {
                tcpKeepAliveInterval = 500; /* ms, default */
            }
            if (!uint.TryParse(Configuration["usbipd:TcpKeepAliveTime"], out var tcpKeepAliveTime))
            {
                tcpKeepAliveTime = 10_000; /* ms, default */
            }
            Logger.Debug($"usbipd:TcpKeepAliveInterval = {tcpKeepAliveInterval} ms");
            Logger.Debug($"usbipd:TcpKeepAliveTime = {tcpKeepAliveTime} ms");

            var keepAlive = new tcp_keepalive()
            {
                onoff = 1,
                keepaliveinterval = tcpKeepAliveInterval,
                keepalivetime = tcpKeepAliveTime,
            };
            TcpListener.Server.IOControl(IOControlCode.KeepAliveValues, MemoryMarshal.AsBytes(new[] { keepAlive }.AsSpan()).ToArray(), null);
        }

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
                catch (Exception ex)
                {
                    Logger.Debug($"connection close: {ex.Message}");
                    throw;
                }
                finally
                {
                    Logger.Debug("connection closed");
                }
            }, stoppingToken);
        }
    }
}
