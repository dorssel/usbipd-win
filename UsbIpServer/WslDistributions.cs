// SPDX-FileCopyrightText: Microsoft Corporation
// SPDX-FileCopyrightText: 2021 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-2.0-only

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace UsbIpServer
{
    sealed partial record WslDistributions(IEnumerable<WslDistributions.Distribution> Distributions, IPAddress? HostAddress);

    sealed partial record WslDistributions
    {
        public const string InstallWslUrl = "https://aka.ms/installwsl";
        public const string SetWslVersionUrl = "https://docs.microsoft.com/windows/wsl/basic-commands#set-wsl-version-to-1-or-2";
        public const string WslWikiUrl = "https://github.com/dorssel/usbipd-win/wiki/WSL-support";

        public static readonly string WslPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "wsl.exe");

        public sealed record Distribution(string Name, bool IsDefault, uint Version, bool IsRunning, IPAddress? IPAddress);

        public Distribution? DefaultDistribution => Distributions.FirstOrDefault((distro) => distro.IsDefault);

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

        /// <summary>
        /// Returns null if WSL 2 is not even installed.
        /// </summary>
        public static async Task<WslDistributions?> CreateAsync(CancellationToken cancellationToken)
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

            // The WSL switch only exists if at least one WSL 2 instance is running.
            var wslHost = NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(nic => nic.Name.Contains("WSL", StringComparison.OrdinalIgnoreCase))?.GetIPProperties().UnicastAddresses
                .FirstOrDefault(ip => ip.Address.AddressFamily == AddressFamily.InterNetwork);

            var distros = new List<Distribution>();

            // Get a list of details of available distros (in any state: Stopped, Running, Installing, etc.)
            // This contains all we need (default, name, state, version).
            // NOTE: WslGetDistributionConfiguration() is unreliable getting the version.
            //
            // Sample output:
            //   NAME               STATE           VERSION
            // * Ubuntu             Running         1
            //   Debian             Stopped         2
            //   Custom-MyDistro    Running         2
            var detailsResult = await ProcessUtils.RunCapturedProcessAsync(WslPath, new[] { "--list", "--all", "--verbose" }, Encoding.Unicode, cancellationToken);
            switch (detailsResult.ExitCode)
            {
                case 0:
                    var details = detailsResult.StandardOutput.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

                    // Sanity check
                    if (!Regex.IsMatch(details.FirstOrDefault() ?? string.Empty, "^  NAME +STATE +VERSION *$"))
                    {
                        throw new UnexpectedResultException($"WSL failed to parse distributions: {detailsResult.StandardOutput}");
                    }

                    foreach (var line in details.Skip(1))
                    {
                        var match = Regex.Match(line, @"^( |\*) (.+) +([a-zA-Z]+) +([0-9])+ *$");
                        if (!match.Success)
                        {
                            throw new UnexpectedResultException($"WSL failed to parse distributions: {detailsResult.StandardOutput}");
                        }
                        var isDefault = match.Groups[1].Value == "*";
                        var name = match.Groups[2].Value.TrimEnd();
                        var isRunning = match.Groups[3].Value == "Running";
                        var version = uint.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture);

                        IPAddress? address = null;
                        if (wslHost is not null && isRunning && version == 2)
                        {
                            // We'll do our best to get the instance address on the WSL virtual switch, but we don't fail if we can't.
                            // We use 'cat /proc/net/fib_trie', where we assume 'cat' is available on all distributions and /proc/net/fib_trie is supported by the WSL kernel.
                            var ipResult = await ProcessUtils.RunCapturedProcessAsync(WslPath, new[] { "--distribution", name, "--", "cat", "/proc/net/fib_trie" }, Encoding.UTF8, cancellationToken);
#pragma warning disable CA1508 // Avoid dead conditional code (false positive)
                            if (ipResult.ExitCode == 0)
#pragma warning restore CA1508 // Avoid dead conditional code
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

                                for (match = Regex.Match(ipResult.StandardOutput, @"\|--\s+(\S+)\s+/32 host LOCAL"); match.Success; match = match.NextMatch())
                                {
                                    if (!IPAddress.TryParse(match.Groups[1].Value, out var wslInstance))
                                    {
                                        continue;
                                    }
                                    if (IsOnSameIPv4Network(wslHost.Address, wslHost.IPv4Mask, wslInstance))
                                    {
                                        address = wslInstance;
                                        break;
                                    }
                                }
                            }
                        }

                        distros.Add(new(name, isDefault, version, isRunning, address));
                    }
                    break;

                case 1:
                    // This is returned by the default wsl.exe placeholder that is available on newer versions of Windows 10 even if WSL is not installed.
                    // At least, that seems to be the case; it turns out that the wsl.exe command line interface isn't stable.

                    // Newer versions of wsl.exe support the --status command.
#pragma warning disable CA1508 // Avoid dead conditional code (false positive)
                    if ((await ProcessUtils.RunCapturedProcessAsync(WslPath, new[] { "--status" }, Encoding.Unicode, cancellationToken)).ExitCode != 0)
#pragma warning restore CA1508 // Avoid dead conditional code
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

            return new(distros, wslHost?.Address);
        }

        public Distribution? LookupByName(string name) => Distributions.FirstOrDefault(distro => distro.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        public Distribution? LookupByIPAddress(IPAddress address) => Distributions.FirstOrDefault(distro => address.Equals(distro.IPAddress));
    }
}
