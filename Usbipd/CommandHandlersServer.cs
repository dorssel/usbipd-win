// SPDX-FileCopyrightText: 2022 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

using System.CommandLine;
using System.Diagnostics;
using Microsoft.Extensions.Hosting.WindowsServices;
using static Usbipd.ConsoleTools;

namespace Usbipd;

sealed partial class CommandHandlers : ICommandHandlers
{
    async Task<ExitCode> ICommandHandlers.Server(string[] args, IConsole console, CancellationToken cancellationToken)
    {
        // Pre-conditions that may fail due to user mistakes. Fail gracefully...

        if (!CheckWriteAccess(console))
        {
            return ExitCode.AccessDenied;
        }

        using var mutex = new Mutex(true, Server.SingletonMutexName, out var createdNew);
        if (!createdNew)
        {
            console.ReportError("Another instance is already running.");
            return ExitCode.Failure;
        }

        // From here on, the server should run without error. Any further errors (exceptions) are probably bugs...

        using var host = Host.CreateDefaultBuilder()
            .UseWindowsService()
            .ConfigureAppConfiguration((context, builder) =>
            {
                var defaultConfig = new Dictionary<string, string?>();
                if (WindowsServiceHelpers.IsWindowsService())
                {
                    // EventLog defaults to Warning, which is OK for .NET components,
                    //      but we want to specifically log Information from our own component.
                    defaultConfig.Add($"Logging:EventLog:LogLevel:{nameof(Usbipd)}", "Information");
                }
                else
                {
                    // When not running as a Windows service, do not spam the EventLog.
                    defaultConfig.Add("Logging:EventLog:LogLevel:Default", "None");
                }
                // set the above as defaults
                _ = builder.AddInMemoryCollection(defaultConfig);
                // allow overrides from the environment
                _ = builder.AddEnvironmentVariables();
                // allow overrides from the command line
                _ = builder.AddCommandLine(args);
            })
            .ConfigureLogging((context, logging) =>
            {
                if (!EventLog.SourceExists(Program.Product))
                {
                    EventLog.CreateEventSource(Program.Product, "Application");
                }
                _ = logging.AddEventLog(settings => settings.SourceName = Program.Product);
            })
            .ConfigureServices((hostContext, services) =>
            {
                _ = services.AddHostedService<Server>();
                _ = services.AddSingleton<PcapNg>();
                _ = services.AddScoped<ClientContext>();
                _ = services.AddScoped<ConnectedClient>();
                _ = services.AddScoped<AttachedClient>();
            })
            .Build();

        await host.RunAsync(cancellationToken);
        return ExitCode.Success;
    }
}
