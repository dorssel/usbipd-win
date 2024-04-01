// SPDX-FileCopyrightText: 2024 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

using System.CommandLine;
using Usbipd.Automation;

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
    public Task<ExitCode> Install(IConsole console, CancellationToken cancellationToken);
    public Task<ExitCode> Uninstall(IConsole console, CancellationToken cancellationToken);
    public Task<ExitCode> PolicyAdd(PolicyRule rule, IConsole console, CancellationToken cancellationToken);
    public Task<ExitCode> PolicyList(IConsole console, CancellationToken cancellationToken);
    public Task<ExitCode> PolicyRemove(Guid guid, IConsole console, CancellationToken cancellationToken);
}
