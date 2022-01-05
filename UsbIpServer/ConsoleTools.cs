// SPDX-FileCopyrightText: 2022 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-2.0-only

using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Text;

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
        static void ReportText(IConsole console, string level, string text)
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

        public static void ReportError(IConsole console, string text)
        {
            using var color = new TemporaryColor(console, ConsoleColor.Red);
            ReportText(console, "error", text);
        }

        public static void ReportWarning(IConsole console, string text)
        {
            using var color = new TemporaryColor(console, ConsoleColor.Yellow);
            ReportText(console, "warning", text);
        }

        public static void ReportInfo(IConsole console, string text)
        {
            using var color = new TemporaryColor(console, ConsoleColor.DarkGray);
            ReportText(console, "info", text);
        }

        /// <summary>
        /// Helper to warn users that the service is not running.
        /// For commands that may lead the user to believe that everything is fine when in fact it is not.
        /// For example: 'list' succeeds and shows 'Shared', but attaching from the client will fail.
        /// For example: 'bind' succeeds, but attaching from the client will fail.
        /// </summary>
        public static void ReportServerRunning(IConsole console)
        {
            if (!Server.IsServerRunning())
            {
                ReportWarning(console, "Server is currently not running.");
            }
        }
    }
}
