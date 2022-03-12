// SPDX-FileCopyrightText: 2022 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-2.0-only

using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Linq;
using System.Text;
using Microsoft.Win32;
using static System.CommandLine.IO.StandardStreamWriter;
using static UsbIpServer.Interop.VBoxUsbMon;

namespace UsbIpServer
{
    static class ConsoleTools
    {
        public static IEnumerable<string> Wrap(string text, int width)
        {
            var lineBuilder = new StringBuilder(Math.Min(text.Length, width) + 2);

            var FirstWord = (string word) =>
            {
                lineBuilder.Append(word);
                if (word.Length == width)
                {
                    // If the first word on a line is exactly the original width, add an extra space
                    // so Windows Terminal does not glue the next word to it on resize.
                    lineBuilder.Append(' ');
                }
            };

            var Flush = () =>
            {
                var result = lineBuilder.ToString();
                lineBuilder.Clear();
                return result;
            };

            foreach (var line in text.Split('\n'))
            {
                foreach (var word in line.Split(' '))
                {
                    if (lineBuilder.Length == 0)
                    {
                        FirstWord(word);
                    }
                    else if (lineBuilder.Length + 1 + word.Length >= width)
                    {
                        // Need to stay *under* the width so Windows Terminal does not automatically
                        // glue the first word of the next line to this line without whitespace on resize.
                        yield return Flush();
                        FirstWord(word);
                    }
                    else
                    {
                        lineBuilder.Append(' ').Append(word);
                    }
                }
                yield return Flush();
            }
        }

        public static void WriteTruncated(this IConsole console, string text, int width, bool fill)
        {
            // Console: depending on terminal / font / etc. international characters can take more than 1 cell.
            // Redirected: just assume every character has width 1.
            var measureConsole = !console.IsOutputRedirected;
            if (measureConsole)
            {
                // We need at least 2 extra characters to be able to measure the console output:
                // international characters may take up 2 cells, and the cursor should not wrap around yet.
                if (Console.CursorLeft + width + 2 >= Console.WindowWidth)
                {
                    // The console is not wide enough; we cannot measure across line wrapping.
                    measureConsole = false;
                }
            }
            if (measureConsole)
            {
                var start = Console.CursorLeft;
                foreach (var c in text)
                {
                    console.Out.Write($"{c}");
                    if (Console.CursorLeft - start > width)
                    {
                        Console.CursorLeft = start + width - 3;
                        console.Write("...");
                        break;
                    }
                }
                if (fill)
                {
                    console.Write(new string(' ', width - (Console.CursorLeft - start)));
                }
            }
            else
            {
                if (text.Length > width)
                {
                    console.Write(text[..(width - 3)]);
                    console.Write("...");
                }
                else
                {
                    console.Write(text);
                    if (fill)
                    {
                        console.Write(new string(' ', width - text.Length));
                    }
                }
            }
        }

        /// <summary>
        /// <para><see cref="CommandLineApplication"/> is rather old and uses the "old style" errors without a terminating period.</para>
        /// <para>Some WinAPI errors (originating from FormatMessage) have a terminating newline.</para>
        /// This function normalizes all errors to
        /// <list type="bullet">
        /// <item>end with a period (.)</item>
        /// <item>not end with a newline</item>
        /// </list>
        /// </summary>
        static string EnforceFinalPeriod(this string s)
        {
            s = s.TrimEnd();
            return s.EndsWith('.') ? s : s + '.';
        }

        /// <summary>
        /// All "console logging" reports go to <see cref="Console.Error"/>, so they can be easily
        /// separated from expected output, e.g. from 'list', which goes to <see cref="Console.Out"/>.
        /// </summary>
        static void ReportText(this IConsole console, string level, string text)
        {
            console.Error.WriteLine($"{Program.ApplicationName}: {level}: {EnforceFinalPeriod(text)}");
        }

        sealed class TemporaryColor
            : IDisposable
        {
            readonly bool NeedReset;

            public TemporaryColor(IConsole console, ConsoleColor color)
            {
                if (console.IsErrorRedirected)
                {
                    return;
                }
                Console.ForegroundColor = color;
                NeedReset = true;
            }

            public void Dispose()
            {
                if (NeedReset)
                {
                    Console.ResetColor();
                }
            }
        }

        public static void ReportError(this IConsole console, string text)
        {
            using var color = new TemporaryColor(console, ConsoleColor.Red);
            console.ReportText("error", text);
        }

        public static void ReportWarning(this IConsole console, string text)
        {
            using var color = new TemporaryColor(console, ConsoleColor.Yellow);
            console.ReportText("warning", text);
        }

        public static void ReportInfo(this IConsole console, string text)
        {
            using var color = new TemporaryColor(console, ConsoleColor.DarkGray);
            console.ReportText("info", text);
        }

        const string ServiceNotRunningText = "The service is currently not running; a reboot should fix that.";
        const string VBoxUsbMonNotRunningText = "The VBoxUsbMon driver is currently not running; a reboot should fix that.";
        const string VBoxUsbMonNotInstalled = "The VBoxUsbMon driver is not correctly installed; a repair or re-install of this software should fix that.";

        public static void ReportVersionNotSupported(IConsole console, UsbSupVersion version)
        {
            console.ReportError($"VBoxUsbMon version {version.major}.{version.minor} is not supported; this must be resolved first.");
        }

        /// <summary>
        /// Helper to warn users that the service is not running.
        /// For commands that may lead the user to believe that everything is fine when in fact it is not.
        ///
        /// <para>
        /// For example: 'list' succeeds and shows 'Shared', but attaching from the client will fail.
        /// </para>
        /// <para>
        /// For example: 'bind' succeeds, but attaching from the client will fail.
        /// </para>
        ///
        /// <para>
        /// Things that can be fixed by a simple reboot are reported as a warning. More serious stuff as an error.
        /// </para>
        /// </summary>
        public static void ReportIfServerNotRunning(this IConsole console)
        {
            if (!VBoxUsbMon.IsServiceInstalled())
            {
                // We rely on the DIFX driver installer framework and on the (suboptimal) VBoxUsbMon.inf.
                // Sometimes, DIFX cannot create the VBoxUsbMon service, but still appears to succeed.
                console.ReportError(VBoxUsbMonNotInstalled);
            }
            else if (!Server.IsRunning())
            {
                console.ReportWarning(ServiceNotRunningText);
            }
            else if (VBoxUsbMon.GetRunningVersion() is not UsbSupVersion version)
            {
                // The usbipd service has a dependency on VBoxUsbMon, but we can still get here when running the server from the command line.
                console.ReportWarning(VBoxUsbMonNotRunningText);
            }
            else if (!VBoxUsbMon.IsVersionSupported(version))
            {
                // This may happen if a full installation of (a rather old version of) VirtualBox interferes.
                ReportVersionNotSupported(console, version);
            }
        }

        public static bool CheckServerRunning(IConsole console)
        {
            if (!VBoxUsbMon.IsServiceInstalled())
            {
                // We rely on the DIFX driver installer framework and on the (suboptimal) VBoxUsbMon.inf.
                // Sometimes, DIFX cannot create the VBoxUsbMon service, but still appears to succeed.
                console.ReportError(VBoxUsbMonNotInstalled);
                return false;
            }
            if (!Server.IsRunning())
            {
                console.ReportError(ServiceNotRunningText);
                return false;
            }
            if (VBoxUsbMon.GetRunningVersion() is not UsbSupVersion version)
            {
                // The usbipd service has a dependency on VBoxUsbMon, but we can still get here when running the server from the command line.
                console.ReportError(VBoxUsbMonNotRunningText);
                return false;
            }
            if (!VBoxUsbMon.IsVersionSupported(version))
            {
                // This may happen if a full installation of (a rather old version of) VirtualBox interferes.
                ReportVersionNotSupported(console, version);
                return false;
            }
            return true;
        }

        static readonly SortedSet<string> WhitelistUpperFilters = new();

        static readonly SortedSet<string> BlacklistUpperFilters = new()
        {
            "EUsbHubFilter",
            "TsUsbFlt",
            "UsbDk",
            "USBPcap",
        };

        const string UpperFiltersPath = @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Class\{36fc9e60-c465-11cf-8056-444553540000}";
        const string UpperFiltersName = @"UpperFilters";

        /// <summary>
        /// Helper to warn users that the service is not running.
        /// For commands that may lead the user to believe that everything is fine when in fact it is not.
        /// For example: 'list' succeeds and shows 'Shared', but attaching from the client will fail.
        /// For example: 'bind' succeeds, but attaching from the client will fail.
        /// </summary>
        public static void ReportIfForceNeeded(this IConsole console)
        {
            var upperFilters = Registry.GetValue(UpperFiltersPath, UpperFiltersName, null) as string[] ?? Array.Empty<string>();
            foreach (var filter in new SortedSet<string>(upperFilters.Where(f => !string.IsNullOrWhiteSpace(f)), StringComparer.InvariantCultureIgnoreCase))
            {
                if (BlacklistUpperFilters.Contains(filter, StringComparer.InvariantCultureIgnoreCase))
                {
                    console.ReportWarning($"USB filter '{filter}' is known to be incompatible with this software; 'bind --force' will be required.");
                }
                else if (!WhitelistUpperFilters.Contains(filter, StringComparer.InvariantCultureIgnoreCase))
                {
                    console.ReportWarning($"Unknown USB filter '{filter}' may be incompatible with this software; 'bind --force' may be required.");
                }
            }
        }

        public static void ReportRebootRequired(this IConsole console)
        {
            console.ReportWarning("A reboot may be required before the changes take effect.");
        }

        public static bool CheckWriteAccess(IConsole console)
        {
            if (!RegistryUtils.HasWriteAccess())
            {
                console.ReportError("Access denied; this operation requires administrator privileges.");
                return false;
            }
            return true;
        }
    }
}
