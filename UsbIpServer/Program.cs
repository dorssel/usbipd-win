// SPDX-FileCopyrightText: 2020 Frans van Dorsselaer, Microsoft Corporation
//
// SPDX-License-Identifier: GPL-2.0-only

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.CommandLineUtils;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Logging;

[assembly: CLSCompliant(true)]
[assembly: SupportedOSPlatform("windows8.0")]

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

        public static string Truncate(this string value, int maxChars)
        {
            return value.Length <= maxChars ? value : value.Substring(0, maxChars - 3) + "...";
        }

        static int Main(string[] args)
        {
            var app = new CommandLineApplication()
            {
                Name = Path.GetFileName(Process.GetCurrentProcess().ProcessName),
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
                cmd.Description = "List connected USB devices.";
                DefaultCmdLine(cmd);
                cmd.OnExecute(async () =>
                {
                    var connectedDevices = await ExportedDevice.GetAll(CancellationToken.None);
                    var persistedDevices = RegistryUtils.GetPersistedDevices(connectedDevices);
                    var deviceChecker = new DeviceInfoChecker();
                    Console.WriteLine("Present:");
                    Console.WriteLine($"{"BUS-ID",-8}{"Device",-60}{"Status",-5}");
                    foreach (var device in connectedDevices)
                    {
                        Console.WriteLine($"{device.BusId, -8}{Truncate(deviceChecker.GetDeviceName(device), 60),-60}{ (RegistryUtils.IsDeviceShared(device)? RegistryUtils.IsDeviceAttached(device)? "Attached" : "Shared" : "Not-Shared"), -5}");
                    }
                    Console.Write("\n");
                    Console.WriteLine("Persisted:");
                    Console.WriteLine($"{"GUID",-38}{"BUS-ID",-8}{"Device",-60}");
                    foreach (var device in persistedDevices)
                    {
                        Console.WriteLine($"{device.Guid,-38}{device.BusId, -8}{Truncate(device.Name, 60),-60}");
                    }

                    if (!Server.IsServerRunning())
                    {
                        Console.WriteLine("\nWARNING: Server is currently not running.");
                    }

                    return 0;
                });
            });

            app.Command("bind", (cmd) =>
            {
                cmd.Description = "Bind device";
                var busId = cmd.Option("-b|--busid=<busid>", "Share device having <busid>", CommandOptionType.SingleValue);
                var bindAll = cmd.Option("-a|--all", "Share all devices.", CommandOptionType.NoValue);
                DefaultCmdLine(cmd);
                cmd.OnExecute(() => BindDeviceAsync(bindAll.HasValue(), busId.Value(), CancellationToken.None));
            });

            app.Command("unbind", (cmd) =>
            {
                cmd.Description = "Unbind device";
                var busId = cmd.Option("-b|--busid=<busid>", "Stop sharing device having <busid>", CommandOptionType.SingleValue);
                var guid = cmd.Option("-g|--guid=<guid>", "Stop sharing persisted device having <guid>", CommandOptionType.SingleValue);
                var unbindAll = cmd.Option("-a|--all", "Stop sharing all devices.", CommandOptionType.NoValue);
                DefaultCmdLine(cmd);
                cmd.OnExecute(() => UnbindDeviceAsync(unbindAll.HasValue(), busId.HasValue() ? busId.Value() : null, guid.HasValue() ? guid.Value() : null, CancellationToken.None));
            });

            app.Command("server", (cmd) =>
            {
                cmd.Description = "Run the server stand-alone on the console";
                DefaultCmdLine(cmd);
                cmd.Argument("key=value", ".NET configuration override", true);
                cmd.OnExecute(() => ExecuteServer(cmd.Arguments.Single().Values.ToArray()));
            });

            app.Command("wsl", (metacmd) =>
            {
                metacmd.Description = "Convenience commands for attaching devices to Windows Subsystem for Linux (WSL).";
                DefaultCmdLine(metacmd);

                metacmd.Command("list", (cmd) =>
                {
                    cmd.Description = "Lists all USB devices that are available for being attached into WSL.";
                    DefaultCmdLine(cmd);
                    cmd.OnExecute(async () =>
                    {
                        var distros = await WslDistributions.CreateAsync(CancellationToken.None);
                        var connectedDevices = await ExportedDevice.GetAll(CancellationToken.None);
                        var deviceChecker = new DeviceInfoChecker();

                        Console.WriteLine($"{"ID",-6}{"NAME",-43}{"STATE",-30}");
                        foreach (var device in connectedDevices)
                        {
                            var isAttached = RegistryUtils.IsDeviceAttached(device);
                            var address = RegistryUtils.GetDeviceAddress(device);

                            // The WSL virtual switch is IPv4, but we persist IPv6 in the registry.
                            if (address?.IsIPv4MappedToIPv6 ?? false)
                            {
                                address = address.MapToIPv4();
                            }

                            var distro = address != null ? distros.LookupByIPAddress(address)?.Name : null;
                            var state = isAttached ? ("Attached" + (distro != null ? $" - {distro}" : string.Empty)) : "Not attached";
                            var name = Truncate(deviceChecker.GetDeviceName(device), 42);

                            Console.WriteLine($"{device.BusId,-6}{name,-43}{state,-30}");
                        }

                        return 0;
                    });
                });

                metacmd.Command("attach", (cmd) =>
                {
                    cmd.Description = "Attaches a USB device to a WSL instance.";
                    var busId = cmd.Argument("id", "Share device with this ID.");
                    var distro = cmd.Option("--distribution", "Name of a specific WSL distribution to attach to (optional).", CommandOptionType.SingleValue);
                    var usbipPath = cmd.Option("--usbippath", "Path in the WSL instance to the usbip client tools (optional).", CommandOptionType.SingleValue);
                    DefaultCmdLine(cmd);

                    cmd.OnExecute(async () =>
                    {
                        var address = NetworkInterface.GetAllNetworkInterfaces()
                            .FirstOrDefault(nic => nic.Name.Contains("WSL", StringComparison.OrdinalIgnoreCase))?.GetIPProperties().UnicastAddresses
                            .FirstOrDefault(ip => ip.Address.AddressFamily == AddressFamily.InterNetwork)
                            ?.Address;

                        if (address == null)
                        {
                            Console.Error.WriteLine("The local IP address for the WSL virtual switch could not be found.");
                            return 1;
                        }

                        var distros = await WslDistributions.CreateAsync(CancellationToken.None);

                        // Make sure the distro is running before we attach. While WSL is capable of
                        // starting on the fly when wsl.exe is invoked, that will cause confusing behavior
                        // where we might attach a USB device to WSL, then immediately detach it when the
                        // WSL VM is shutdown shortly afterwards.
                        if (distro.HasValue() && distros.LookupByName(distro.Value()) == null)
                        {
                            Console.Error.WriteLine($"The WSL distribution {distro.Value()} is not running or does exist.");
                            return 1;
                        }
                        else if (!distro.HasValue() && distros.DefaultDistribution == null)
                        {
                            Console.Error.WriteLine($"The default WSL distribution is not running.");
                            return 1;
                        }

                        var bindResult = await BindDeviceAsync(bindAll: false, busId.Value, CancellationToken.None);
                        if (bindResult != 0)
                        {
                            Console.Error.WriteLine($"Failed to bind device with ID \"{busId.Value}\".");
                            return 1;
                        }

                        var path = usbipPath.HasValue() ? usbipPath.Value() : "usbip";
                        var wslResult = await ProcessUtils.RunUncapturedProcessAsync(
                            "wsl.exe",
                            (distro.HasValue() ? new[] { "--distribution", distro.Value() } : Enumerable.Empty<string>()).Concat(
                                new[] { "--", "sudo", path, "attach", $"--remote={address}", $"--busid={busId.Value}" }),
                            CancellationToken.None);
                        if (wslResult != 0)
                        {
                            Console.Error.WriteLine($"Failed to attach device with ID \"{busId.Value}\".");
                            return 1;
                        }

                        return 0;
                    });
                });

                metacmd.Command("detach", (cmd) =>
                {
                    cmd.Description = "Detaches a USB device from a WSL instance and makes it available again in Windows.";
                    var busId = cmd.Argument("id", "Stop sharing device with this ID.");
                    var detachAll = cmd.Option("--all", "Stop sharing all devices.", CommandOptionType.NoValue);
                    DefaultCmdLine(cmd);

                    // This command only exists for convenience. There's no extra work to do in the WSL instance.
                    // Terminating the connection on Windows will also cause WSL to detach.
                    cmd.OnExecute(() => UnbindDeviceAsync(detachAll.HasValue(), busId.Value, guid: null, CancellationToken.None));
                });

                metacmd.OnExecute(() =>
                {
                    app.ShowHelp("wsl");
                    return 0;
                });
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
                    services.AddSingleton<RegistryWatcher>();
                })
                .Build()
                .Run();
            return 0;
        }

        static async Task<int> BindDeviceAsync(bool bindAll, string busId, CancellationToken cancellationToken)
        {
            var connectedDevices = await ExportedDevice.GetAll(cancellationToken);
            if (bindAll)
            {
                foreach (var device in connectedDevices)
                {
                    var checker = new DeviceInfoChecker();
                    RegistryUtils.ShareDevice(device, checker.GetDeviceName(device));
                }

                return 0;
            }

            try
            {
                var targetDevice = connectedDevices.Where(x => x.BusId == busId).First();
                if (targetDevice != null && !RegistryUtils.IsDeviceShared(targetDevice))
                {
                    var checker = new DeviceInfoChecker();
                    RegistryUtils.ShareDevice(targetDevice, checker.GetDeviceName(targetDevice));
                }

                return 0;
            }
            catch (InvalidOperationException)
            {
                Console.Error.WriteLine("There's no device with the specified BUS-ID.");
                return 1;
            }
        }

        static async Task<int> UnbindDeviceAsync(bool unbindAll, string? busId, string? guid, CancellationToken cancellationToken)
        {
            if (unbindAll)
            {
                RegistryUtils.StopSharingAllDevices();
                return 0;
            }

            if (busId != null)
            {
                try
                {
                    var connectedDevices = await ExportedDevice.GetAll(CancellationToken.None);
                    var targetDevice = connectedDevices.Where(x => x.BusId == busId).First();
                    if (targetDevice != null && RegistryUtils.IsDeviceShared(targetDevice))
                    {
                        RegistryUtils.StopSharingDevice(targetDevice);
                    }

                    return 0;
                }
                catch (InvalidOperationException)
                {
                    Console.Error.WriteLine("There's no device with the specified BUS-ID.");
                    return 1;
                }
            }

            if (guid != null)
            {
                try
                {
                    RegistryUtils.StopSharingDevice(guid);
                    return 0;
                }
                catch (ArgumentException)
                {
                    Console.Error.WriteLine("There's no device with the specified GUID.");
                    return 1;
                }
            }

            return 0;
        }
    }
}
