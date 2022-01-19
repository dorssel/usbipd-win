﻿// SPDX-FileCopyrightText: 2022 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-2.0-only

using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Linq;
using System.Text;
using Microsoft.Win32;
using static System.CommandLine.IO.StandardStreamWriter;

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

        public static string Truncate(this string value, int maxChars)
        {
            return value.Length <= maxChars ? value : string.Concat(value.AsSpan(0, maxChars - 3), "...");
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

        /// <summary>
        /// Helper to warn users that the service is not running.
        /// For commands that may lead the user to believe that everything is fine when in fact it is not.
        /// For example: 'list' succeeds and shows 'Shared', but attaching from the client will fail.
        /// For example: 'bind' succeeds, but attaching from the client will fail.
        /// </summary>
        public static void ReportIfServerNotRunning(this IConsole console)
        {
            if (!Server.IsServerRunning())
            {
                console.ReportWarning("Server is currently not running.");
            }
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
                console.ReportError("Access denied.");
                return false;
            }
            return true;
        }

        public static bool CheckServerRunning(IConsole console)
        {
            if (!Server.IsServerRunning())
            {
                console.ReportError("Server is currently not running.");
                return false;
            }
            return true;
        }
    }
}