// SPDX-FileCopyrightText: 2020 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-2.0-only

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.CommandLineUtils;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Logging;

[assembly: CLSCompliant(true)]

namespace UsbIpServer
{
    static class Program
    {
        static string Product => Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyProductAttribute>()!.Product;
        static string Copyright => Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyCopyrightAttribute>()!.Copyright;

        static void ShowCopyright()
        {
            Console.WriteLine($@"{Product} {GitVersionInformation.MajorMinorPatch}
{Copyright}

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, version 2.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with this program.  If not, see <https://www.gnu.org/licenses/>.
");
        }

        static int Main(string[] args)
        {
            var app = new CommandLineApplication()
            {
                Name = Path.ChangeExtension(Path.GetFileName(Assembly.GetExecutingAssembly().Location), "exe"),
            };
            app.VersionOption("-v|--version", GitVersionInformation.MajorMinorPatch, GitVersionInformation.InformationalVersion);

            void DefaultCmdLine(CommandLineApplication cmd)
            {
                // all commands (as well as the top-level executable) have these
                cmd.FullName = Product;
                cmd.ShortVersionGetter = app.ShortVersionGetter;
                cmd.HelpOption("-h|--help").ShowInHelpText = false;
            }

            DefaultCmdLine(app);
            app.OptionHelp.ShowInHelpText = true;
            app.Command("license", (cmd) =>
            {
                cmd.Description = "Display license information";
                DefaultCmdLine(cmd);
                cmd.OnExecute(() =>
                {
                    ShowCopyright();
                    return 0;
                });
            });

            app.Command("list", (cmd) =>
            {

            });

            app.Command("bind", (cmd) =>
            {

            });

            app.Command("unbind", (cmd) =>
            {

            });
#if false
            // TODO: Linux style binding (optional?)
            // for now, just allow any client to claim any device (!)
            app.Command("bind", (cmd) => {
                cmd.Description = "Bind device to VBoxUsb.sys";
                DefaultCmdLine(cmd);
                cmd.Option("-b|--busid=<busid>", "Bind VBoxUsb.sys to device on <busid>", CommandOptionType.SingleValue);
            });
            app.Command("unbind", (cmd) => {
                cmd.Description = "Unbind device from VBoxUsb.sys";
                DefaultCmdLine(cmd);
                cmd.Option("-b|--busid=<busid>", "Unbind VBoxUsb.sys from device on <busid>", CommandOptionType.SingleValue);
            });
#endif
            app.Command("server", (cmd) =>
            {
                cmd.Description = "Run the server stand-alone on the console";
                DefaultCmdLine(cmd);
                cmd.Argument("key=value", ".NET configuration override", true);
                cmd.OnExecute(() => ExecuteServer(cmd.Arguments.Single().Values.ToArray()));
            });

            app.OnExecute(() =>
            {
                app.ShowRootCommandFullNameAndVersion();
                app.ShowHint();
                return 0;
            });

            try
            {
                return app.Execute(args);
            }
            catch (CommandParsingException ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }
        }

        static int ExecuteServer(string[] args)
        {
            Host.CreateDefaultBuilder()
                .UseWindowsService()
                .ConfigureAppConfiguration((context, builder) =>
                {
                    var defaultConfig = new Dictionary<string, string>();
                    if (WindowsServiceHelpers.IsWindowsService())
                    {
                        // EventLog defaults to Warning, which is OK for .NET components,
                        //      but we want to specifically log Information from our own component.
                        defaultConfig.Add($"Logging:EventLog:LogLevel:{nameof(UsbIpServer)}", "Information");
                    }
                    else
                    {
                        // When not running as a Windows service, do not spam the EventLog.
                        defaultConfig.Add("Logging:EventLog:LogLevel:Default", "None");
                    }
                    // set the above as defaults
                    builder.AddInMemoryCollection(defaultConfig);
                    // allow overrides from the environment
                    builder.AddEnvironmentVariables();
                    // allow overrides from the command line
                    builder.AddCommandLine(args);
                })
                .ConfigureLogging((context, logging) =>
                {
                    logging.AddEventLog(settings =>
                    {
                        settings.SourceName = Product;
                    });
                })
                .ConfigureServices((hostContext, services) =>
                {
                    services.AddHostedService<Server>();
                    services.AddScoped<ClientContext>();
                    services.AddScoped<ConnectedClient>();
                    services.AddScoped<AttachedClient>();
                })
                .Build()
                .Run();
            return 0;
        }
    }
}
