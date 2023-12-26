// SPDX-FileCopyrightText: Microsoft Corporation
// SPDX-FileCopyrightText: 2021 Frans van Dorsselaer
// SPDX-FileCopyrightText: 2022 Ye Jun Huang
//
// SPDX-License-Identifier: GPL-3.0-only

using System.CommandLine;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using Usbipd.Automation;
using static Usbipd.Program;

namespace Usbipd;

static partial class Wsl
{
    public const string AttachWslUrl = "https://learn.microsoft.com/windows/wsl/connect-usb#attach-a-usb-device";
    const string InstallWslUrl = "https://learn.microsoft.com/windows/wsl/install";
    const string ListDistributionsUrl = "https://learn.microsoft.com/windows/wsl/basic-commands#install";
    const string InstallDistributionUrl = "https://learn.microsoft.com/windows/wsl/basic-commands#install";
    const string SetWslVersionUrl = "https://learn.microsoft.com/windows/wsl/basic-commands#set-wsl-version-to-1-or-2";
    const string AutomountWslUrl = "https://learn.microsoft.com/windows/wsl/wsl-config#automount-settings";

    static readonly string WslPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "wsl.exe");

    public sealed record Distribution(string Name, bool IsDefault, uint Version, bool IsRunning);

    static readonly char[] CtrlC = ['\x03'];

    static async Task<(int ExitCode, string StandardOutput, string StandardError)>
        RunWslAsync((string distribution, string directory)? linux, Action<string, bool>? outputCallback, CancellationToken cancellationToken, params string[] arguments)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var startInfo = new ProcessStartInfo
        {
            FileName = WslPath,
            UseShellExecute = false,
            StandardOutputEncoding = linux is null ? Encoding.Unicode : Encoding.UTF8,
            StandardErrorEncoding = linux is null ? Encoding.Unicode : Encoding.UTF8,
            // None of our commands require user input from the real console.
            StandardInputEncoding = Encoding.ASCII,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
        };
        if (linux is not null)
        {
            startInfo.ArgumentList.Add("--distribution");
            startInfo.ArgumentList.Add(linux.Value.distribution);
            startInfo.ArgumentList.Add("--user");
            startInfo.ArgumentList.Add("root");
            startInfo.ArgumentList.Add("--cd");
            startInfo.ArgumentList.Add(linux.Value.directory);
            startInfo.ArgumentList.Add("--shell-type");
            startInfo.ArgumentList.Add("none");
            startInfo.ArgumentList.Add("--exec");
        }
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new UnexpectedResultException($"Failed to start \"{WslPath}\" with arguments {string.Join(" ", arguments.Select(arg => $"\"{arg}\""))}.");
        var stdout = string.Empty;
        var stderr = string.Empty;

        var callbackLock = new object();
        async Task OnLine(StreamReader streamReader, bool isStandardError)
        {
            while (await streamReader.ReadLineAsync(cancellationToken) is string line)
            {
                if (outputCallback is not null)
                {
                    // prevent stderr/stdout collisions
                    lock (callbackLock)
                    {
                        outputCallback(line, isStandardError);
                    }
                }
                // Note that this normalizes the line endings.
                if (isStandardError)
                {
                    stderr += line + '\n';
                }
                else
                {
                    stdout += line + '\n';
                }
            }
        }

        var captureTasks = new[]
        {
            OnLine(process.StandardOutput, false),
            OnLine(process.StandardError, true),
        };

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        finally
        {
            try
            {
                if (linux is not null)
                {
                    // Our process is wsl.exe running something within Linux. This function tries to kill all of it.
                    // First, Ctrl+C is blindly sent to the Linux process; we allow 100ms for wsl.exe to pass it on.
                    // Regardless of the outcome, we then kill the entire Windows process tree.
                    //
                    // If all went well, there are no left-over processes either on Linux or Windows.
                    // Worst case: the Linux process didn't receive or respond to the Ctrl+C and is still running.
                    //
                    // In any case: the Windows process (wsl.exe) is dead after this.

                    // This should be enough time for the Ctrl+C to pass through. If not, too bad.
                    using var remoteTimeoutTokenSource = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
                    // Fire-and-forget Ctrl+C, this *should* terminate any Linux process.
                    await process.StandardInput.WriteAsync(CtrlC, remoteTimeoutTokenSource.Token);
                    process.StandardInput.Close();
                    await process.WaitForExitAsync(remoteTimeoutTokenSource.Token);
                }
            }
            catch (Exception e) when (e is OperationCanceledException or IOException) { }
            finally
            {
                // Kill the entire Windows process tree, just in case it hasn't exited already.
                process.Kill(true);
            }
        }

        // Since the process either completed or was killed, these should complete or cancel promptly.
        await Task.WhenAll(captureTasks);

        cancellationToken.ThrowIfCancellationRequested();
        return new(process.ExitCode, stdout, stderr);
    }

    /// <summary>
    /// BusId has already been checked, and the server is running.
    /// </summary>
    public static async Task<ExitCode> Attach(BusId busId, bool autoAttach, string? distribution, IConsole console, CancellationToken cancellationToken)
    {
        // Figure out which distribution to use. WSL can be in many states:
        // (a) not installed at all
        // (b) if the user specified one:
        //      (1) it must exist
        //      (2) it must be version 2
        //      (3) it must be running
        // (c) if the user did not specify one:
        //      (1) there must exist at least one distribution
        //      (2) there must exist at least one version 2 distribution
        //      (3) there must be at least one version 2 running
        //      (4)
        //          (i) use the default distribution, if and only if it is version 2 and running
        //              (FYI: This is administered by WSL in HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\Lxss.)
        //          (ii) use the first one that is version 2 and running
        //
        // We provide enough instructions to the user how to fix whatever
        // error/warning we give. Or else we get flooded with "it doesn't work" issues...

        if (await CreateAsync(cancellationToken) is not IEnumerable<Distribution> distributions)
        {
            // check (a) failed
            console.ReportError($"Windows Subsystem for Linux version 2 is not available. See {InstallWslUrl}.");
            return ExitCode.Failure;
        }

        if (distribution is not null)
        {
            // case (b)
            console.ReportInfo("Selecting a specific distribution is no longer required. " +
                "Please file an issue if you believe that the default selection mechanism is not working for you.");

            // check (b1)
            if (distributions.FirstOrDefault(d => d.Name.Equals(distribution, StringComparison.OrdinalIgnoreCase)) is not Distribution selectedDistribution)
            {
                console.ReportError($"The WSL distribution '{distribution}' does not exist. Learn how to list all installed distributions at {ListDistributionsUrl}.");
                return ExitCode.Failure;
            }

            // check (b2)
            switch (selectedDistribution.Version)
            {
                case 1:
                    console.ReportError($"The selected WSL distribution is using WSL 1, but WSL 2 is required. Learn how to upgrade at {SetWslVersionUrl}.");
                    return ExitCode.Failure;
                case 2:
                    // Supported
                    break;
                default:
                    console.ReportError($"The selected WSL distribution is using unsupported WSL {selectedDistribution.Version}, but WSL 2 is required.");
                    return ExitCode.Failure;
            }

            // check (b3)
            if (!selectedDistribution.IsRunning)
            {
                // Make sure the distribution is running before we attach. While WSL is capable of
                // starting on the fly when wsl.exe is invoked, that will cause confusing behavior
                // where we might attach a USB device to WSL, then immediately detach it when the
                // WSL VM is shutdown shortly afterwards.

                console.ReportError($"The selected WSL distribution is not running. Keep a command prompt to the distribution open to leave it running.");
                return ExitCode.Failure;
            }

            distribution = selectedDistribution.Name;
        }
        else
        {
            // case (c)

            // check (c1)
            if (!distributions.Any())
            {
                console.ReportError($"There are no WSL distributions installed. Learn how to install one at {InstallDistributionUrl}.");
                return ExitCode.Failure;
            }

            // check (c2)
            if (!distributions.Any(d => d.Version == 2))
            {
                console.ReportError($"This program only works with WSL 2 distributions. Learn how to upgrade at {SetWslVersionUrl}.");
                return ExitCode.Failure;
            }

            // check (c3)
            if (!distributions.Any(d => d.Version == 2 && d.IsRunning))
            {
                console.ReportError($"There is no WSL 2 distribution running; keep a command prompt to a WSL 2 distribution open to leave it running.");
                return ExitCode.Failure;
            }

            if (distributions.FirstOrDefault(d => d.IsDefault && d.Version == 2 && d.IsRunning) is Distribution defaultDistribution)
            {
                // case (c4i)
                distribution = defaultDistribution.Name;
            }
            else
            {
                // case (c4ii)
                distribution = distributions.First(d => d.Version == 2 && d.IsRunning).Name;
            }
        }

        console.ReportInfo($"Using WSL distribution '{distribution}' to attach; the device will be available in all WSL 2 distributions.");

        // We now have determined which running version 2 distribution to use.

        // Check: WSL kernel must be USBIP capable.
        {
            var wslResult = await RunWslAsync((distribution, "/"), null, cancellationToken, "cat", "/sys/devices/platform/vhci_hcd.0/status");
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

        // Find the location of our WSL client tools w.r.t. a mount point in the distribution.
        string wslLinuxPath;
        {
            var wslWindowsPath = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath)!, "wsl");
            if ((Path.GetPathRoot(wslWindowsPath) is not string wslWindowsPathRoot) || (!LocalDriveRegex().IsMatch(wslWindowsPathRoot)))
            {
                console.ReportError($"Option '--wsl' requires that this software is installed on a local drive.");
                return ExitCode.Failure;
            }
            string wslLinuxMountPoint;
            {
                var wslResult = await RunWslAsync((distribution, "/"), null, cancellationToken, "cat", "/proc/self/mountinfo");
                // Example output:
                //
                // 46 51 0:26 / /mnt/wsl rw,relatime shared:1 - tmpfs none rw
                // 47 51 0:28 / /usr/lib/wsl/drivers ro,nosuid,nodev,noatime - 9p none ro,dirsync,aname=drivers;fmask=222;dmask=222,mmap,access=client,msize=65536,trans=fd,rfd=7,wfd=7
                // 51 38 8:32 / / rw,relatime - ext4 /dev/sdc rw,discard,errors=remount-ro,data=ordered
                // ...
                // 82 68 0:56 / /sys/fs/cgroup/rdma rw,nosuid,nodev,noexec,relatime shared:25 - cgroup cgroup rw,rdma
                // 83 68 0:57 / /sys/fs/cgroup/misc rw,nosuid,nodev,noexec,relatime shared:26 - cgroup cgroup rw,misc
                // 84 51 0:58 / /mnt/c rw,noatime - 9p drvfs rw,dirsync,aname=drvfs;path=C:\;uid=1000;gid=1000;symlinkroot=/mnt/,mmap,access=client,msize=262144,trans=virtio
                //
                // NOTE: The final backslash (\) is optional, and the drive letter is not case sensitive.
                if (wslResult.ExitCode != 0)
                {
                    console.ReportError($"Unable to parse the WSL mount points. Please report this at https://github.com/dorssel/usbipd-win/issues.");
                    return ExitCode.Failure;
                }
                if ((wslResult.StandardOutput.Split('\n')
                    .FirstOrDefault(line => line.Split(" - ").Skip(1).FirstOrDefault()?.Split(' ').Skip(2).FirstOrDefault()?.Split(';').Any(
                        o => o.ToLowerInvariant().TrimEnd('\\') == $"path={wslWindowsPathRoot.ToLowerInvariant().TrimEnd('\\')}") ?? false) is not string mountLine)
                    || (mountLine.Split(' ').Skip(4).FirstOrDefault() is not string mountPoint))
                {
                    console.ReportError($"Option '--wsl' requires that drive {wslWindowsPathRoot} is mounted in WSL; see {AutomountWslUrl}.");
                    return ExitCode.Failure;
                }
                wslLinuxMountPoint = mountPoint;
            }
            wslLinuxPath = Path.Combine(wslLinuxMountPoint, Path.GetRelativePath(wslWindowsPathRoot, wslWindowsPath)).Replace('\\', '/');
        }

        console.ReportInfo($"Using client tools located at {wslLinuxPath}");

        // Check: our distribution-independent usbip client must be runnable.
        {
            var wslResult = await RunWslAsync((distribution, wslLinuxPath), null, cancellationToken, "./usbip", "version");
            if (wslResult.ExitCode != 0 || wslResult.StandardOutput.Trim() != "usbip (usbip-utils 2.0)")
            {
                console.ReportError($"Unable to run 'usbip' client tool. Please report this at https://github.com/dorssel/usbipd-win/issues.");
                return ExitCode.Failure;
            }
        }

        // Now find out the IP address of the host.
        IPAddress hostAddress;
        {
            var wslResult = await RunWslAsync((distribution, "/"), null, cancellationToken, "/usr/bin/wslinfo", "--networking-mode");
            if (wslResult.ExitCode == 0 && wslResult.StandardOutput.Trim() == "mirrored")
            {
                // mirrored networking mode ... we're done
                hostAddress = IPAddress.Loopback;
            }
            else
            {
                // Get all non-loopback unicast IPv4 addresses for WSL.
                var clientAddresses = new List<IPAddress>();
                {
                    // We use 'cat /proc/net/fib_trie', where we assume 'cat' is available on all distributions and /proc/net/fib_trie is supported by the WSL kernel.
                    var ipResult = await RunWslAsync((distribution, "/"), null, cancellationToken, "cat", "/proc/net/fib_trie");
                    if (ipResult.ExitCode == 0)
                    {
                        // Example output:
                        //
                        // Main:
                        //   +-- 0.0.0.0/0 3 0 5
                        //      |-- 0.0.0.0
                        //         /0 universe UNICAST
                        //      +-- 127.0.0.0/8 2 0 2
                        //         +-- 127.0.0.0/31 1 0 0
                        //            |-- 127.0.0.0
                        //               /32 link BROADCAST
                        //               /8 host LOCAL
                        //            |-- 127.0.0.1
                        //               /32 host LOCAL
                        //         |-- 127.255.255.255
                        //            /32 link BROADCAST
                        // ...
                        //
                        // We are interested in all entries like:
                        //
                        //            |-- 127.0.0.1
                        //               /32 host LOCAL
                        //
                        // These are the interface addresses.

                        for (var match = LocalAddressRegex().Match(ipResult.StandardOutput); match.Success; match = match.NextMatch())
                        {
                            if (!IPAddress.TryParse(match.Groups[1].Value, out var clientAddress))
                            {
                                continue;
                            }
                            if (clientAddress.AddressFamily != AddressFamily.InterNetwork)
                            {
                                // For simplicity, we only use IPv4.
                                continue;
                            }
                            if (IsOnSameIPv4Network(IPAddress.Loopback, IPAddress.Parse("255.0.0.0"), clientAddress))
                            {
                                // We are *not* in mirrored network mode, so ignore loopback addresses.
                                continue;
                            }
                            // Only add unique entries. List is not going to be long, so a linear search is fine.
                            if (!clientAddresses.Contains(clientAddress))
                            {
                                clientAddresses.Add(clientAddress);
                            }
                        }
                    }
                }
                if (clientAddresses.Count == 0)
                {
                    console.ReportError($"WSL does not appear to have network connectivity; try `wsl --shutdown` and then restart WSL.");
                    return ExitCode.Failure;
                }

                // Get all non-loopback unicast IPv4 addresses (with their mask) for the host.
                var hostAddresses = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(ni => ni.OperationalStatus == OperationalStatus.Up)
                    .Select(ni => ni.GetIPProperties().UnicastAddresses)
                    .SelectMany(uac => uac)
                    .Where(ua => ua.Address.AddressFamily == AddressFamily.InterNetwork)
                    .Where(ua => !IsOnSameIPv4Network(IPAddress.Loopback, IPAddress.Parse("255.0.0.0"), ua.Address));

                // Find any match; we'll just take the first.
                if (hostAddresses.FirstOrDefault(ha => clientAddresses.Any(ca => IsOnSameIPv4Network(ha.Address, ha.IPv4Mask, ca))) is not UnicastIPAddressInformation matchHost)
                {
                    console.ReportError("The host IP address for the WSL virtual switch could not be found.");
                    return ExitCode.Failure;
                }

                hostAddress = matchHost.Address;
            }
        }

        console.ReportInfo($"Using IP address {hostAddress} to reach the host.");

        // Heuristic firewall check
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
                _ = await RunWslAsync((distribution, "/"), null, linkedTokenSource.Token, "bash", "-c", $"echo < /dev/tcp/{hostAddress}/{Interop.UsbIp.USBIP_PORT}");
            }
            catch (OperationCanceledException) when (timeoutTokenSource.IsCancellationRequested)
            {
                console.ReportWarning($"A third-party firewall may be blocking the connection; ensure TCP port {Interop.UsbIp.USBIP_PORT} is allowed.");
            }
        }

        // This allows us to augment the errors produced by the Linux usbip client tool.
        // Since we are using our own build of usbip, the "interface" is stable.
        void FilterUsbip(string line, bool isStandardError)
        {
            if (string.IsNullOrEmpty(line))
            {
                // usbip throws in an extraneous final empty line
                return;
            }
            // Prepend "WSL", so the user does not confused over "usbip: ... " vs our own "usbipd: ...".
            // We output as "normal text" (although we could filter on "error:").
            console.WriteLine("WSL " + line);
            if (line.Contains("Device busy"))
            {
                // We have already checked that the device is not attached to some other client.
                console.ReportWarning("The device appears to be used by Windows; stop the software using the device, or bind the device using the '--force' option.");
            }
        }

        // Finally, call 'usbip attach', or run the auto-attach.sh script.
        if (!autoAttach)
        {
            var wslResult = await RunWslAsync((distribution, wslLinuxPath), FilterUsbip, cancellationToken, "./usbip", "attach", $"--remote={hostAddress}", $"--busid={busId}");
            if (wslResult.ExitCode != 0)
            {
                console.ReportError($"Failed to attach device with busid '{busId}'.");
                return ExitCode.Failure;
            }
        }
        else
        {
            console.ReportInfo("Starting endless attach loop; press Ctrl+C to quit.");

            _ = await RunWslAsync((distribution, wslLinuxPath), FilterUsbip, cancellationToken, "./auto-attach.sh", hostAddress.ToString(), busId.ToString());
            // This process always ends in failure, as it is supposed to run an endless loop.
            // This may be intended by the user (Ctrl+C, WSL shutdown), others may be real errors.
            // There is no way to tell the difference...
        }

        return ExitCode.Success;
    }

    internal static bool IsOnSameIPv4Network(IPAddress hostAddress, IPAddress hostMask, IPAddress clientAddress)
    {
        if (hostAddress.AddressFamily != AddressFamily.InterNetwork
            || hostMask.AddressFamily != AddressFamily.InterNetwork
            || clientAddress.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        // NOTE: we don't care about byte order here
        var rawHost = BitConverter.ToUInt32(hostAddress.GetAddressBytes());
        var rawInstance = BitConverter.ToUInt32(clientAddress.GetAddressBytes());
        var rawMask = BitConverter.ToUInt32(hostMask.GetAddressBytes());
        return (rawHost & rawMask) == (rawInstance & rawMask);
    }

    [GeneratedRegex(@"^  NAME +STATE +VERSION *$")]
    private static partial Regex WslListHeaderRegex();

    [GeneratedRegex(@"^( |\*) (.+) +([a-zA-Z]+) +([0-9])+ *$")]
    private static partial Regex WslListDistributionRegex();

    [GeneratedRegex(@"\|--\s+(\S+)\s+/32 host LOCAL")]
    private static partial Regex LocalAddressRegex();

    [GeneratedRegex(@"^[a-zA-Z]:\\")]
    private static partial Regex LocalDriveRegex();

    /// <summary>
    /// Returns null if WSL 2 is not even installed.
    /// </summary>
    public static async Task<IEnumerable<Distribution>?> CreateAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(WslPath))
        {
            // Since WSL 2, the wsl.exe command is used to manage WSL.
            // And since USBIP requires a real kernel (i.e. WSL 2), we may safely assume that wsl.exe is available.
            // Users with older (< 1903) Windows will simply get a report that WSL 2 is not available,
            //    even if they have WSL (version 1) installed. It won't work for them anyway.
            // We won't bother checking for the older wslconfig.exe that was used to manage WSL 1.
            return null;
        }

        var distributions = new List<Distribution>();

        // Get a list of details of available distributions (in any state: Stopped, Running, Installing, etc.)
        // This contains all we need (default, name, state, version).
        // NOTE: WslGetDistributionConfiguration() is unreliable getting the version.
        //
        // Sample output:
        //   NAME               STATE           VERSION
        // * Ubuntu             Running         1
        //   Debian             Stopped         2
        //   Custom-MyDistro    Running         2
        var detailsResult = await RunWslAsync(null, null, cancellationToken, "--list", "--all", "--verbose");
        switch (detailsResult.ExitCode)
        {
            case 0:
                var details = detailsResult.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);

                // Sanity check
                if (!WslListHeaderRegex().IsMatch(details.FirstOrDefault() ?? string.Empty))
                {
                    throw new UnexpectedResultException($"WSL failed to parse distributions: {detailsResult.StandardOutput}");
                }

                foreach (var line in details.Skip(1))
                {
                    var match = WslListDistributionRegex().Match(line);
                    if (!match.Success)
                    {
                        throw new UnexpectedResultException($"WSL failed to parse distributions: {detailsResult.StandardOutput}");
                    }
                    var isDefault = match.Groups[1].Value == "*";
                    var name = match.Groups[2].Value.TrimEnd();
                    var isRunning = match.Groups[3].Value == "Running";
                    var version = uint.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture);
                    distributions.Add(new(name, isDefault, version, isRunning));
                }
                break;

            case 1:
                // This is returned by the default wsl.exe placeholder that is available on newer versions of Windows 10 even if WSL is not installed.
                // At least, that seems to be the case; it turns out that the wsl.exe command line interface isn't stable.

                // Newer versions of wsl.exe support the --status command.
                if ((await RunWslAsync(null, null, cancellationToken, "--status")).ExitCode != 0)
                {
                    // We conclude that WSL is indeed not installed at all.
                    return null;
                }

                // We conclude that WSL is installed after all.
                break;

            case -1:
                // This is returned by wsl.exe when WSL is installed, but there are no distributions installed.
                // At least, that seems to be the case; it turns out that the wsl.exe command line interface isn't stable.
                break;

            default:
                // An unknown response. Just assume WSL is installed and report no distributions.
                break;
        }

        return distributions.AsEnumerable();
    }
}
