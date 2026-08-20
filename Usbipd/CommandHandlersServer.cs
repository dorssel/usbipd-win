// SPDX-FileCopyrightText: 2022 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

using static Usbipd.ConsoleTools;

namespace Usbipd;

sealed partial class CommandHandlers : ICommandHandlers
{
    async Task<ExitCode> ICommandHandlers.Server(string[] args, IConsole console, CancellationToken cancellationToken)
    {
        // Pre-conditions that may fail due to user mistakes. Fail gracefully...

        if (!CheckInstalled(console))
        {
            return ExitCode.Failure;
        }
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
            .ConfigureAppConfiguration((context, builder) => _ = builder
                // set the defaults
                .AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        // Our ETW logger only logs explicit LoggerMessages (and not ASP.NET and what not).
                        { $"Logging:Etw:LogLevel:Default", "None" },
                        // It is smart enough to only log events when a listener is attached.
                        { $"Logging:Etw:LogLevel:{nameof(Usbipd)}", "Trace" }
                    })
                // allow overrides from the environment
                .AddEnvironmentVariables()
                // allow overrides from the command line
                .AddCommandLine(args)
            )
            .ConfigureLogging((context, logging) => _ = logging
                // The default builder also adds an EventLog provider, but we don't want that.
                .ClearProviders()
                .AddConsole()
                .AddDebug()
                .AddEtwLogger()
            )
            .ConfigureServices((hostContext, services) => _ = services
                .AddHostedService<Server>()
                .AddSingleton<PcapNg>()
                .AddScoped<ClientContext>()
                .AddScoped<ConnectedClient>()
                .AddScoped<AttachedClient>()
            )
            .Build();

        await host.RunAsync(cancellationToken);
        return ExitCode.Success;
    }
}
