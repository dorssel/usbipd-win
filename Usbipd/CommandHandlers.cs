// SPDX-FileCopyrightText: 2022 Frans van Dorsselaer
// SPDX-FileCopyrightText: Microsoft Corporation
//
// SPDX-License-Identifier: GPL-3.0-only

using System;
using System.Collections.Generic;
using System.CommandLine;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Logging;
using Usbipd.Automation;
using Windows.Win32.Security;
using static Usbipd.ConsoleTools;
using ExitCode = Usbipd.Program.ExitCode;

namespace Usbipd;

interface ICommandHandlers
{
    public Task<ExitCode> Bind(BusId busId, bool force, IConsole console, CancellationToken cancellationToken);
    public Task<ExitCode> Bind(VidPid vidPid, bool force, IConsole console, CancellationToken cancellationToken);
    public Task<ExitCode> License(IConsole console, CancellationToken cancellationToken);
    public Task<ExitCode> List(IConsole console, CancellationToken cancellationToken);
    public Task<ExitCode> Server(string[] args, IConsole console, CancellationToken cancellationToken);
    public Task<ExitCode> Unbind(BusId busId, IConsole console, CancellationToken cancellationToken);
    public Task<ExitCode> Unbind(Guid guid, IConsole console, CancellationToken cancellationToken);
    public Task<ExitCode> Unbind(VidPid vidPid, IConsole console, CancellationToken cancellationToken);
    public Task<ExitCode> UnbindAll(IConsole console, CancellationToken cancellationToken);

    public Task<ExitCode> WslAttach(BusId busId, bool autoAttach, string? distribution, IConsole console, CancellationToken cancellationToken);
    public Task<ExitCode> WslAttach(VidPid vidPid, bool autoAttach, string? distribution, IConsole console, CancellationToken cancellationToken);
    public Task<ExitCode> WslDetach(BusId busId, IConsole console, CancellationToken cancellationToken);
    public Task<ExitCode> WslDetach(VidPid vidPid, IConsole console, CancellationToken cancellationToken);
    public Task<ExitCode> WslDetachAll(IConsole console, CancellationToken cancellationToken);
    public Task<ExitCode> WslList(IConsole console, CancellationToken cancellationToken);

    public Task<ExitCode> State(IConsole console, CancellationToken cancellationToken);
}

sealed class CommandHandlers : ICommandHandlers
{
    static IEnumerable<UsbDevice> GetDevicesByHardwareId(VidPid vidPid, bool connectedOnly, IConsole console)
    {
        if (!CheckNoStub(vidPid, console))
        {
            return Array.Empty<UsbDevice>();
        }
        var devices = UsbDevice.GetAll().Where(d => (d.HardwareId == vidPid) && (!connectedOnly || d.BusId.HasValue));
        if (!devices.Any())
        {
            console.ReportError($"No devices found with hardware-id '{vidPid}'.");
        }
        else
        {
            foreach (var device in devices)
            {
                if (device.BusId.HasValue)
                {
                    console.ReportInfo($"Device with hardware-id '{vidPid}' found at busid '{device.BusId.Value}'.");
                }
                else if (device.Guid.HasValue)
                {
                    console.ReportInfo($"Persisted device with hardware-id '{vidPid}' found at guid '{device.Guid.Value:D}'.");
                }
            }
        }
        return devices;
    }

    static BusId? GetBusIdByHardwareId(VidPid vidPid, IConsole console)
    {
        var devices = GetDevicesByHardwareId(vidPid, true, console);
        switch (devices.Take(2).Count())
        {
            case 0:
                // Already reported.
                return null;
            case 1:
                return devices.Single().BusId;
            case 2:
            default:
                console.ReportError($"Multiple devices with hardware-id '{vidPid}' were found; disambiguate by using '--busid'.");
                return null;
        }
    }

    Task<ExitCode> ICommandHandlers.License(IConsole console, CancellationToken cancellationToken)
    {
        // 70 leads (approximately) to the GPL default.
        var width = console.IsOutputRedirected ? 70 : Console.WindowWidth;
        foreach (var line in Wrap($"""
            {Program.Product} {GitVersionInformation.MajorMinorPatch}
            {Program.Copyright}

            This program is free software: you can redistribute it and/or modify \
            it under the terms of the GNU General Public License as published by \
            the Free Software Foundation, version 3.

            This program is distributed in the hope that it will be useful, \
            but WITHOUT ANY WARRANTY; without even the implied warranty of \
            MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the \
            GNU General Public License for more details.

            You should have received a copy of the GNU General Public License \
            along with this program. If not, see <https://www.gnu.org/licenses/>.

            """.Replace("\r\n", "\n").Replace("\\\n", "")
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
        console.WriteLine($"{"BUSID",-5}  {"VID:PID",-9}  {"DEVICE",-60}  STATE");
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
            console.Write($"{device.HardwareId,-9}  ");
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

    Task<ExitCode> ICommandHandlers.Bind(VidPid vidPid, bool force, IConsole console, CancellationToken cancellationToken)
    {
        if (GetBusIdByHardwareId(vidPid, console) is not BusId busId)
        {
            return Task.FromResult(ExitCode.Failure);
        }
        return Task.FromResult(Bind(busId, false, force, console));
    }

    async Task<ExitCode> ICommandHandlers.Server(string[] args, IConsole console, CancellationToken cancellationToken)
    {
        // Pre-conditions that may fail due to user mistakes. Fail gracefully...

        if (!CheckWriteAccess(console))
        {
            return ExitCode.AccessDenied;
        }

        using var mutex = new Mutex(true, Usbipd.Server.SingletonMutexName, out var createdNew);
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

    static ExitCode Unbind(IEnumerable<UsbDevice> devices, IConsole console)
    {
        // Unbind acts as a cleanup and has to support partially failed binds.

        if (!devices.Any())
        {
            // This would result in a no-op, which may not be what the user intended.
            return ExitCode.Failure;
        }
        if (!CheckWriteAccess(console))
        {
            // We don't actually know if there is anything to clean up, but if there is
            // then administrator privileges are required.
            return ExitCode.AccessDenied;
        }
        var reboot = false;
        var driverError = false;
        foreach (var device in devices)
        {
            if (device.Guid is not null)
            {
                RegistryUtils.StopSharingDevice(device.Guid.Value);
            }
            try
            {
                if (NewDev.UnforceVBoxDriver(device.InstanceId))
                {
                    reboot = true;
                }
            }
            catch (Exception ex) when (ex is Win32Exception || ex is ConfigurationManagerException)
            {
                driverError = true;
            }
        }
        if (driverError)
        {
            console.ReportError("Not all drivers could be restored.");
        }
        if (reboot)
        {
            console.ReportRebootRequired();
        }
        return ExitCode.Success;
    }

    Task<ExitCode> ICommandHandlers.Unbind(Guid guid, IConsole console, CancellationToken cancellationToken)
    {
        var device = RegistryUtils.GetBoundDevices().Where(d => d.Guid.HasValue && d.Guid.Value == guid).SingleOrDefault();
        if (device is null)
        {
            console.ReportError($"There is no device with guid '{guid:D}'.");
            return Task.FromResult(ExitCode.Failure);
        }
        return Task.FromResult(Unbind(new[] { device }, console));
    }

    Task<ExitCode> ICommandHandlers.Unbind(VidPid vidPid, IConsole console, CancellationToken cancellationToken)
    {
        return Task.FromResult(Unbind(GetDevicesByHardwareId(vidPid, false, console), console));
    }

    Task<ExitCode> ICommandHandlers.UnbindAll(IConsole console, CancellationToken cancellationToken)
    {
        // UnbindAll() is even more special. It will also delete corrupt registry entries and
        // also removes stub drivers for devices that are neither shared nor connected.
        // Therefore, UnbindAll() cannot use the generic Unbind() helper.

        if (!CheckWriteAccess(console))
        {
            return Task.FromResult(ExitCode.AccessDenied);
        }
        RegistryUtils.StopSharingAllDevices();
        var reboot = false;
        var driverError = false;
        foreach (var originalDeviceId in ConfigurationManager.GetOriginalDeviceIdsWithVBoxDriver())
        {
            try
            {
                if (NewDev.UnforceVBoxDriver(originalDeviceId))
                {
                    reboot = true;
                }
            }
            catch (Exception ex) when (ex is Win32Exception || ex is ConfigurationManagerException)
            {
                driverError = true;
            }
        }
        if (driverError)
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

    async Task<ExitCode> ICommandHandlers.WslAttach(BusId busId, bool autoAttach, string? distribution, IConsole console, CancellationToken cancellationToken)
    {
        var device = UsbDevice.GetAll().Where(d => d.BusId.HasValue && d.BusId.Value == busId).SingleOrDefault();
        if (device is null)
        {
            console.ReportError($"There is no device with busid '{busId}'.");
            return ExitCode.Failure;
        }
        // We allow auto-attach on devices that are already attached.
        if (!autoAttach && (device.IPAddress is not null))
        {
            console.ReportError($"Device with busid '{busId}' is already attached to a client.");
            return ExitCode.Failure;
        }

        if (await GetDistributionsAsync(console, cancellationToken) is not WslDistributions distros)
        {
            return ExitCode.Failure;
        }

        if (!CheckServerRunning(console))
        {
            return ExitCode.Failure;
        }

        // The order of the following checks is important, as later checks can only succeed if earlier checks already passed.

        // 1) Distro must exist

        // Figure out which distribution to use. WSL can be in many states:
        // (a) not installed at all (already handled by GetDistributionsAsync above)
        // (b) installed but, with 0 distributions (error)
        // (c) 1 distribution, but it is not marked as default (warning)
        // (d) 1 distribution, correctly marked as default (ok)
        // (e) more than 1 distribution, none marked as default (error, or warning when using --distribution)
        // (f) more than 1 distribution, one of which is default (ok)
        // This is administered by WSL in HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\Lxss.
        //
        // We provide enough instructions to the user how to fix whatever
        // error/warning we give. Or else we get flooded with "it doesn't work" issues...

        WslDistributions.Distribution distroData;
        if (distribution is null)
        {
            // The user did not specifically provide the --distribution option.
            switch (distros.Distributions.Count())
            {
                case 0:
                    // case (b)
                    console.ReportError("There are no WSL distributions installed; see https://docs.microsoft.com/windows/wsl/basic-commands#install on how to install one.");
                    return ExitCode.Failure;
                case 1:
                    if (distros.DefaultDistribution is null)
                    {
                        // case (c)
                        // This can happen if the user removed the default distribution.
                        distroData = distros.Distributions.Single();
                        console.ReportWarning($"Using the only available WSL distribution '{distroData.Name}', but it should have been set as default; " +
                            "see https://docs.microsoft.com/windows/wsl/basic-commands#set-default-linux-distribution on how to fix this.");
                    }
                    else
                    {
                        // case (d)
                        distroData = distros.DefaultDistribution;
                    }
                    break;
                default:
                    if (distros.DefaultDistribution is null)
                    {
                        // case (e)
                        // This can happen if the user removed the default distribution.
                        console.ReportError("More than one WSL distribution is available, but none is set as default; " +
                            "see https://docs.microsoft.com/windows/wsl/basic-commands#set-default-linux-distribution on how to fix this.");
                        return ExitCode.Failure;
                    }
                    else
                    {
                        // case (f)
                        distroData = distros.DefaultDistribution;
                        // This helps out users that may not be aware that they have more than one and have the default set to the "wrong" one.
                        console.ReportInfo($"Using default WSL distribution '{distroData.Name}'; specify the '--distribution' option to select a different one.");
                    }
                    break;
            }
        }
        else if (distros.LookupByName(distribution) is WslDistributions.Distribution selectedDistroData)
        {
            distroData = selectedDistroData;
            if (distros.DefaultDistribution is null)
            {
                // case (c/e)
                console.ReportWarning("No WSL distribution is set as default; " +
                    "see https://docs.microsoft.com/windows/wsl/basic-commands#set-default-linux-distribution on how to fix this.");
            }
        }
        else
        {
            console.ReportError($"The WSL distribution '{distribution}' does not exist; " +
                "see https://docs.microsoft.com/windows/wsl/basic-commands#list-installed-linux-distributions.");
            return ExitCode.Failure;
        }

        // 2) Distro must be correct WSL version

        switch (distroData.Version)
        {
            case 1:
                console.ReportError($"The selected WSL distribution is using WSL 1, but WSL 2 is required. Learn how to upgrade at {WslDistributions.SetWslVersionUrl}.");
                return ExitCode.Failure;
            case 2:
                // Supported
                break;
            default:
                console.ReportError($"The selected WSL distribution is using unsupported WSL {distroData.Version}, but WSL 2 is required.");
                return ExitCode.Failure;
        }

        // 3) Distro must be running

        if (!distroData.IsRunning)
        {
            // Make sure the distro is running before we attach. While WSL is capable of
            // starting on the fly when wsl.exe is invoked, that will cause confusing behavior
            // where we might attach a USB device to WSL, then immediately detach it when the
            // WSL VM is shutdown shortly afterwards.

            console.ReportError($"The selected WSL distribution is not running; keep a command prompt to the distribution open to leave it running.");
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
            console.ReportError($"The selected WSL distribution cannot be reached via the WSL virtual switch; try restarting the WSL distribution.");
            return ExitCode.Failure;
        }

        var bindResult = Bind(busId, true, false, console);
        if (bindResult != ExitCode.Success)
        {
            return bindResult;
        }

        // 6) WSL kernel must be USBIP capable.

        {
            var wslResult = await ProcessUtils.RunCapturedProcessAsync(
                WslDistributions.WslPath,
                new[] { "--distribution", distroData.Name, "--user", "root", "--", "cat", "/sys/devices/platform/vhci_hcd.0/status" },
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
                new[] { "--distribution", distroData.Name, "--user", "root", "--", "usbip", "version" },
                Encoding.UTF8, cancellationToken);
            // Expected output:
            //
            //    usbip (usbip-utils 2.0)
            //
            // NOTE: The package name and version varies.
            if (wslResult.ExitCode != 0 || !wslResult.StandardOutput.StartsWith("usbip ("))
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
                    new[] { "--distribution", distroData.Name, "--user", "root", "--", "bash", "-c", $"echo < /dev/tcp/{distros.HostAddress}/{Interop.UsbIp.USBIP_PORT}" },
                    Encoding.UTF8, linkedTokenSource.Token);
            }
            catch (OperationCanceledException) when (timeoutTokenSource.IsCancellationRequested)
            {
                console.ReportWarning($"A third-party firewall may be blocking the connection; ensure TCP port {Interop.UsbIp.USBIP_PORT} is allowed.");
            }
        }

        // Finally, call 'usbip attach', or run the auto-attach.sh script.
        if (!autoAttach)
        {
            var wslResult = await ProcessUtils.RunUncapturedProcessAsync(
                WslDistributions.WslPath,
                new[] { "--distribution", distroData.Name, "--user", "root", "--", "usbip", "attach", $"--remote={distros.HostAddress}", $"--busid={busId}" },
                cancellationToken);
            if (wslResult != 0)
            {
                console.ReportError($"Failed to attach device with busid '{busId}'.");
                return ExitCode.Failure;
            }
        }
        else
        {
            var scriptWindowsPath = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath)!, "wsl-scripts", "auto-attach.sh");
            if (!Regex.IsMatch(Path.GetPathRoot(scriptWindowsPath)!, "[a-zA-Z]:\\\\"))
            {
                console.ReportError($"Option '--auto-attach' requires that this software is installed on a local drive.");
                return ExitCode.Failure;
            }
            var driveLetter = scriptWindowsPath[0..1].ToLowerInvariant();
            var scriptLinuxPath = Path.Combine(@"\mnt", driveLetter, Path.GetRelativePath(Path.GetPathRoot(scriptWindowsPath)!, scriptWindowsPath)).Replace('\\', '/');

            console.ReportInfo("Starting endless attach loop; press Ctrl+C to quit.");

            await ProcessUtils.RunUncapturedProcessAsync(
                WslDistributions.WslPath,
                new[] { "--distribution", distroData.Name, "--user", "root", "--", "bash", scriptLinuxPath, distros.HostAddress.ToString(), busId.ToString() },
                cancellationToken);
            // This process always ends in failure, as it is supposed to run an endless loop.
            // This may be intended by the user (Ctrl+C, WSL shutdown), others may be real errors.
            // There is no way to tell the difference...
        }

        return ExitCode.Success;
    }

    async Task<ExitCode> ICommandHandlers.WslAttach(VidPid vidPid, bool autoAttach, string? distribution, IConsole console, CancellationToken cancellationToken)
    {
        if (GetBusIdByHardwareId(vidPid, console) is not BusId busId)
        {
            return ExitCode.Failure;
        }
        return await ((ICommandHandlers)this).WslAttach(busId, autoAttach, distribution, console, cancellationToken);
    }

    static ExitCode WslDetach(IEnumerable<UsbDevice> devices, IConsole console)
    {
        var error = false;
        foreach (var device in devices)
        {
            if (!device.Guid.HasValue || device.IPAddress is null)
            {
                // Not an error, just let the user know they just executed a no-op.
                console.ReportInfo($"Device with busid '{device.BusId}' was already not attached.");
                continue;
            }
            if (!RegistryUtils.SetDeviceAsDetached(device.Guid.Value))
            {
                console.ReportError($"Failed to detach device with busid '{device.BusId}'.");
                error = true;
            }
        }
        return error ? ExitCode.Failure : ExitCode.Success;
    }

    Task<ExitCode> ICommandHandlers.WslDetach(BusId busId, IConsole console, CancellationToken cancellationToken)
    {
        var device = UsbDevice.GetAll().Where(d => d.BusId.HasValue && d.BusId.Value == busId).SingleOrDefault();
        if (device is null)
        {
            console.ReportError($"There is no device with busid '{busId}'.");
            return Task.FromResult(ExitCode.Failure);
        }
        return Task.FromResult(WslDetach(new[] { device }, console));
    }

    Task<ExitCode> ICommandHandlers.WslDetach(VidPid vidPid, IConsole console, CancellationToken cancellationToken)
    {
        var devices = GetDevicesByHardwareId(vidPid, true, console);
        if (!devices.Any())
        {
            // This would result in a no-op, which may not be what the user intended.
            return Task.FromResult(ExitCode.Failure);
        }
        return Task.FromResult(WslDetach(devices, console));
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

        console.WriteLine($"{"BUSID",-5}  {"VID:PID",-9}  {"DEVICE",-60}  STATE");
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
            console.Write($"{device.HardwareId,-9}  ");
            console.WriteTruncated(device.Description, 60, true);
            console.WriteLine($"  {state}");
        }
        console.WriteLine(string.Empty);

        console.ReportIfServerNotRunning();
        console.ReportIfForceNeeded();
        return ExitCode.Success;
    }

    async Task<ExitCode> ICommandHandlers.State(IConsole console, CancellationToken cancellationToken)
    {
        Console.SetError(TextWriter.Null);

        WslDistributions? distros = null;
        try
        {
            distros = await GetDistributionsAsync(console, cancellationToken);
        }
        catch (UnexpectedResultException) { }

        var devices = new List<Device>();
        foreach (var device in UsbDevice.GetAll().OrderBy(d => d.InstanceId))
        {
            devices.Add(new()
            {
                InstanceId = device.InstanceId,
                Description = device.Description,
                IsForced = device.IsForced,
                BusId = device.BusId,
                PersistedGuid = device.Guid,
                StubInstanceId = device.StubInstanceId,
                ClientIPAddress = device.IPAddress,
                ClientWslInstance = device.IPAddress is null ? null : distros?.LookupByIPAddress(device.IPAddress)?.Name,
            });
        }

        var state = new State()
        {
            Devices = devices,
        };

        var context = new StateSerializerContext(new()
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = true,
        });
        var json = JsonSerializer.Serialize(state, context.State);

        Console.Write(json);
        return ExitCode.Success;
    }
}
