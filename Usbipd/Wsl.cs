// SPDX-FileCopyrightText: Microsoft Corporation
// SPDX-FileCopyrightText: 2021 Frans van Dorsselaer
// SPDX-FileCopyrightText: 2022 Ye Jun Huang
//
// SPDX-License-Identifier: GPL-3.0-only

using System.CommandLine;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using Usbipd.Automation;

namespace Usbipd;

static partial class Wsl
{
    public const string AttachWslUrl = "https://learn.microsoft.com/windows/wsl/connect-usb#attach-a-usb-device";
    const string InstallWslUrl = "https://learn.microsoft.com/windows/wsl/install";
    const string ListDistributionsUrl = "https://learn.microsoft.com/windows/wsl/basic-commands#install";
    const string InstallDistributionUrl = "https://learn.microsoft.com/windows/wsl/basic-commands#install";
    const string SetWslVersionUrl = "https://learn.microsoft.com/windows/wsl/basic-commands#set-wsl-version-to-1-or-2";

    static readonly string WslPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "wsl.exe");

    const string WslMountPoint = "/var/run/usbipd-win";

    internal sealed record Distribution(string Name, bool IsDefault, uint Version, bool IsRunning);

    static readonly char[] CtrlC = ['\x03'];

    static string? GetPossibleBlockReason()
    {
        using var publicProfile = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\WindowsFirewall\PublicProfile");
        if (publicProfile is not null)
        {
            if (publicProfile.GetValue("DoNotAllowExceptions") is int doNotAllowExceptions && doNotAllowExceptions != 0)
            {
                return "A group policy blocks all incoming connections for the public network profile, which includes WSL.";
            }
            if (publicProfile.GetValue("AllowLocalPolicyMerge") is int allowLocalPolicyMerge && allowLocalPolicyMerge == 0)
            {
                return "A group policy blocks the 'usbipd' firewall rule for the public network profile, which includes WSL.";
            }
        }
        else
        {
            // Only if PublicProfile does not exist, the StandardProfile settings are used.
            // See: https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-gpfas/abe4eb0f-e3a0-48cc-bde3-5dc89b81b40b
            using var standardProfile = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\WindowsFirewall\StandardProfile");
            if (standardProfile is not null)
            {
                if (standardProfile.GetValue("DoNotAllowExceptions") is int doNotAllowExceptions && doNotAllowExceptions != 0)
                {
                    return "A group policy blocks all incoming connections for the standard network profile, which includes WSL.";
                }
                // AllowLocalPolicyMerge is not valid for the StandardProfile
                // See: https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-gpfas/2c979624-900a-4b6e-b4ef-09b387cd62ab
            }
        }

        using var settings = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\PublicProfile");
        if (settings is not null)
        {
            if (settings.GetValue("DoNotAllowExceptions") is int doNotAllowExceptions && doNotAllowExceptions != 0)
            {
                return "Windows Firewall is configured to block all incoming connections for the public network profile, which includes WSL.";
            }
        }

        return null;
    }

    static async Task<(int ExitCode, string StandardOutput, string StandardError, MemoryStream BinaryOutput)>
        RunWslAsync((string distribution, string directory)? linux, Action<string, bool>? outputCallback, bool binaryOutput,
        CancellationToken cancellationToken, params string[] arguments)
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
        var memoryStream = new MemoryStream();

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

        async Task CaptureBinary(Stream stream)
        {
            await process.StandardOutput.BaseStream.CopyToAsync(memoryStream, cancellationToken);
            memoryStream.Position = 0;
        }

        var captureTasks = new[]
        {
            binaryOutput ? CaptureBinary(process.StandardOutput.BaseStream) : OnLine(process.StandardOutput, false),
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
        return new(process.ExitCode, stdout, stderr, memoryStream);
    }

    enum FirewallCheckResult
    {
        Unknown,
        Pass,
        Fail,
    }

    /// <summary>
    /// BusId has already been checked, and the server is running.
    /// </summary>
    public static async Task<ExitCode> Attach(BusId busId, bool autoAttach, string? distribution, IPAddress? hostAddress,
        IConsole console, CancellationToken cancellationToken)
    {
        var wslWindowsPath = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath)!, "WSL");
        if (!Path.Exists(wslWindowsPath))
        {
            console.ReportError($"WSL support was not installed; reinstall this application with the WSL feature enabled.");
            return ExitCode.Failure;
        }
        if ((Path.GetPathRoot(wslWindowsPath) is not string wslWindowsPathRoot) || (!LocalDriveRegex().IsMatch(wslWindowsPathRoot)))
        {
            console.ReportError($"Option '--wsl' requires that this software is installed on a local drive.");
            return ExitCode.Failure;
        }

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
                console.ReportError(
                    $"The WSL distribution '{distribution}' does not exist. Learn how to list all installed distributions at {ListDistributionsUrl}.");
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
            var wslResult = await RunWslAsync((distribution, "/"), null, true, cancellationToken, "/bin/ls", "--directory", "/sys/bus/platform/drivers/vhci_hcd");
            if (wslResult.ExitCode != 0)
            {
                // No USBIP driver (yet), check for loadable module support.
                wslResult = await RunWslAsync((distribution, "/"), null, true, cancellationToken, "/bin/ls", "/proc/modules");
                if (wslResult.ExitCode != 0)
                {
                    // No USBIP driver, and no loadable module support.
                    // Must be an old WSL version, or a misconfigured custom kernel.
                    console.ReportError($"WSL kernel is not USBIP capable; update with 'wsl --update'.");
                    return ExitCode.Failure;
                }
                console.ReportInfo($"Loading vhci_hcd module.");
                wslResult = await RunWslAsync((distribution, "/"), null, false, cancellationToken, "/sbin/modprobe", "vhci_hcd");
                if (wslResult.ExitCode != 0)
                {
                    // Must be an old WSL version, or a misconfigured custom kernel.
                    console.ReportError($"Loading vhci_hcd failed; update with 'wsl --update'.");
                    return ExitCode.Failure;
                }
            }
        }

        // Ensure our wsl directory is mounted.
        // NOTE: This should resolve all issues for users that modified [automount], such as:
        //       disabled automount, mounting at weird locations, mounting non-executable, etc.
        // NOTE: We don't know the shell type (for example, docker-desktop does not even have bash),
        //       so be as portable as possible: single line, use 'test', quote all paths, etc.
        {
            var wslResult = await RunWslAsync((distribution, "/"), null, false, cancellationToken, "/bin/sh", "-c", $$"""
                if ! test -d "{{WslMountPoint}}"; then
                    mkdir -m 0000 "{{WslMountPoint}}";
                fi;
                if ! test -f "{{WslMountPoint}}/README.md"; then
                    mount -t drvfs -o "ro,umask=222" "{{wslWindowsPath}}" "{{WslMountPoint}}";
                fi;
                """.ReplaceLineEndings(" "));
            if (wslResult.ExitCode != 0)
            {
                console.ReportError($"Mounting '{wslWindowsPath}' within WSL failed.");
                return ExitCode.Failure;
            }
        }

        // Check: our distribution-independent usbip client must be runnable.
        {
            var wslResult = await RunWslAsync((distribution, WslMountPoint), null, false, cancellationToken, "./usbip", "version");
            if (wslResult.ExitCode != 0 || wslResult.StandardOutput.Trim() != "usbip (usbip-utils 2.0)")
            {
                console.ReportError($"Unable to run 'usbip' client tool. Please report this at https://github.com/dorssel/usbipd-win/issues.");
                return ExitCode.Failure;
            }
        }

        // Now find out the IP address of the host (if not explicitly provided by the user).
        if (hostAddress is null)
        {
            var wslResult = await RunWslAsync((distribution, "/"), null, false, cancellationToken, "/bin/wslinfo", "--networking-mode");
            string networkingMode;
            if (wslResult.ExitCode == 0)
            {
                networkingMode = wslResult.StandardOutput.Trim();
                console.ReportInfo($"Detected networking mode '{networkingMode}'.");
            }
            else
            {
                networkingMode = "nat";
                console.ReportWarning($"Unable to determine networking mode, assuming 'nat'.");
            }
            switch (networkingMode)
            {
                case "none":
                case "virtioproxy":
                default:
                    console.ReportError($"Networking mode '{networkingMode}' is not supported.");
                    return ExitCode.Failure;
                case "mirrored":
                    hostAddress = IPAddress.Loopback;
                    break;
                case "nat":
                    // See https://learn.microsoft.com/en-us/windows/wsl/networking
                    // We need to get the default gateway address.
                    // We use 'cat /proc/net/route', where we assume 'cat' is available on all distributions
                    //      and /proc/net/route is supported by the WSL kernel.
                    var ipResult = await RunWslAsync((distribution, "/"), null, false, cancellationToken, "/bin/cat", "/proc/net/route");
                    if (ipResult.ExitCode == 0)
                    {
                        // Example output:
                        //
                        // Iface   Destination     Gateway         Flags   RefCnt  Use     Metric  Mask            MTU     Window  IRTT
                        //
                        // eth0    00000000        01E01AAC        0003    0       0       0       00000000        0       0       0
                        //
                        // eth0    00E01AAC        00000000        0001    0       0       0       00F0FFFF        0       0       0

                        for (var match = RouteRegex().Match(ipResult.StandardOutput); match.Success; match = match.NextMatch())
                        {
                            if (uint.TryParse(match.Groups[2].Value, NumberStyles.HexNumber, null, out var destination) && destination == 0
                                && uint.TryParse(match.Groups[3].Value, NumberStyles.HexNumber, null, out var gateway))
                            {
                                hostAddress = new IPAddress(gateway);
                                break;
                            }
                        }
                    }
                    break;
            }
        }
        if (hostAddress is null)
        {
            console.ReportError("Unable to determine host address.");
            return ExitCode.Failure;
        }

        console.ReportInfo($"Using IP address {hostAddress} to reach the host.");

        // Heuristic firewall check
        {
            // The current timeout is two seconds.
            // This used to be one second, but some users got false results due to WSL being slow to start the command.
            using var timeoutTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            using var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutTokenSource.Token);
            FirewallCheckResult result;
            try
            {
                // With minimal requirements (bash only) try to connect from WSL to our server.
                var pingResult = await RunWslAsync((distribution, "/"), null, false, linkedTokenSource.Token, "/bin/bash", "-c",
                    $"echo < /dev/tcp/{hostAddress}/{Interop.UsbIp.USBIP_PORT}");
                if (pingResult.StandardError.Contains("refused"))
                {
                    // If the output contains "refused", then the test was executed and failed, irrespective of the exit code.
                    result = FirewallCheckResult.Fail;
                }
                else if (pingResult.ExitCode == 0)
                {
                    // The test was executed, and returned within the timeout, and the connection was not actively refused (see above).
                    result = FirewallCheckResult.Pass;
                }
                else
                {
                    // The test was not executed properly (bash unavailable, /dev/tcp not supported, etc.).
                    result = FirewallCheckResult.Unknown;
                }
            }
            catch (OperationCanceledException) when (timeoutTokenSource.IsCancellationRequested)
            {
                // Timeout, probably a firewall dropping the connection request (i.e., not actively refused (DENY), but DROP).
                result = FirewallCheckResult.Fail;
            }
            switch (result)
            {
                case FirewallCheckResult.Unknown:
                default:
                    {
                        console.ReportInfo($"Firewall check not possible with this distribution (no bash, or wrong version of bash).");
                        // Try to detect any (domain) policy blockers.
                        if (GetPossibleBlockReason() is string blockReason)
                        {
                            // We found a possible blocker.
                            console.ReportWarning(blockReason);
                        }
                    }
                    break;

                case FirewallCheckResult.Fail:
                    {
                        if (GetPossibleBlockReason() is string blockReason)
                        {
                            // We found a possible reason.
                            console.ReportWarning(blockReason);
                        }
                        // In any case, it isn't working...
                        console.ReportWarning($"A firewall appears to be blocking the connection; ensure TCP port {Interop.UsbIp.USBIP_PORT} is allowed.");
                    }
                    break;

                case FirewallCheckResult.Pass:
                    // All is well.
                    break;
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
                console.ReportWarning(
                    "The device appears to be used by Windows; stop the software using the device, or bind the device using the '--force' option.");
            }
        }

        // Finally, call 'usbip attach', or run the auto-attach.sh script.
        if (!autoAttach)
        {
            var wslResult = await RunWslAsync((distribution, WslMountPoint), FilterUsbip, false, cancellationToken, "./usbip", "attach",
                $"--remote={hostAddress}", $"--busid={busId}");
            if (wslResult.ExitCode != 0)
            {
                console.ReportError($"Failed to attach device with busid '{busId}'.");
                return ExitCode.Failure;
            }
        }
        else
        {
            console.ReportInfo("Starting endless attach loop; press Ctrl+C to quit.");

            _ = await RunWslAsync((distribution, WslMountPoint), FilterUsbip, false, cancellationToken, "./auto-attach.sh", hostAddress.ToString(),
                busId.ToString());
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

    [GeneratedRegex(@"\s*(\S+)\s+(\S+)\s+(\S+)\s+(\S+)\s+(\S+)\s+(\S+)\s+(\S+)\s+(\S+)\s+(\S+)\s+(\S+)\s+(\S+)\s*")]
    private static partial Regex RouteRegex();

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
        var detailsResult = await RunWslAsync(null, null, false, cancellationToken, "--list", "--all", "--verbose");
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
                    if (name == "docker-desktop-data")
                    {
                        // NOTE: docker-desktop-data is unusable
                        continue;
                    }
                    distributions.Add(new(name, isDefault, version, isRunning));
                }
                break;

            case 1:
                // This is returned by the default wsl.exe placeholder that is available on newer versions of Windows 10 even if WSL is not installed.
                // At least, that seems to be the case; it turns out that the wsl.exe command line interface isn't stable.

                // Newer versions of wsl.exe support the --status command.
                if ((await RunWslAsync(null, null, false, cancellationToken, "--status")).ExitCode != 0)
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
