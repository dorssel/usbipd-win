// SPDX-FileCopyrightText: 2020 Frans van Dorsselaer, Microsoft Corporation
//
// SPDX-License-Identifier: GPL-2.0-only

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
        const string InstallWslUrl = "https://aka.ms/installwsl";
        const string SetWslVersionUrl = "https://docs.microsoft.com/windows/wsl/basic-commands#set-wsl-version-to-1-or-2";

        static readonly string Product = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyProductAttribute>()!.Product;
        static readonly string Copyright = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyCopyrightAttribute>()!.Copyright;
        static readonly string ApplicationName = Path.GetFileName(Process.GetCurrentProcess().ProcessName);

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

        static string Truncate(this string value, int maxChars)
        {
            return value.Length <= maxChars ? value : string.Concat(value.AsSpan(0, maxChars - 3), "...");
        }

        /// <summary>
        /// <para><see cref="CommandLineApplication"/> is rather old and uses the "old style" errors without a terminating period.</para>
        /// <para>Some WinAPI errors (originating from FormatMessage) have a terminating newline.</para>
        /// This function normalizes all errors to
        /// <list type="bullet">
        /// <item>end with a period (.)</item>
        /// <item>not end with a newline</item>
        /// </list>
        /// </summary>
        static string EnforceFinalPeriod(this string s)
        {
            s = s.TrimEnd();
            return s.EndsWith('.') ? s : s + '.';
        }

        /// <summary>
        /// All "console logging" reports go to <see cref="Console.Error"/>, so they can be easily
        /// separated from expected output, e.g. from 'list', which goes to <see cref="Console.Out"/>.
        /// </summary>
        static void ReportText(string level, string text) =>
            Console.Error.WriteLine($"{ApplicationName}: {level}: {EnforceFinalPeriod(text)}");

        static void ReportError(string text) =>
            ReportText("error", text);

        static void ReportWarning(string text) =>
            ReportText("warning", text);

        static void ReportInfo(string text) =>
            ReportText("info", text);

        /// <summary>
        /// Helper to warn users that the service is not running.
        /// For commands that may lead the user to believe that everything is fine when in fact it is not.
        /// For example: 'list' succeeds and shows 'Shared', but attaching from the client will fail.
        /// For example: 'bind' succeeds, but attaching from the client will fail.
        /// </summary>
        static void ReportServerRunning()
        {
            if (!Server.IsServerRunning())
            {
                ReportWarning("Server is currently not running.");
            }
        }

        static string InvalidValueText(CommandOption option) =>
            $"Invalid value '{option.Value()}' for option '--{option.LongName}'.";

        static bool CheckBusId(CommandOption option)
        {
            if (!option.HasValue())
            {
                // Assume the option is optional.
                return true;
            }
            if (!BusId.TryParse(option.Value(), out _))
            {
                ReportError(InvalidValueText(option));
                return false;
            }
            return true;
        }

        static bool CheckGuid(CommandOption option)
        {
            if (!option.HasValue())
            {
                // Assume the option is optional.
                return true;
            }
            if (!Guid.TryParse(option.Value(), out _))
            {
                ReportError(InvalidValueText(option));
                return false;
            }
            return true;
        }

        static string OneOfRequiredText(params CommandOption[] options)
        {
            Debug.Assert(options.Length >= 1);

            var names = options.Select((o) => $"'--{o.LongName}'").ToArray();
            switch (options.Length)
            {
                case 1:
                    // '--a'
                    return $"The option {names[0]} is required.";
                case 2:
                    // '--a' or '--b'
                    return $"Exactly one of the options {names[0]} or {names[1]} is required.";
                default:
                    // '--a', '--b', '--c', or '--d'
                    var list = string.Join(", ", names[0..(names.Length - 1)]) + ", or " + names[^1];
                    return $"Exactly one of the options {list} is required.";
            }
        }

        static bool CheckOneOf(params CommandOption[] options)
        {
            if (options.Count((o) => o.HasValue()) != 1)
            {
                ReportError(OneOfRequiredText(options));
                return false;
            }
            return true;
        }

        static bool CheckWriteAccess()
        {
            if (!RegistryUtils.HasWriteAccess())
            {
                ReportError("Access denied.");
                return false;
            }
            return true;
        }

        static bool CheckServerRunning()
        {
            if (!Server.IsServerRunning())
            {
                ReportError("Server is currently not running.");
                return false;
            }
            return true;
        }

        static int Main(string[] args)
        {
            var app = new CommandLineApplication()
            {
                Name = ApplicationName,
            };
            app.HelpOption("-h|--help");
            app.VersionOption("-v|--version", GitVersionInformation.MajorMinorPatch, GitVersionInformation.InformationalVersion);
            app.ExtendedHelpText = @"
Shares locally connected USB devices to other machines, including Hyper-V
guests and WSL 2.
";
            app.OptionVersion.Description = app.OptionVersion.Description.EnforceFinalPeriod();

            void DefaultCmdLine(CommandLineApplication cmd)
            {
                // all commands (as well as the top-level executable) have these
                cmd.FullName = Product;
                cmd.ShortVersionGetter = app.ShortVersionGetter;
                cmd.OptionHelp.Description = cmd.OptionHelp.Description.EnforceFinalPeriod();
            }

            DefaultCmdLine(app);
            app.Command("license", (cmd) =>
            {
                cmd.Description = "Display license information.";
                cmd.HelpOption("-h|--help");
                cmd.ExtendedHelpText = @"
Displays license information.
";
                DefaultCmdLine(cmd);
                cmd.OnExecute(() =>
                {
                    ShowCopyright();
                    return 0;
                });
            });

            app.Command("list", (cmd) =>
            {
                cmd.Description = "List compatible USB devices.";
                cmd.HelpOption("-h|--help");
                cmd.ExtendedHelpText = @"
Displays a list of compatible USB devices.
";
                DefaultCmdLine(cmd);
                cmd.OnExecute(async () =>
                {
                    var connectedDevices = await ExportedDevice.GetAll(CancellationToken.None);
                    var persistedDevices = RegistryUtils.GetPersistedDevices(connectedDevices);
                    Console.WriteLine("Present:");
                    Console.WriteLine($"{"BUSID",-5}  {"DEVICE",-60}  STATE");
                    foreach (var device in connectedDevices)
                    {
                        // NOTE: Strictly speaking, both Bus and Port can be > 99. If you have one of those, you win a prize!
                        Console.WriteLine($@"{device.BusId,-5}  {Truncate(device.Description, 60),-60}  {
                            (RegistryUtils.IsDeviceShared(device) ? RegistryUtils.IsDeviceAttached(device) ? "Attached" : "Shared" : "Not shared")}");
                    }
                    Console.WriteLine();
                    Console.WriteLine("Persisted:");
                    Console.WriteLine($"{"GUID",-38}  {"BUSID",-5}  DEVICE");
                    foreach (var device in persistedDevices)
                    {
                        Console.WriteLine($"{device.Guid,-38:B}  {device.BusId,-5}  {Truncate(device.Description, 60),-60}");
                    }
                    ReportServerRunning();
                    return 0;
                });
            });

            app.Command("bind", (cmd) =>
            {
                cmd.Description = "Bind device.";
                var bindAll = cmd.Option("-a|--all", "Share all devices.", CommandOptionType.NoValue);
                var busId = cmd.Option("-b|--busid <BUSID>", "Share device having <BUSID>.", CommandOptionType.SingleValue);
                cmd.HelpOption("-h|--help");
                cmd.ExtendedHelpText = $@"
Registers one (or all) compatible USB devices for sharing, so they can be
attached by other machines. Bound devices remain available to the local
machine until they are attached by another machine, at which time they
become unavailable to the local machine.

{OneOfRequiredText(bindAll, busId)}
";
                DefaultCmdLine(cmd);
                cmd.OnExecute(async () =>
                {
                    if (!CheckOneOf(bindAll, busId) || !CheckBusId(busId) || !CheckWriteAccess())
                    {
                        return 1;
                    }

                    int ret;
                    if (bindAll.HasValue())
                    {
                        ret = await BindAllAsync(CancellationToken.None);
                    }
                    else
                    {
                        ret = await BindDeviceAsync(BusId.Parse(busId.Value()), false, CancellationToken.None);
                    }
                    ReportServerRunning();
                    return ret;
                });
            });

            app.Command("unbind", (cmd) =>
            {
                cmd.Description = "Unbind device.";
                var unbindAll = cmd.Option("-a|--all", "Stop sharing all devices.", CommandOptionType.NoValue);
                var busId = cmd.Option("-b|--busid <BUSID>", "Stop sharing device having <BUSID>.", CommandOptionType.SingleValue);
                var guid = cmd.Option("-g|--guid <GUID>", "Stop sharing persisted device having <GUID>.", CommandOptionType.SingleValue);
                cmd.HelpOption("-h|--help");
                cmd.ExtendedHelpText = $@"
Unregisters one (or all) USB devices for sharing. If the device is currently
attached, it will immediately be detached and it becomes available to the
machine again; the remote machine will see this as a surprise removal event.

{OneOfRequiredText(unbindAll, busId, guid)}
";
                DefaultCmdLine(cmd);
                cmd.OnExecute(async () =>
                {
                    if (!CheckOneOf(unbindAll, busId, guid) || !CheckBusId(busId) || !CheckGuid(guid) || !CheckWriteAccess())
                    {
                        return 1;
                    }

                    if (unbindAll.HasValue())
                    {
                        RegistryUtils.StopSharingAllDevices();
                        return 0;
                    }
                    else if (busId.HasValue())
                    {
                        return await UnbindDeviceAsync(BusId.Parse(busId.Value()), CancellationToken.None);
                    }
                    else
                    {
                        var deviceGuid = Guid.Parse(guid.Value());
                        if (!RegistryUtils.GetPersistedDeviceGuids().Contains(deviceGuid))
                        {
                            // Not an error, just let the user know they just executed a no-op.
                            ReportInfo($"There is no persisted device with guid '{deviceGuid:B}'.");
                            return 0;
                        }
                        RegistryUtils.StopSharingDevice(deviceGuid);
                        return 0;
                    }
                });
            });

            app.Command("server", (cmd) =>
            {
                cmd.Description = "Run the server stand-alone on the console.";
                cmd.HelpOption("-h|--help");
                cmd.ExtendedHelpText = @"
Upon installation, the server is running as a background service.
This command is intended for troubleshooting and debugging purposes.
";
                DefaultCmdLine(cmd);
                cmd.Argument("key=value", ".NET configuration override.", true);
                cmd.OnExecute(() => ExecuteServer(cmd.Arguments.Single().Values.ToArray()));
            });

            app.Command("wsl", (metacmd) =>
            {
                metacmd.Description = "Convenience commands for attaching devices to WSL.";
                metacmd.HelpOption("-h|--help");
                metacmd.ExtendedHelpText = @"
Convenience commands for attaching devices to Windows Subsystem for Linux.
";
                DefaultCmdLine(metacmd);

                bool CheckWslInstalled(out WslDistributions distros)
                {
                    distros = WslDistributions.CreateAsync(CancellationToken.None).Result;
                    if (!distros.IsWsl2Installed)
                    {
                        ReportError($"Windows Subsystem for Linux version 2 is not available. See {InstallWslUrl}.");
                        return false;
                    }
                    return true;
                }

                metacmd.Command("list", (cmd) =>
                {
                    cmd.Description = "List all compatible USB devices.";
                    cmd.HelpOption("-h|--help");
                    cmd.ExtendedHelpText = @"
Lists all USB devices that are available for being attached into WSL.
";
                    DefaultCmdLine(cmd);
                    cmd.OnExecute(async () =>
                    {
                        if (!CheckWslInstalled(out var distros))
                        {
                            return 1;
                        }

                        var connectedDevices = await ExportedDevice.GetAll(CancellationToken.None);

                        Console.WriteLine($"{"BUSID",-5}  {"DEVICE",-60}  STATE");
                        foreach (var device in connectedDevices)
                        {
                            var isAttached = RegistryUtils.IsDeviceAttached(device);
                            var address = RegistryUtils.GetDeviceAddress(device);
                            var distro = address is not null ? distros.LookupByIPAddress(address)?.Name : null;
                            var state = isAttached ? ("Attached" + (distro is not null ? $" - {distro}" : string.Empty)) : "Not attached";
                            var description = Truncate(device.Description, 60);

                            Console.WriteLine($"{device.BusId,-5}  {description,-60}  {state}");
                        }
                        ReportServerRunning();
                        return 0;
                    });
                });

                metacmd.Command("attach", (cmd) =>
                {
                    cmd.Description = "Attach a compatible USB device to a WSL instance.";
                    var busId = cmd.Option("-b|--busid <BUSID>", "Attach device having <BUSID>.", CommandOptionType.SingleValue);
                    var distro = cmd.Option("-d|--distribution <NAME>", "Name of a specific WSL distribution to attach to.", CommandOptionType.SingleValue);
                    cmd.HelpOption("-h|--help");
                    var usbipPath = cmd.Option("-u|--usbip-path <PATH>", "Path in the WSL instance to the 'usbip' client tool.", CommandOptionType.SingleValue);
                    cmd.ExtendedHelpText = $@"
Attaches a compatible USB devices to a WSL instance.

The 'wsl attach' command is equivalent to the 'bind' command, followed by
a 'usbip attach' command on the Linux side.

{OneOfRequiredText(busId)}
";
                    DefaultCmdLine(cmd);

                    cmd.OnExecute(async () =>
                    {
                        if (!CheckOneOf(busId) || !CheckBusId(busId) || !CheckWslInstalled(out var distros) || !CheckWriteAccess() || !CheckServerRunning())
                        {
                            return 1;
                        }

                        // Make sure the distro is running before we attach. While WSL is capable of
                        // starting on the fly when wsl.exe is invoked, that will cause confusing behavior
                        // where we might attach a USB device to WSL, then immediately detach it when the
                        // WSL VM is shutdown shortly afterwards.
                        var distroData = distro.HasValue() ? distros.LookupByName(distro.Value()) : distros.DefaultDistribution;

                        // The order of the following checks is important, as later checks can only succeed if earlier checks already passed.

                        // 1) Distro must exist

                        if (distroData is null)
                        {
                            ReportError(distro.HasValue()
                                ? $"The WSL distribution '{distro.Value()}' does not exist."
                                : "No default WSL distribution exists."
                            );
                            return 1;
                        }

                        // 2) Distro must be correct WSL version

                        switch (distroData.Version)
                        {
                            case 1:
                                ReportError($"The specified WSL distribution is using WSL 1, but WSL 2 is required. Learn how to upgrade at {SetWslVersionUrl}.");
                                return 1;
                            case 2:
                                // Supported
                                break;
                            default:
                                ReportError($"The specified WSL distribution is using unsupported WSL {distroData.Version}, but WSL 2 is required.");
                                return 1;
                        }

                        // 3) Distro must be running

                        if (!distroData.IsRunning)
                        {
                            ReportError($"The specified WSL distribution is not running.");
                            return 1;
                        }

                        // 4) Host must be reachable.
                        //    This check only makes sense if at least one WSL 2 distro is running, which is ensured by earlier checks.

                        if (distros.HostAddress is null)
                        {
                            // This would be weird: we already know that a WSL 2 instance is running.
                            // Maybe the virtual switch does not have 'WSL' in the name?
                            ReportError("The local IP address for the WSL virtual switch could not be found.");
                            return 1;
                        }

                        // 5) Distro must have connectivity.
                        //    This check only makes sense if the host is reachable, which is ensured by earlier checks.

                        if (distroData.IPAddress is null)
                        {
                            ReportError($"The specified WSL distribution cannot be reached via the WSL virtual switch; try restarting the WSL distribution.");
                            return 1;
                        }

                        var bindResult = await BindDeviceAsync(BusId.Parse(busId.Value()), true, CancellationToken.None);
                        if (bindResult != 0)
                        {
                            ReportError($"Failed to bind device with ID '{busId.Value()}'.");
                            return 1;
                        }

                        var path = usbipPath.HasValue() ? usbipPath.Value() : "usbip";
                        var wslResult = await ProcessUtils.RunUncapturedProcessAsync(
                            WslDistributions.WslPath,
                            (distro.HasValue() ? new[] { "--distribution", distro.Value() } : Enumerable.Empty<string>()).Concat(
                                new[] { "--", "sudo", path, "attach", $"--remote={distros.HostAddress}", $"--busid={busId.Value()}" }),
                            CancellationToken.None);
                        if (wslResult != 0)
                        {
                            ReportError($"Failed to attach device with ID '{busId.Value()}'.");
                            return 1;
                        }

                        return 0;
                    });
                });

                metacmd.Command("detach", (cmd) =>
                {
                    cmd.Description = "Detaches a USB device from a WSL instance.";
                    var detachAll = cmd.Option("-a|--all", "Detach all devices.", CommandOptionType.NoValue);
                    var busId = cmd.Option("-b|--busid <BUSID>", "Detach device having <BUSID>.", CommandOptionType.SingleValue);
                    cmd.HelpOption("-h|--help");
                    cmd.ExtendedHelpText = $@"
Detaches one (or all) USB devices. The WSL instance sees this as a surprise
removal event. A detached device becomes available again in Windows.

The 'wsl detach' command is equivalent to the 'unbind' command.

{OneOfRequiredText(detachAll, busId)}
";
                    DefaultCmdLine(cmd);
                    cmd.OnExecute(async () =>
                    {
                        if (!CheckOneOf(detachAll, busId) || !CheckBusId(busId) || !CheckWslInstalled(out _) || !CheckWriteAccess())
                        {
                            return 1;
                        }

                        if (detachAll.HasValue())
                        {
                            RegistryUtils.StopSharingAllDevices();
                            return 0;
                        }
                        else
                        {
                            return await UnbindDeviceAsync(BusId.Parse(busId.Value()), CancellationToken.None);
                        }
                    });
                });

                metacmd.OnExecute(() =>
                {
                    // 'wsl' always expects a subcommand. Without a subcommand, just act as if '--help' was provided.
                    app.ShowHelp("wsl");
                    return 0;
                });
            });

            app.OnExecute(() =>
            {
                // main executable always expects a subcommand. Without a subcommand, just act as if '--help' was provided.
                app.ShowHelp();
                return 0;
            });

            try
            {
                return app.Execute(args);
            }
            catch (CommandParsingException ex)
            {
                ReportError(ex.Message);
                return 1;
            }
        }

        static int ExecuteServer(string[] args)
        {
            // Pre-conditions that may fail due to user mistakes. Fail gracefully...

            if (!CheckWriteAccess())
            {
                return 1;
            }

            using var mutex = new Mutex(true, Server.SingletonMutexName, out var createdNew);
            if (!createdNew)
            {
                ReportError("Another instance is already running.");
                return 1;
            }

            // From here on, the server should run without error. Any further errors (exceptions) are probably bugs...

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
                    services.AddSingleton<DeviceChangeWatcher>();
                })
                .Build()
                .Run();
            return 0;
        }

        /// <summary>
        /// Worker for <code>usbipd bind --all</code>.
        /// </summary>
        static async Task<int> BindAllAsync(CancellationToken cancellationToken)
        {
            var connectedDevices = await ExportedDevice.GetAll(cancellationToken);
            foreach (var device in connectedDevices)
            {
                if (!RegistryUtils.IsDeviceShared(device))
                {
                    RegistryUtils.ShareDevice(device, device.Description);
                }
            }
            return 0;
        }

        /// <summary>
        /// Worker for <code>usbipd bind --busid <paramref name="busId"/></code>.
        /// </summary>
        static async Task<int> BindDeviceAsync(BusId busId, bool quiet, CancellationToken cancellationToken)
        {
            var connectedDevices = await ExportedDevice.GetAll(cancellationToken);
            var device = connectedDevices.Where(x => x.BusId == busId).SingleOrDefault();
            if (device is null)
            {
                ReportError($"There is no compatible device with busid '{busId}'.");
                return 1;
            }
            if (RegistryUtils.IsDeviceShared(device))
            {
                // Not an error, just let the user know they just executed a no-op.
                if (!quiet)
                {
                    ReportInfo($"Connected device with busid '{busId}' was already shared.");
                }
                return 0;
            }
            RegistryUtils.ShareDevice(device, device.Description);
            return 0;
        }

        /// <summary>
        /// Worker for <code>usbipd unbind --busid <paramref name="busId"/></code>.
        /// </summary>
        static async Task<int> UnbindDeviceAsync(BusId busId, CancellationToken cancellationToken)
        {
            var connectedDevices = await ExportedDevice.GetAll(cancellationToken);
            var device = connectedDevices.Where(x => x.BusId == busId).SingleOrDefault();
            if (device is null)
            {
                ReportError($"There is no compatible device with busid '{busId}'.");
                return 1;
            }
            if (!RegistryUtils.IsDeviceShared(device))
            {
                // Not an error, just let the user know they just executed a no-op.
                ReportInfo($"Connected device with busid '{busId}' was already not shared.");
                return 0;
            }
            RegistryUtils.StopSharingDevice(device);
            return 0;
        }
    }
}
