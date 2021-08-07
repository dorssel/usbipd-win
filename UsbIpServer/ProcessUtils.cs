using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace UsbIpServer
{
    static class ProcessUtils
    {
        public record ProcessResult(int ExitCode, string StandardOutput, string StandardError);

        public static async Task<ProcessResult> RunCapturedProcessAsync(string filename, IEnumerable<string> arguments, Encoding encoding, CancellationToken cancellationToken)
        {
            var startInfo = CreateCommonProcessStartInfo(filename, arguments);
            startInfo.StandardOutputEncoding = encoding;
            startInfo.StandardErrorEncoding = encoding;
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;

            using var process = new Process { StartInfo = startInfo };

            if (!process.Start())
            {
                throw new UnexpectedResultException(FormatStartFailedMessage(filename, arguments));
            }

            var stdout = string.Empty;
            var stderr = string.Empty;

            await Task.WhenAll(
                Task.Run(async () => { stdout = await process.StandardOutput.ReadToEndAsync(); }, cancellationToken),
                Task.Run(async () => { stderr = await process.StandardError.ReadToEndAsync(); }, cancellationToken),
                process.WaitForExitAsync(cancellationToken));

            return new ProcessResult(process.ExitCode, stdout, stderr);
        }

        public static async Task<int> RunUncapturedProcessAsync(string filename, IEnumerable<string> arguments, CancellationToken cancellationToken)
        {
            using var process = Process.Start(CreateCommonProcessStartInfo(filename, arguments));

            if (process == null)
            {
                throw new UnexpectedResultException(FormatStartFailedMessage(filename, arguments));
            }

            await process.WaitForExitAsync(cancellationToken);
            return process.ExitCode;
        }

        static ProcessStartInfo CreateCommonProcessStartInfo(string filename, IEnumerable<string> arguments)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = filename,
                UseShellExecute = false,
            };

            foreach (string argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            return startInfo;
        }

        static string FormatStartFailedMessage(string filename, IEnumerable<string> arguments)
        {
            return $"Failed to start \"{filename}\" with arguments {string.Join(" ", arguments.Select(arg => $"\"{arg}\""))}.";
        }
    }
}
