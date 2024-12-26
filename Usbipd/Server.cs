// SPDX-FileCopyrightText: 2020 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

using System.Net;
using System.Net.Sockets;
using Windows.Win32;
using Windows.Win32.Security;

using static Usbipd.Interop.UsbIp;

namespace Usbipd;

sealed partial class Server : BackgroundService
{
    public const string SingletonMutexName = @"Global\usbipd-{A8256F62-728F-49B0-82BB-E5E48F83D28F}";

    public Server(ILogger<Server> logger, IConfiguration config, IServiceScopeFactory serviceScopeFactory, PcapNg _)
    {
        Logger = logger;
        Configuration = config;
        ServiceScopeFactory = serviceScopeFactory;
        if (!ushort.TryParse(Configuration["usbipd:Port"], out var port))
        {
            port = USBIP_PORT;
        }
        Logger.Debug($"usbipd:Port = {port}");
        TcpListener = TcpListener.Create(port);
    }

    readonly ILogger Logger;
    readonly IConfiguration Configuration;
    readonly IServiceScopeFactory ServiceScopeFactory;
    readonly TcpListener TcpListener;

    public override void Dispose()
    {
        TcpListener.Dispose();
        base.Dispose();
    }

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
        using (token)
        {
            unsafe // DevSkim: ignore DS172412
            {
                PInvoke.AdjustTokenPrivileges(token, false, &tokenPrivileges, 0, null, null).ThrowOnError(nameof(PInvoke.AdjustTokenPrivileges));
            }
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Logger.Debug(GitVersionInformation.InformationalVersion);

        // Cleanup any left-overs in case the previous instance crashed.
        _ = RegistryUtilities.SetAllDevicesAsDetached();

        // Non-interactive services have this disabled by default.
        // We require it so the ConfigurationManager can change the driver.
        EnablePrivilege("SeLoadDriverPrivilege");

        // All client sockets will inherit these. Formally, these options have to
        // be set before the socket reaches connected state, including Accept().
        TcpListener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
        TcpListener.Server.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, 1 /* s */);
        TcpListener.Server.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, 10 /* s */);
        TcpListener.Server.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, 5);

        TcpListener.Start();
        while (true)
        {
            var tcpClient = await TcpListener.AcceptTcpClientAsync(stoppingToken);
            var clientAddress = (tcpClient.Client.RemoteEndPoint as IPEndPoint)!.Address;
            if (clientAddress.IsIPv4MappedToIPv6)
            {
                clientAddress = clientAddress.MapToIPv4();
            }
            if (clientAddress.Equals(IPAddress.Loopback))
            {
                // HACK: workaround for https://github.com/microsoft/WSL/issues/10741
                Logger.Debug("WSL keep-alive workaround");
                tcpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, false);
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
