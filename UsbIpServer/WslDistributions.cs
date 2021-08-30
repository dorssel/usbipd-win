// SPDX-FileCopyrightText: Copyright (c) Microsoft Corporation
//
// SPDX-License-Identifier: GPL-2.0-only

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace UsbIpServer
{
    class WslDistributions
    {
        public static readonly string WslPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "wsl.exe");

        public record Distribution(string Name, bool IsDefault, uint Version, bool IsRunning, IPAddress? IPAddress);

        public static bool IsWsl2Installed()
        {
            // Since WSL 2, the wsl.exe command is used to manage WSL.
            // And since USBIP requires a real kernel (i.e. WSL 2), we may safely assume that wsl.exe is available.
            // Users with older (< 1903) Windows will simply get a report that WSL 2 is not available,
            //    even if they have WSL (version 1) installed. It won't work for them anyway.
            // We won't bother checking for the older wslconfig.exe that was used to manage WSL 1.
            if (!File.Exists(WslPath))
            {
                return false;
            }
            // On recent Windows 10, wsl.exe will be available even if the WSL feature is not installed.
            // In fact, 'wsl.exe --install' can be used to install the feature...
            using var searcher = new ManagementObjectSearcher(@"SELECT * FROM Win32_OptionalFeature WHERE Name = 'Microsoft-Windows-Subsystem-Linux' AND InstallState = 1");
            using var collection = searcher.Get();
            return collection.Count == 1;
        }

        WslDistributions(List<Distribution> distributions, IPAddress? hostAddress)
        {
            Distributions = distributions;
            HostAddress = hostAddress;
        }

        public IReadOnlyCollection<Distribution> Distributions { get; }

        public IPAddress? HostAddress { get; }

        public Distribution? DefaultDistribution => Distributions.FirstOrDefault((distro) => distro.IsDefault);

        static bool IsOnSameIPv4Network(UnicastIPAddressInformation wslHost, IPAddress wslInstance)
        {
            if (wslHost.Address.AddressFamily != AddressFamily.InterNetwork
                || wslInstance.AddressFamily != AddressFamily.InterNetwork)
            {
                return false;
            }

            // NOTE: we don't care about byte order here
            var rawHost = BitConverter.ToUInt32(wslHost.Address.GetAddressBytes());
            var rawInstance = BitConverter.ToUInt32(wslInstance.GetAddressBytes());
            var rawMask = BitConverter.ToUInt32(wslHost.IPv4Mask.GetAddressBytes());
            return (rawHost & rawMask) == (rawInstance & rawMask);
        }

        public static async Task<WslDistributions> CreateAsync(CancellationToken cancellationToken)
        {
            var wslHost = NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(nic => nic.Name.Contains("WSL", StringComparison.OrdinalIgnoreCase))?.GetIPProperties().UnicastAddresses
                .FirstOrDefault(ip => ip.Address.AddressFamily == AddressFamily.InterNetwork);

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
            if (detailsResult.ExitCode != 0)
            {
                throw new UnexpectedResultException($"WSL failed to list distributions: {detailsResult.StandardError}");
            }
            var details = detailsResult.StandardOutput.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

            // Sanity check
            if (!Regex.IsMatch(details.FirstOrDefault() ?? string.Empty, "^  NAME +STATE +VERSION *$"))
            {
                throw new UnexpectedResultException($"WSL failed to parse distributions: {detailsResult.StandardOutput}");
            }

            var distros = new List<Distribution>();
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
                    // 'hostname' is run on the WSL instance and returns a <space> separated list of addresses, both IPv4 and IPv6.
                    var ipResult = await ProcessUtils.RunCapturedProcessAsync(WslPath, new[] { "--distribution", name, "--", "hostname", "--all-ip-addresses" }, Encoding.UTF8, cancellationToken);
#pragma warning disable CA1508 // Avoid dead conditional code (false positive)
                    if (ipResult.ExitCode == 0)
#pragma warning restore CA1508 // Avoid dead conditional code
                    {
                        foreach (var a in ipResult.StandardOutput.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        {
                            if (!IPAddress.TryParse(a, out var wslInstance))
                            {
                                continue;
                            }
                            if (IsOnSameIPv4Network(wslHost, wslInstance))
                            {
                                address = wslInstance;
                                break;
                            }
                        }
                    }
                }

                distros.Add(new Distribution(name, isDefault, version, isRunning, address));
            }

            return new WslDistributions(distros, wslHost?.Address);
        }

        public Distribution? LookupByName(string name) => Distributions.FirstOrDefault(distro => distro.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        public Distribution? LookupByIPAddress(IPAddress address) => Distributions.FirstOrDefault(distro => address.Equals(distro.IPAddress));
    }
}
