// SPDX-FileCopyrightText: Microsoft Corporation
// SPDX-FileCopyrightText: 2022 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-2.0-only

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace UsbIpServer
{
    static class ProcessUtils
    {
        public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);

        /// <summary>
        /// <para>
        /// Our process is usually wsl.exe running something within Linux. This function tries to kill all of it.
        /// First, Ctrl+C is blindly sent to the Linux process; we allow 100ms for wsl.exe to pass it on.
        /// Regardless of the outcome, we then kill the entire Windows process tree.
        /// </para>
        /// <para>
        /// If all went well, there are no left-over processes either on Linux or Windows.
        /// Worst case: the Linux process didn't receive or respond to the Ctrl+C and is still running.
        /// </para>
        /// <para>
        /// In any case: the Windows process (the one that <paramref name="process"/> references) is dead.
        /// </para>
        /// </summary>
        static async Task TerminateProcess(Process process)
        {
            // This should be enough time for the Ctrl+C to pass through. If not, too bad.
            using var remoteTimeoutTokenSource = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
            try
            {
                // Fire-and-forget Ctrl+C, this *should* terminate any Linux process.
                // If this is not a remote command execution, then the local Windows process gets it free of charge.
                await process.StandardInput.WriteAsync(new[] { '\x03' }, remoteTimeoutTokenSource.Token);
                process.StandardInput.Close();
                await process.WaitForExitAsync(remoteTimeoutTokenSource.Token);
            }
            catch (OperationCanceledException) { }
            finally
            {
                // Kill the entire Windows process tree, just in case it hasn't exited already.
                process.Kill(true);
            }
        }

        public static async Task<ProcessResult> RunCapturedProcessAsync(string filename, IEnumerable<string> arguments, Encoding encoding, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var startInfo = CreateCommonProcessStartInfo(filename, arguments);
            startInfo.StandardOutputEncoding = encoding;
            startInfo.StandardErrorEncoding = encoding;
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;

            using var process = Process.Start(startInfo);
            ThrowIf(process is null, filename, arguments);

            var stdout = string.Empty;
            var stderr = string.Empty;

            var captureTasks = new[]
            {
                Task.Run(async () => { stdout = await process.StandardOutput.ReadToEndAsync(); }, cancellationToken),
                Task.Run(async () => { stderr = await process.StandardError.ReadToEndAsync(); }, cancellationToken),
            };

            try
            {
                await process.WaitForExitAsync(cancellationToken);
            }
            finally
            {
                await TerminateProcess(process);
            }

            // Since the local process either completed or was killed, these should complete or cancel promptly.
            await Task.WhenAll(captureTasks);

            cancellationToken.ThrowIfCancellationRequested();
            return new(process.ExitCode, stdout, stderr);
        }

        public static async Task<int> RunUncapturedProcessAsync(string filename, IEnumerable<string> arguments, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var process = Process.Start(CreateCommonProcessStartInfo(filename, arguments));
            ThrowIf(process is null, filename, arguments);

            try
            {
                await process.WaitForExitAsync(cancellationToken);
            }
            finally
            {
                await TerminateProcess(process);
            }
            cancellationToken.ThrowIfCancellationRequested();
            return process.ExitCode;
        }

        static ProcessStartInfo CreateCommonProcessStartInfo(string filename, IEnumerable<string> arguments)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = filename,
                UseShellExecute = false,
                // None of our commands require user input from the real console.
                StandardInputEncoding = Encoding.ASCII,
                RedirectStandardInput = true,
            };

            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            return startInfo;
        }

        [ExcludeFromCodeCoverage(Justification = "Not testable with UseShellExecute = false")]
        static void ThrowIf([DoesNotReturnIf(true)] bool processIsNull, string filename, IEnumerable<string> arguments)
        {
            if (processIsNull)
            {
                throw new UnexpectedResultException($"Failed to start \"{filename}\" with arguments {string.Join(" ", arguments.Select(arg => $"\"{arg}\""))}.");
            }
        }
    }
}
