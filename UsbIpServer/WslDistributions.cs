// SPDX-FileCopyrightText: Copyright (c) Microsoft Corporation
//
// SPDX-License-Identifier: GPL-2.0-only

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace UsbIpServer
{
    class WslDistributions
    {
        public static readonly string WslPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "wsl.exe");

        public record Distribution(string Name, IPAddress IPAddress, ulong? Version);

        public static bool IsWslInstalled() => File.Exists(WslPath);

        readonly string? defaultDistro;

        WslDistributions(List<Distribution> distributions, string? defaultDistro)
        {
            this.Distributions = distributions;
            this.defaultDistro = defaultDistro;
        }

        public IReadOnlyCollection<Distribution> Distributions { get; }

        public Distribution? DefaultDistribution => defaultDistro != null ? LookupByName(defaultDistro) : null;

        public static async Task<WslDistributions> CreateAsync(CancellationToken cancellationToken)
        {
            var allDistrosResult = await ProcessUtils.RunCapturedProcessAsync(WslPath, new[] { "--list" }, Encoding.Unicode, cancellationToken);
            if (allDistrosResult.ExitCode != 0)
            {
                throw new UnexpectedResultException($"WSL failed to list distributions: {allDistrosResult.StandardError}");
            }

            // Sample output:
            //   Windows Subsystem for Linux Distributions:
            //   Ubuntu(Default)
            // We skip the first line, then look for a "(" to mark the default distro.
            // This is similar to how Windows Terminal handles WSL.
            // https://github.com/microsoft/terminal/blob/9e83655b0870f7964789a7a17ccfd232cab4945a/src/cascadia/TerminalSettingsModel/WslDistroGenerator.cpp#L120
            var defaultDistro = allDistrosResult.StandardOutput.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
                .Skip(1).FirstOrDefault(d => d.Contains('(', StringComparison.Ordinal));
            if (defaultDistro != null)
            {
                var firstNonNameChar = defaultDistro.IndexOf('(', StringComparison.Ordinal);
                if (firstNonNameChar != -1)
                {
                    defaultDistro = defaultDistro.Substring(0, firstNonNameChar).Trim();
                }
            }

            var runningDistrosResult = await ProcessUtils.RunCapturedProcessAsync(WslPath, new[] { "--list", "--quiet", "--running" }, Encoding.Unicode, cancellationToken);

            // The compiler erroneously thinks that this exit code check and the
            // ipResult exit code check below are always false.
#pragma warning disable CA1508 // Avoid dead conditional code
            if (runningDistrosResult.ExitCode != 0)
#pragma warning restore CA1508 // Avoid dead conditional code
            {
                throw new UnexpectedResultException($"WSL failed to list running distributions: {runningDistrosResult.StandardError}");
            }

            var distros = new List<Distribution>();
            foreach (var distroName in runningDistrosResult.StandardOutput.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var ipResult = await ProcessUtils.RunCapturedProcessAsync(WslPath, new[] { "--distribution", distroName, "--", "hostname", "-I" }, Encoding.UTF8, cancellationToken);
#pragma warning disable CA1508 // Avoid dead conditional code
                if (ipResult.ExitCode != 0)
#pragma warning restore CA1508 // Avoid dead conditional code
                {
                    // Ignore this distro if we couldn't get an IP address.
                    continue;
                }

                ulong? version = null;
                try
                {
                    if (NativeWslApi.WslGetDistributionConfiguration(distroName, out ulong knownVersion, out ulong _, out NativeWslApi.WSL_DISTRIBUTION_FLAGS _, out IntPtr _, out ulong _) == 0)
                    {
                        version = knownVersion;
                    }
                }
                catch (DllNotFoundException ex)
                {
                    // For some reason the WSL API couldn't be loaded.
                    // We'll just leave the version set to null (unknown).
                }

                // hostname can include multiple IP addresses, but in most cases there's
                // only one. Since we don't have a good way to figure out which one is the
                // WSL virtual switch, just take the first one and hope it's correct.
                var firstAddress = ipResult.StandardOutput.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();

                if (firstAddress == null || !IPAddress.TryParse(firstAddress, out IPAddress? address))
                {
                    // Ignore this distro if the IP address coudn't be parsed.
                    continue;
                }

                distros.Add(new Distribution(distroName, address, version));
            }

            return new WslDistributions(distros, defaultDistro);
        }

        public Distribution? LookupByName(string name) => this.Distributions.FirstOrDefault(distro => distro.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        public Distribution? LookupByIPAddress(IPAddress address) => this.Distributions.FirstOrDefault(distro => distro.IPAddress.Equals(address));
    }
}
