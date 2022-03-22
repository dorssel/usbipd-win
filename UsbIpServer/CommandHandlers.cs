// SPDX-FileCopyrightText: 2022 Frans van Dorsselaer
// SPDX-FileCopyrightText: Microsoft Corporation
//
// SPDX-License-Identifier: GPL-2.0-only

using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Logging;
using Windows.Win32.Security;
using static UsbIpServer.ConsoleTools;
using Automation = Usbipd.Automation;
using ExitCode = UsbIpServer.Program.ExitCode;

namespace UsbIpServer
{
    interface ICommandHandlers
    {
        public Task<ExitCode> Bind(BusId busId, bool force, IConsole console, CancellationToken cancellationToken);
        public Task<ExitCode> License(IConsole console, CancellationToken cancellationToken);
        public Task<ExitCode> List(IConsole console, CancellationToken cancellationToken);
        public Task<ExitCode> Server(string[] args, IConsole console, CancellationToken cancellationToken);
        public Task<ExitCode> Unbind(BusId busId, IConsole console, CancellationToken cancellationToken);
        public Task<ExitCode> Unbind(Guid guid, IConsole console, CancellationToken cancellationToken);
        public Task<ExitCode> UnbindAll(IConsole console, CancellationToken cancellationToken);

        public Task<ExitCode> WslAttach(BusId busId, string? distribution, string? usbipPath, IConsole console, CancellationToken cancellationToken);
        public Task<ExitCode> WslDetach(BusId busId, IConsole console, CancellationToken cancellationToken);
        public Task<ExitCode> WslDetachAll(IConsole console, CancellationToken cancellationToken);
        public Task<ExitCode> WslList(IConsole console, CancellationToken cancellationToken);

        public Task<ExitCode> State(IConsole console, CancellationToken cancellationToken);
    }

    sealed class CommandHandlers : ICommandHandlers
    {
        Task<ExitCode> ICommandHandlers.License(IConsole console, CancellationToken cancellationToken)
        {
            // 70 leads (approximately) to the GPL default.
            var width = console.IsOutputRedirected ? 70 : Console.WindowWidth;
            foreach (var line in Wrap($"{Program.Product} {GitVersionInformation.MajorMinorPatch}\n"
                + $"{Program.Copyright}\n"
                + "\n"
                + "This program is free software: you can redistribute it and/or modify "
                + "it under the terms of the GNU General Public License as published by "
                + "the Free Software Foundation, version 2.\n"
                + "\n"
                + "This program is distributed in the hope that it will be useful, "
                + "but WITHOUT ANY WARRANTY; without even the implied warranty of "
                + "MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the "
                + "GNU General Public License for more details.\n"
                + "\n"
                + "You should have received a copy of the GNU General Public License "
                + "along with this program. If not, see <https://www.gnu.org/licenses/>.\n"
                , width))
            {
                console.WriteLine(line);
            }
            return Task.FromResult(ExitCode.Success);
        }

        Task<ExitCode> ICommandHandlers.List(IConsole console, CancellationToken cancellationToken)
        {
            var allDevices = UsbDevice.GetAll();
            console.WriteLine("Connected:");
            console.WriteLine($"{"BUSID",-5}  {"DEVICE",-60}  STATE");
            foreach (var device in allDevices.Where(d => d.BusId.HasValue).OrderBy(d => d.BusId.GetValueOrDefault()))
            {
                Debug.Assert(device.BusId.HasValue);
                string state;
                if (device.IPAddress is not null)
                {
                    state = "Attached";
                }
                else if (device.Guid is not null)
                {
                    state = device.IsForced ? "Shared (forced)" : "Shared";
                }
                else
                {
                    state = "Not shared";
                }
                // NOTE: Strictly speaking, both Bus and Port can be > 99. If you have one of those, you win a prize!
                console.Write($"{device.BusId.Value,-5}  ");
                console.WriteTruncated(device.Description, 60, true);
                console.WriteLine($"  {state}");
            }
            console.WriteLine(string.Empty);

            console.WriteLine("Persisted:");
            console.WriteLine($"{"GUID",-36}  DEVICE");
            foreach (var device in allDevices.Where(d => !d.BusId.HasValue && d.Guid.HasValue).OrderBy(d => d.Guid.GetValueOrDefault()))
            {
                Debug.Assert(device.Guid.HasValue);
                console.Write($"{device.Guid.Value,-36:D}  ");
                console.WriteTruncated(device.Description, 60, false);
                console.WriteLine(string.Empty);
            }
            console.WriteLine(string.Empty);

            console.ReportIfServerNotRunning();
            console.ReportIfForceNeeded();
            return Task.FromResult(ExitCode.Success);
        }

        static ExitCode Bind(BusId busId, bool wslAttach, bool force, IConsole console)
        {
            var device = UsbDevice.GetAll().Where(d => d.BusId.HasValue && d.BusId.Value == busId).SingleOrDefault();
            if (device is null)
            {
                console.ReportError($"There is no device with busid '{busId}'.");
                return ExitCode.Failure;
            }
            if (device.Guid.HasValue && (wslAttach || (force == device.IsForced)))
            {
                // Not an error, just let the user know they just executed a no-op.
                if (!wslAttach)
                {
                    console.ReportInfo($"Device with busid '{busId}' was already shared.");
                }
                if (!device.IsForced)
                {
                    console.ReportIfForceNeeded();
                }
                return ExitCode.Success;
            }
            if (!CheckWriteAccess(console))
            {
                if (wslAttach)
                {
                    TOKEN_ELEVATION_TYPE elevationType;
                    unsafe
                    {
                        using var identity = WindowsIdentity.GetCurrent();
                        var b = Windows.Win32.PInvoke.GetTokenInformation(identity.AccessToken, TOKEN_INFORMATION_CLASS.TokenElevationType, &elevationType, 4, out var returnLength);
                        if (!b || returnLength != 4)
                        {
                            // Assume elevation is not available.
                            elevationType = TOKEN_ELEVATION_TYPE.TokenElevationTypeDefault;
                        }
                    }

                    if (elevationType == TOKEN_ELEVATION_TYPE.TokenElevationTypeLimited)
                    {
                        console.ReportInfo("The first time attaching a device to WSL requires elevated privileges; subsequent attaches will succeed with standard user privileges.");
                    }
                    else
                    {
                        console.ReportInfo($"To share this device, an administrator will first have to execute 'usbipd bind --busid {busId}'.");
                    }
                }
                return ExitCode.AccessDenied;
            }
            if (!device.Guid.HasValue)
            {
                RegistryUtils.Persist(device.InstanceId, device.Description);
            }
            if (!force)
            {
                // Do not warn that force may be needed if the user is actually using --force.
                console.ReportIfForceNeeded();
            }
            if (force != device.IsForced)
            {
                // Switch driver.
                var reboot = force ? NewDev.ForceVBoxDriver(device.InstanceId) : NewDev.UnforceVBoxDriver(device.InstanceId);
                if (reboot)
                {
                    console.ReportRebootRequired();
                }
            }
            console.ReportIfServerNotRunning();
            return ExitCode.Success;
        }

        Task<ExitCode> ICommandHandlers.Bind(BusId busId, bool force, IConsole console, CancellationToken cancellationToken)
        {
            return Task.FromResult(Bind(busId, false, force, console));
        }

        async Task<ExitCode> ICommandHandlers.Server(string[] args, IConsole console, CancellationToken cancellationToken)
        {
            // Pre-conditions that may fail due to user mistakes. Fail gracefully...

            if (!CheckWriteAccess(console))
            {
                return ExitCode.AccessDenied;
            }

            using var mutex = new Mutex(true, UsbIpServer.Server.SingletonMutexName, out var createdNew);
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
                    if (!EventLog.SourceExists(Program.Product))
                    {
                        EventLog.CreateEventSource(Program.Product, "Application");
                    }
                    logging.AddEventLog(settings =>
                    {
                        settings.SourceName = Program.Product;
                    });
                })
                .ConfigureServices((hostContext, services) =>
                {
                    services.AddHostedService<Server>();
                    services.AddSingleton<PcapNg>();
                    services.AddScoped<ClientContext>();
                    services.AddScoped<ConnectedClient>();
                    services.AddScoped<AttachedClient>();
                })
                .Build();

            await host.RunAsync(cancellationToken);
            return ExitCode.Success;
        }

        Task<ExitCode> ICommandHandlers.Unbind(BusId busId, IConsole console, CancellationToken cancellationToken)
        {
            var device = UsbDevice.GetAll().Where(d => d.BusId.HasValue && d.BusId.Value == busId).SingleOrDefault();
            if (device is null)
            {
                console.ReportError($"There is no device with busid '{busId}'.");
                return Task.FromResult(ExitCode.Failure);
            }
            if (device.Guid is null)
            {
                // Not an error, just let the user know they just executed a no-op.
                console.ReportInfo($"Device with busid '{busId}' was already not shared.");
                return Task.FromResult(ExitCode.Success);
            }
            if (!CheckWriteAccess(console))
            {
                return Task.FromResult(ExitCode.AccessDenied);
            }
            RegistryUtils.StopSharingDevice(device.Guid.Value);
            if (NewDev.UnforceVBoxDriver(device.InstanceId))
            {
                console.ReportRebootRequired();
            }
            return Task.FromResult(ExitCode.Success);
        }

        Task<ExitCode> ICommandHandlers.Unbind(Guid guid, IConsole console, CancellationToken cancellationToken)
        {
            var device = RegistryUtils.GetBoundDevices().Where(d => d.Guid.HasValue && d.Guid.Value == guid).SingleOrDefault();
            if (device is null)
            {
                console.ReportError($"There is no device with guid '{guid:D}'.");
                return Task.FromResult(ExitCode.Failure);
            }
            if (!CheckWriteAccess(console))
            {
                return Task.FromResult(ExitCode.AccessDenied);
            }
            RegistryUtils.StopSharingDevice(guid);
            if (NewDev.UnforceVBoxDriver(device.InstanceId))
            {
                console.ReportRebootRequired();
            }
            return Task.FromResult(ExitCode.Success);
        }

        Task<ExitCode> ICommandHandlers.UnbindAll(IConsole console, CancellationToken cancellationToken)
        {
            if (!CheckWriteAccess(console))
            {
                return Task.FromResult(ExitCode.AccessDenied);
            }
            RegistryUtils.StopSharingAllDevices();
            var reboot = false;
            var error = false;
            foreach (var originalDeviceId in ConfigurationManager.GetOriginalDeviceIdsWithVBoxDriver())
            {
                try
                {
                    if (NewDev.UnforceVBoxDriver(originalDeviceId))
                    {
                        reboot = true;
                    }
                }
                catch (ConfigurationManagerException)
                {
                    error = true;
                }
            }
            if (error)
            {
                console.ReportError("Not all drivers could be restored.");
            }
            if (reboot)
            {
                console.ReportRebootRequired();
            }
            return Task.FromResult(ExitCode.Success);
        }

        static async Task<WslDistributions?> GetDistributionsAsync(IConsole console, CancellationToken cancellationToken)
        {
            var distributions = await WslDistributions.CreateAsync(cancellationToken);
            if (distributions is null)
            {
                console.ReportError($"Windows Subsystem for Linux version 2 is not available. See {WslDistributions.InstallWslUrl}.");
            }
            return distributions;
        }

        async Task<ExitCode> ICommandHandlers.WslAttach(BusId busId, string? distribution, string? usbipPath, IConsole console, CancellationToken cancellationToken)
        {
            if (await GetDistributionsAsync(console, cancellationToken) is not WslDistributions distros)
            {
                return ExitCode.Failure;
            }

            if (!CheckServerRunning(console))
            {
                return ExitCode.Failure;
            }

            // Make sure the distro is running before we attach. While WSL is capable of
            // starting on the fly when wsl.exe is invoked, that will cause confusing behavior
            // where we might attach a USB device to WSL, then immediately detach it when the
            // WSL VM is shutdown shortly afterwards.
            var distroData = distribution is not null ? distros.LookupByName(distribution) : distros.DefaultDistribution;

            // The order of the following checks is important, as later checks can only succeed if earlier checks already passed.

            // 1) Distro must exist

            if (distroData is null)
            {
                console.ReportError(distribution is not null
                    ? $"The WSL distribution '{distribution}' does not exist."
                    : "No default WSL distribution exists."
                );
                return ExitCode.Failure;
            }

            if ((distros.Distributions.Count() > 1) && (distribution is null))
            {
                // This helps out users that may not be aware that they have more than one and have the default set to the "wrong" one.
                console.ReportInfo($"Using default distribution '{distroData.Name}'.");
            }

            // 2) Distro must be correct WSL version

            switch (distroData.Version)
            {
                case 1:
                    console.ReportError($"The specified WSL distribution is using WSL 1, but WSL 2 is required. Learn how to upgrade at {WslDistributions.SetWslVersionUrl}.");
                    return ExitCode.Failure;
                case 2:
                    // Supported
                    break;
                default:
                    console.ReportError($"The specified WSL distribution is using unsupported WSL {distroData.Version}, but WSL 2 is required.");
                    return ExitCode.Failure;
            }

            // 3) Distro must be running

            if (!distroData.IsRunning)
            {
                console.ReportError($"The specified WSL distribution is not running.");
                return ExitCode.Failure;
            }

            // 4) Host must be reachable.
            //    This check only makes sense if at least one WSL 2 distro is running, which is ensured by earlier checks.

            if (distros.HostAddress is null)
            {
                // This would be weird: we already know that a WSL 2 instance is running.
                // Maybe the virtual switch does not have 'WSL' in the name?
                console.ReportError("The local IP address for the WSL virtual switch could not be found.");
                return ExitCode.Failure;
            }

            // 5) Distro must have connectivity.
            //    This check only makes sense if the host is reachable, which is ensured by earlier checks.

            if (distroData.IPAddress is null)
            {
                console.ReportError($"The specified WSL distribution cannot be reached via the WSL virtual switch; try restarting the WSL distribution.");
                return ExitCode.Failure;
            }

            var bindResult = Bind(busId, true, false, console);
            if (bindResult != ExitCode.Success)
            {
                return bindResult;
            }

            usbipPath ??= "usbip";

            // 6) WSL kernel must be USBIP capable.

            {
                var wslResult = await ProcessUtils.RunCapturedProcessAsync(
                    WslDistributions.WslPath,
                    (distribution is not null ? new[] { "--distribution", distribution } : Enumerable.Empty<string>()).Concat(
                        new[] { "--user", "root", "--", "cat", "/sys/devices/platform/vhci_hcd.0/status" }),
                    Encoding.UTF8, cancellationToken);
                // Expected output:
                //
                //    hub port sta spd dev      sockfd local_busid
                //    hs  0000 006 002 00040002 000003 1-1
                //    hs  0001 004 000 00000000 000000 0-0
                //    ...
                if (wslResult.ExitCode != 0 || !wslResult.StandardOutput.Contains("local_busid"))
                {
                    console.ReportError($"WSL kernel is not USBIP capable; update with 'wsl --update'.");
                    return ExitCode.Failure;
                }
            }

            // 7) WSL 'usbip' must be correctly installed for root.

            {
                var wslResult = await ProcessUtils.RunCapturedProcessAsync(
                    WslDistributions.WslPath,
                    (distribution is not null ? new[] { "--distribution", distribution } : Enumerable.Empty<string>()).Concat(
                        new[] { "--user", "root", "--", usbipPath, "version" }),
                    Encoding.UTF8, cancellationToken);
                // Expected output:
                //
                //    usbip (usbip-utils 2.0)
                //
                // NOTE: The package name and version varies.
#pragma warning disable CA1508 // Avoid dead conditional code (false positive)
                if (wslResult.ExitCode != 0 || !wslResult.StandardOutput.StartsWith("usbip ("))
#pragma warning restore CA1508 // Avoid dead conditional code
                {
                    console.ReportError($"WSL 'usbip' client not correctly installed. See {WslDistributions.WslWikiUrl} for the latest instructions.");
                    return ExitCode.Failure;
                }
            }

            // 8) Heuristic firewall check
            //
            // With minimal requirements (bash only) try to connect from WSL to our server.
            // If the process does not terminate within one second, then most likely a third party
            // firewall is blocking the connection. Anything else (e.g. bash not available, or not supporting
            // /dev/tcp, or whatever) will most likely finish within 1 second and the test will simply pass.
            // In any case, just issue a warning, which is a lot more informative than the 1 minute TCP
            // timeout that usbip will get.
            {
                using var timeoutTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                using var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutTokenSource.Token);
                try
                {
                    var wslResult = await ProcessUtils.RunCapturedProcessAsync(
                        WslDistributions.WslPath,
                        (distribution is not null ? new[] { "--distribution", distribution } : Enumerable.Empty<string>()).Concat(
                            new[] { "--user", "root", "--", "bash", "-c", $"echo < /dev/tcp/{distros.HostAddress}/{Interop.UsbIp.USBIP_PORT}" }),
                        Encoding.UTF8, linkedTokenSource.Token);
                }
                catch (OperationCanceledException) when (timeoutTokenSource.IsCancellationRequested)
                {
                    console.ReportWarning($"A third-party firewall may be blocking the connection; ensure TCP port {Interop.UsbIp.USBIP_PORT} is allowed.");
                }
            }

            // Finally, call 'usbip attach'.

            {
                var wslResult = await ProcessUtils.RunUncapturedProcessAsync(
                    WslDistributions.WslPath,
                    (distribution is not null ? new[] { "--distribution", distribution } : Enumerable.Empty<string>()).Concat(
                        new[] { "--user", "root", "--", usbipPath, "attach", $"--remote={distros.HostAddress}", $"--busid={busId}" }),
                    cancellationToken);
                if (wslResult != 0)
                {
                    console.ReportError($"Failed to attach device with BUSID '{busId}'.");
                    return ExitCode.Failure;
                }
            }

            return ExitCode.Success;
        }

        Task<ExitCode> ICommandHandlers.WslDetach(BusId busId, IConsole console, CancellationToken cancellationToken)
        {
            var device = UsbDevice.GetAll().Where(d => d.BusId.HasValue && d.BusId.Value == busId).SingleOrDefault();
            if (device is null)
            {
                console.ReportError($"There is no device with busid '{busId}'.");
                return Task.FromResult(ExitCode.Failure);
            }
            if (!device.Guid.HasValue || device.IPAddress is null)
            {
                // Not an error, just let the user know they just executed a no-op.
                console.ReportInfo($"Device with busid '{busId}' was already not attached.");
                return Task.FromResult(ExitCode.Success);
            }
            if (!RegistryUtils.SetDeviceAsDetached(device.Guid.Value))
            {
                console.ReportError($"Failed to detach device with BUSID '{busId}'.");
                return Task.FromResult(ExitCode.Failure);
            }
            return Task.FromResult(ExitCode.Success);
        }

        Task<ExitCode> ICommandHandlers.WslDetachAll(IConsole console, CancellationToken cancellationToken)
        {
            if (!RegistryUtils.SetAllDevicesAsDetached())
            {
                console.ReportError($"Failed to detach one or more devices.");
                return Task.FromResult(ExitCode.Failure);
            }
            return Task.FromResult(ExitCode.Success);
        }

        async Task<ExitCode> ICommandHandlers.WslList(IConsole console, CancellationToken cancellationToken)
        {
            if (await GetDistributionsAsync(console, cancellationToken) is not WslDistributions distros)
            {
                return ExitCode.Failure;
            }

            console.WriteLine($"{"BUSID",-5}  {"DEVICE",-60}  STATE");
            foreach (var device in UsbDevice.GetAll().Where(d => d.BusId.HasValue).OrderBy(d => d.BusId))
            {
                string state;
                if (device.IPAddress is not null)
                {
                    if (distros.LookupByIPAddress(device.IPAddress)?.Name is string distro)
                    {
                        state = $"Attached - {distro}";
                    }
                    else
                    {
                        state = "Attached";
                    }
                }
                else
                {
                    state = "Not attached";
                }
                // NOTE: Strictly speaking, both Bus and Port can be > 99. If you have one of those, you win a prize!
                console.Write($"{device.BusId,-5}  ");
                console.WriteTruncated(device.Description, 60, true);
                console.WriteLine($"  {state}");
            }
            console.WriteLine(string.Empty);

            console.ReportIfServerNotRunning();
            console.ReportIfForceNeeded();
            return ExitCode.Success;
        }

        [UnconditionalSuppressMessage("Trimming", "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code",
            Justification = "Only basic types are used; all required members are accessed (and therefore not trimmed away).")]
        async Task<ExitCode> ICommandHandlers.State(IConsole console, CancellationToken cancellationToken)
        {
            Console.SetError(TextWriter.Null);

            WslDistributions? distros = null;
            try
            {
                distros = await GetDistributionsAsync(console, cancellationToken);
            }
            catch (UnexpectedResultException) { }

            var devices = new List<Automation.Device>();
            foreach (var device in UsbDevice.GetAll().OrderBy(d => d.InstanceId))
            {
                devices.Add(new()
                {
                    InstanceId = device.InstanceId,
                    Description = device.Description,
                    IsForced = device.IsForced,
                    BusId = device.BusId?.ToString(),
                    PersistedGuid = device.Guid,
                    StubInstanceId = device.StubInstanceId,
                    ClientIPAddress = device.IPAddress,
                    ClientWslInstance = device.IPAddress is null ? null : distros?.LookupByIPAddress(device.IPAddress)?.Name,
                });
            }

            var state = new Automation.State()
            {
                Devices = devices,
            };

            using var memoryStream = new MemoryStream();
            {
                using var writer = JsonReaderWriterFactory.CreateJsonWriter(memoryStream, Encoding.UTF8, false, true);
                var serializer = new DataContractJsonSerializer(state.GetType());
                serializer.WriteObject(writer, state);
            }

            Console.Write(Encoding.UTF8.GetString(memoryStream.ToArray()));
            return ExitCode.Success;
        }
    }
}
