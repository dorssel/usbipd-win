// SPDX-FileCopyrightText: 2024 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

using System.CommandLine;
using System.Net;
using Usbipd.Automation;

namespace Usbipd;

interface ICommandHandlers
{
    Task<ExitCode> AttachWsl(BusId busId, bool autoAttach, bool unplugged, string? distribution, IPAddress? hostAddress,
        IConsole console, CancellationToken cancellationToken);
    Task<ExitCode> AttachWsl(VidPid vidPid, bool autoAttach, string? distribution, IPAddress? hostAddress,
        IConsole console, CancellationToken cancellationToken);
    Task<ExitCode> Bind(BusId busId, bool force, IConsole console, CancellationToken cancellationToken);
    Task<ExitCode> Bind(VidPid vidPid, bool force, IConsole console, CancellationToken cancellationToken);
    Task<ExitCode> Detach(BusId busId, IConsole console, CancellationToken cancellationToken);
    Task<ExitCode> Detach(VidPid vidPid, IConsole console, CancellationToken cancellationToken);
    Task<ExitCode> DetachAll(IConsole console, CancellationToken cancellationToken);
    Task<ExitCode> License(IConsole console, CancellationToken cancellationToken);
    Task<ExitCode> List(bool usbIds, IConsole console, CancellationToken cancellationToken);
    Task<ExitCode> Server(string[] args, IConsole console, CancellationToken cancellationToken);
    Task<ExitCode> Unbind(BusId busId, IConsole console, CancellationToken cancellationToken);
    Task<ExitCode> Unbind(Guid guid, IConsole console, CancellationToken cancellationToken);
    Task<ExitCode> Unbind(VidPid vidPid, IConsole console, CancellationToken cancellationToken);
    Task<ExitCode> UnbindAll(IConsole console, CancellationToken cancellationToken);
    Task<ExitCode> State(IConsole console, CancellationToken cancellationToken);
    Task<ExitCode> Install(IConsole console, CancellationToken cancellationToken);
    Task<ExitCode> Uninstall(IConsole console, CancellationToken cancellationToken);
    Task<ExitCode> PolicyAdd(PolicyRule rule, IConsole console, CancellationToken cancellationToken);
    Task<ExitCode> PolicyList(IConsole console, CancellationToken cancellationToken);
    Task<ExitCode> PolicyRemove(Guid guid, IConsole console, CancellationToken cancellationToken);
    Task<ExitCode> PolicyRemoveAll(IConsole console, CancellationToken cancellationToken);
}
