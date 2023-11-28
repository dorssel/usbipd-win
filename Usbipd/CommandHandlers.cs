// SPDX-FileCopyrightText: 2022 Frans van Dorsselaer
// SPDX-FileCopyrightText: Microsoft Corporation
//
// SPDX-License-Identifier: GPL-3.0-only

using System.CommandLine;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.Hosting.WindowsServices;
using Usbipd.Automation;
using static Usbipd.ConsoleTools;
using ExitCode = Usbipd.Program.ExitCode;

namespace Usbipd;

interface ICommandHandlers
{
    public Task<ExitCode> AttachWsl(BusId busId, bool autoAttach, string? distribution, IConsole console, CancellationToken cancellationToken);
    public Task<ExitCode> AttachWsl(VidPid vidPid, bool autoAttach, string? distribution, IConsole console, CancellationToken cancellationToken);
    public Task<ExitCode> Bind(BusId busId, bool force, IConsole console, CancellationToken cancellationToken);
    public Task<ExitCode> Bind(VidPid vidPid, bool force, IConsole console, CancellationToken cancellationToken);
    public Task<ExitCode> Detach(BusId busId, IConsole console, CancellationToken cancellationToken);
    public Task<ExitCode> Detach(VidPid vidPid, IConsole console, CancellationToken cancellationToken);
    public Task<ExitCode> DetachAll(IConsole console, CancellationToken cancellationToken);
    public Task<ExitCode> License(IConsole console, CancellationToken cancellationToken);
    public Task<ExitCode> List(bool usbids, IConsole console, CancellationToken cancellationToken);
    public Task<ExitCode> Server(string[] args, IConsole console, CancellationToken cancellationToken);
    public Task<ExitCode> Unbind(BusId busId, IConsole console, CancellationToken cancellationToken);
    public Task<ExitCode> Unbind(Guid guid, IConsole console, CancellationToken cancellationToken);
    public Task<ExitCode> Unbind(VidPid vidPid, IConsole console, CancellationToken cancellationToken);
    public Task<ExitCode> UnbindAll(IConsole console, CancellationToken cancellationToken);
    public Task<ExitCode> State(IConsole console, CancellationToken cancellationToken);
}

sealed class CommandHandlers : ICommandHandlers
{
    static IEnumerable<UsbDevice> GetDevicesByHardwareId(VidPid vidPid, bool connectedOnly, IConsole console)
    {
        if (!CheckNoStub(vidPid, console))
        {
            return [];
        }
        var devices = UsbDevice.GetAll().Where(d => (d.HardwareId == vidPid) && (!connectedOnly || d.BusId.HasValue));
        var found = false;
        foreach (var device in devices)
        {
            if (device.BusId.HasValue)
            {
                found = true;
                console.ReportInfo($"Device with hardware-id '{vidPid}' found at busid '{device.BusId.Value}'.");
            }
            else if (device.Guid.HasValue)
            {
                found = true;
                console.ReportInfo($"Persisted device with hardware-id '{vidPid}' found at guid '{device.Guid.Value:D}'.");
            }
        }
        if (!found)
        {
            console.ReportError($"No devices found with hardware-id '{vidPid}'.");
        }
        return devices;
    }

    static BusId? GetBusIdByHardwareId(VidPid vidPid, IConsole console)
    {
        try
        {
            var device = GetDevicesByHardwareId(vidPid, true, console).SingleOrDefault();
            if (device is null)
            {
                // Already reported.
                return null;
            }
            return device.BusId;
        }
        catch (InvalidOperationException)
        {
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

    static string GetDescription(UsbDevice device, bool usbids)
    {
        if (usbids)
        {
            var vendor = device.HardwareId.Vendor;
            if (vendor is not null)
            {
                return $"{vendor}, {device.HardwareId.Product ?? ConfigurationManager.UnknownDevice}";
            }
            else
            {
                return ConfigurationManager.UnknownDevice;
            }
        }
        else
        {
            return device.Description;
        }
    }

    Task<ExitCode> ICommandHandlers.List(bool usbids, IConsole console, CancellationToken cancellationToken)
    {
        var allDevices = UsbDevice.GetAll().ToList();
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
            console.WriteTruncated(GetDescription(device, usbids), 60, true);
            console.WriteLine($"  {state}");
        }
        console.WriteLine(string.Empty);

        console.WriteLine("Persisted:");
        console.WriteLine($"{"GUID",-36}  DEVICE");
        foreach (var device in allDevices.Where(d => !d.BusId.HasValue && d.Guid.HasValue).OrderBy(d => d.Guid.GetValueOrDefault()))
        {
            Debug.Assert(device.Guid.HasValue);
            console.Write($"{device.Guid.Value,-36:D}  ");
            console.WriteTruncated(GetDescription(device, usbids), 60, false);
            console.WriteLine(string.Empty);
        }
        console.WriteLine(string.Empty);

        console.CheckAndReportServerRunning(false);
        console.ReportIfForceNeeded();
        return Task.FromResult(ExitCode.Success);
    }

    static ExitCode Bind(BusId busId, bool force, IConsole console)
    {
        var device = UsbDevice.GetAll().Where(d => d.BusId.HasValue && d.BusId.Value == busId).SingleOrDefault();
        if (device is null)
        {
            console.ReportError($"There is no device with busid '{busId}'.");
            return ExitCode.Failure;
        }
        if (device.Guid.HasValue && (force == device.IsForced))
        {
            // Not an error, just let the user know they just executed a no-op.
            console.ReportInfo($"Device with busid '{busId}' was already shared.");
            if (!device.IsForced)
            {
                console.ReportIfForceNeeded();
            }
            return ExitCode.Success;
        }
        if (!CheckWriteAccess(console))
        {
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
        console.CheckAndReportServerRunning(false);
        return ExitCode.Success;
    }

    Task<ExitCode> ICommandHandlers.Bind(BusId busId, bool force, IConsole console, CancellationToken cancellationToken)
    {
        return Task.FromResult(Bind(busId, force, console));
    }

    Task<ExitCode> ICommandHandlers.Bind(VidPid vidPid, bool force, IConsole console, CancellationToken cancellationToken)
    {
        if (GetBusIdByHardwareId(vidPid, console) is not BusId busId)
        {
            return Task.FromResult(ExitCode.Failure);
        }
        return Task.FromResult(Bind(busId, force, console));
    }

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

        var deviceList = devices.ToList();
        if (deviceList.Count == 0)
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
        foreach (var device in deviceList)
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

    async Task<ExitCode> ICommandHandlers.AttachWsl(BusId busId, bool autoAttach, string? distribution, IConsole console, CancellationToken cancellationToken)
    {
        var device = UsbDevice.GetAll().Where(d => d.BusId.HasValue && d.BusId.Value == busId).SingleOrDefault();
        if (device is null)
        {
            console.ReportError($"There is no device with busid '{busId}'.");
            return ExitCode.Failure;
        }
        if (!device.Guid.HasValue)
        {
            console.ReportError($"Device is not shared; run 'usbipd bind -b {busId}' as administrator first.");
            return ExitCode.Failure;
        }
        // We allow auto-attach on devices that are already attached.
        if (!autoAttach && (device.IPAddress is not null))
        {
            console.ReportError($"Device with busid '{busId}' is already attached to a client.");
            return ExitCode.Failure;
        }

        if (!console.CheckAndReportServerRunning(true))
        {
            return ExitCode.Failure;
        }

        return await Wsl.Attach(busId, autoAttach, distribution, console, cancellationToken);
    }

    async Task<ExitCode> ICommandHandlers.AttachWsl(VidPid vidPid, bool autoAttach, string? distribution, IConsole console, CancellationToken cancellationToken)
    {
        if (GetBusIdByHardwareId(vidPid, console) is not BusId busId)
        {
            return ExitCode.Failure;
        }
        return await ((ICommandHandlers)this).AttachWsl(busId, autoAttach, distribution, console, cancellationToken);
    }

    static ExitCode Detach(IEnumerable<UsbDevice> devices, IConsole console)
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

    Task<ExitCode> ICommandHandlers.Detach(BusId busId, IConsole console, CancellationToken cancellationToken)
    {
        var device = UsbDevice.GetAll().Where(d => d.BusId.HasValue && d.BusId.Value == busId).SingleOrDefault();
        if (device is null)
        {
            console.ReportError($"There is no device with busid '{busId}'.");
            return Task.FromResult(ExitCode.Failure);
        }
        return Task.FromResult(Detach(new[] { device }, console));
    }

    Task<ExitCode> ICommandHandlers.Detach(VidPid vidPid, IConsole console, CancellationToken cancellationToken)
    {
        var devices = GetDevicesByHardwareId(vidPid, true, console);
        if (!devices.Any())
        {
            // This would result in a no-op, which may not be what the user intended.
            return Task.FromResult(ExitCode.Failure);
        }
        return Task.FromResult(Detach(devices, console));
    }

    Task<ExitCode> ICommandHandlers.DetachAll(IConsole console, CancellationToken cancellationToken)
    {
        if (!RegistryUtils.SetAllDevicesAsDetached())
        {
            console.ReportError($"Failed to detach one or more devices.");
            return Task.FromResult(ExitCode.Failure);
        }
        return Task.FromResult(ExitCode.Success);
    }

    Task<ExitCode> ICommandHandlers.State(IConsole console, CancellationToken cancellationToken)
    {
        Console.SetError(TextWriter.Null);

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
        return Task.FromResult(ExitCode.Success);
    }
}
