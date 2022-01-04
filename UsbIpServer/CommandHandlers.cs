// SPDX-FileCopyrightText: 2022 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-2.0-only

using System;
using System.CommandLine;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using static UsbIpServer.ConsoleTools;

namespace UsbIpServer
{
    interface ICommandHandlers
    {
        public Task<bool> Bind(BusId busId, IConsole console, CancellationToken cancellationToken);
        public Task<bool> License(IConsole console, CancellationToken cancellationToken);
        public Task<bool> List(IConsole console, CancellationToken cancellationToken);
        public Task<bool> UnbindAll(IConsole console, CancellationToken cancellationToken);
        public Task<bool> Unbind(BusId busId, IConsole console, CancellationToken cancellationToken);
        public Task<bool> Unbind(Guid guid, IConsole console, CancellationToken cancellationToken);
    }

    class CommandHandlers : ICommandHandlers
    {
        public Task<bool> License(IConsole console, CancellationToken cancellationToken)
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
            return Task.FromResult(true);
        }

        public async Task<bool> List(IConsole console, CancellationToken cancellationToken)
        {
            var connectedDevices = await ExportedDevice.GetAll(cancellationToken);
            var persistedDevices = RegistryUtils.GetPersistedDevices(connectedDevices);
            console.WriteLine("Present:");
            console.WriteLine($"{"BUSID",-5}  {"DEVICE",-60}  STATE");
            foreach (var device in connectedDevices)
            {
                // NOTE: Strictly speaking, both Bus and Port can be > 99. If you have one of those, you win a prize!
                console.WriteLine($@"{device.BusId,-5}  {device.Description.Truncate(60),-60}  {
                    (RegistryUtils.IsDeviceShared(device) ? RegistryUtils.IsDeviceAttached(device) ? "Attached" : "Shared" : "Not shared")}");
            }
            console.Write("\n");
            console.WriteLine("Persisted:");
            console.WriteLine($"{"GUID",-38}  {"BUSID",-5}  DEVICE");
            foreach (var device in persistedDevices)
            {
                console.WriteLine($"{device.Guid,-38:B}  {device.BusId,-5}  {device.Description.Truncate(60),-60}");
            }
            ReportServerRunning();
            return true;
        }

        public async Task<bool> Bind(BusId busId, IConsole console, CancellationToken cancellationToken)
        {
            var connectedDevices = await ExportedDevice.GetAll(cancellationToken);
            var device = connectedDevices.Where(x => x.BusId == busId).SingleOrDefault();
            if (device is null)
            {
                ReportError($"There is no compatible device with busid '{busId}'.");
                return false;
            }
            if (RegistryUtils.IsDeviceShared(device))
            {
                // Not an error, just let the user know they just executed a no-op.
#if false
                if (!quiet)
#endif
                {
                    ReportInfo($"Connected device with busid '{busId}' was already shared.");
                }
                return true;
            }
            RegistryUtils.ShareDevice(device, device.Description);
            return true;
        }

        public Task<bool> UnbindAll(IConsole console, CancellationToken cancellationToken)
        {
            RegistryUtils.StopSharingAllDevices();
            return Task.FromResult(true);
        }

        public async Task<bool> Unbind(BusId busId, IConsole console, CancellationToken cancellationToken)
        {
            var connectedDevices = await ExportedDevice.GetAll(cancellationToken);
            var device = connectedDevices.Where(x => x.BusId == busId).SingleOrDefault();
            if (device is null)
            {
                ReportError($"There is no compatible device with busid '{busId}'.");
                return false;
            }
            if (!RegistryUtils.IsDeviceShared(device))
            {
                // Not an error, just let the user know they just executed a no-op.
                ReportInfo($"Connected device with busid '{busId}' was already not shared.");
                return true;
            }
            RegistryUtils.StopSharingDevice(device);
            return true;
        }

        public Task<bool> Unbind(Guid guid, IConsole console, CancellationToken cancellationToken)
        {
            if (!RegistryUtils.GetPersistedDeviceGuids().Contains(guid))
            {
                // Not an error, just let the user know they just executed a no-op.
                ReportInfo($"There is no persisted device with guid '{guid:B}'.");
                return Task.FromResult(true);
            }
            RegistryUtils.StopSharingDevice(guid);
            return Task.FromResult(true);
        }
    }
}
