// SPDX-FileCopyrightText: 2022 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-2.0-only

using System.Diagnostics;
using System.IO;
using System.Management.Automation;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading.Tasks;
using Usbipd.Automation;

namespace Usbipd.PowerShell
{
    [Cmdlet(VerbsCommon.Get, "UsbipdDevice")]
    [OutputType(typeof(Device))]
    public class GetUsbipdDeviceCommand : Cmdlet
    {
        State State = new();

        protected override void BeginProcessing()
        {
            WriteDebug($"Detected installation path: {Installation.ExePath}");

            var startInfo = new ProcessStartInfo
            {
                FileName = Installation.ExePath,
                UseShellExecute = false,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                Arguments = "state",
            };

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                throw new ApplicationFailedException($"Cannot execute '{Installation.ExePath}'.");
            }

            var stdout = string.Empty;
            var stderr = string.Empty;

            var captureTasks = new[]
            {
                Task.Run(async () => { stdout = await process.StandardOutput.ReadToEndAsync(); }),
                Task.Run(async () => { stderr = await process.StandardError.ReadToEndAsync(); }),
            };

            process.WaitForExit();
            Task.WhenAll(captureTasks).Wait();

            if (process.ExitCode != 0)
            {
                throw new ApplicationFailedException($"usbipd failed with exit code {process.ExitCode}.");
            }
            if (!string.IsNullOrEmpty(stderr))
            {
                throw new ApplicationFailedException($"usbipd returned unexpected error text:\n\n{stderr}");
            }

            WriteDebug(stdout);

            var serializer = new DataContractJsonSerializer(typeof(State));
            using var memoryStream = new MemoryStream(Encoding.UTF8.GetBytes(stdout));
            State = (State)serializer.ReadObject(memoryStream);
        }

        protected override void ProcessRecord()
        {
            foreach (var d in State.Devices)
            {
                WriteObject(d);
            }
        }
    }
}
