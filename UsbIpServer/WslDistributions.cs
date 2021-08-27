// SPDX-FileCopyrightText: Copyright (c) Microsoft Corporation
//
// SPDX-License-Identifier: GPL-2.0-only

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Win32;

namespace UsbIpServer
{
    class WslDistributions
    {
        public static readonly string WslPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "wsl.exe");

        public record Distribution(string Name, IPAddress IPAddress, uint? Version);

        public static bool IsWslInstalled()
        {
            if (!File.Exists(WslPath))
            {
                // Definitely not installed.
                return false;
            }
            // On recent Windows 10, wsl.exe will be available even if the WSL feature is not installed.
            // In fact, 'wsl.exe --install' can be used to install the feature...
            using var searcher = new ManagementObjectSearcher(@"SELECT * FROM Win32_OptionalFeature WHERE Name = 'Microsoft-Windows-Subsystem-Linux' AND InstallState = 1");
            using var collection = searcher.Get();
            return collection.Count == 1;
        }

        readonly string? DefaultDistro;

        WslDistributions(List<Distribution> distributions, string? defaultDistro)
        {
            Distributions = distributions;
            DefaultDistro = defaultDistro;
        }

        public IReadOnlyCollection<Distribution> Distributions { get; }

        public Distribution? DefaultDistribution => DefaultDistro is not null ? LookupByName(DefaultDistro) : null;

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
            if (defaultDistro is not null)
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

                uint? version = null;
                try
                {
                    unsafe
                    {
                        if (PInvoke.WslGetDistributionConfiguration(distroName, out var knownVersion, out _, out _, out _, out _) == Constants.S_OK)
                        {
                            version = knownVersion;
                        }
                    }
                }
                catch (DllNotFoundException)
                {
                    // For some reason the WSL API couldn't be loaded.
                    // We'll just leave the version set to null (unknown).
                }

                // hostname can include multiple IP addresses, but in most cases there's
                // only one. Since we don't have a good way to figure out which one is the
                // WSL virtual switch, just take the first one and hope it's correct.
                var firstAddress = ipResult.StandardOutput.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();

                if (firstAddress is null || !IPAddress.TryParse(firstAddress, out var address))
                {
                    // Ignore this distro if the IP address coudn't be parsed.
                    continue;
                }

                distros.Add(new Distribution(distroName, address, version));
            }

            return new WslDistributions(distros, defaultDistro);
        }

        public Distribution? LookupByName(string name) => Distributions.FirstOrDefault(distro => distro.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        public Distribution? LookupByIPAddress(IPAddress address) => Distributions.FirstOrDefault(distro => distro.IPAddress.Equals(address));
    }
}
