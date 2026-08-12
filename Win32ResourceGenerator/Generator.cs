// SPDX-FileCopyrightText: 2026 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Globalization;
using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis;
using Windows.Win32;
using Windows.Win32.Storage.FileSystem;

namespace Win32ResourceGenerator;

[Generator]
public class Generator : IIncrementalGenerator
{
    readonly string WindowsSDKBuildToolsBinVersionedArchFolder;

    readonly byte[] Resources;

    readonly FileVersionInfo FileVersionInfo;

    readonly string EventManifestTemplate;

    readonly string VersionInfoTemplate;

    const string DefaultXmlNs = "http://schemas.microsoft.com/win/2004/08/events"; // DevSkim: ignore DS137138
    const string WinXmlNs = "http://manifests.microsoft.com/win/2004/08/windows/events"; // DevSkim: ignore DS137138

    const string ProviderName = "usbipd";
    readonly string ProviderGuid;

    public Generator()
    {
        // Initialize some constants.
        {
            // The provider GUID is generated based on the provider name.
            using var eventSource = new EventSource(ProviderName);
            ProviderGuid = eventSource.Guid.ToString("B").ToUpperInvariant();
        }

        // Get some embedded data from the generator assembly.
        {
            var assembly = typeof(Generator).Assembly;
            // MSBuild properties of the source generator project are embedded as custom attributes.
            {
                var metadata = assembly.GetCustomAttributes<AssemblyMetadataAttribute>();
                WindowsSDKBuildToolsBinVersionedArchFolder = metadata.Single(a => a.Key == "WindowsSDKBuildToolsBinVersionedArchFolder").Value;
            }
            // (Template) resource files are embedded as resources.
            {
                var resourceNames = assembly.GetManifestResourceNames();
                {
                    using var stream = assembly.GetManifestResourceStream(resourceNames.Single(r => r.EndsWith(".Resources.rc2")));
                    using var memoryStream = new MemoryStream();
                    stream.CopyTo(memoryStream);
                    Resources = memoryStream.ToArray();
                }
                {
                    using var stream = assembly.GetManifestResourceStream(resourceNames.Single(r => r.EndsWith(".EventManifestTemplate.xml")));
                    using var reader = new StreamReader(stream);
                    EventManifestTemplate = reader.ReadToEnd();
                }
                {
                    using var stream = assembly.GetManifestResourceStream(resourceNames.Single(r => r.EndsWith(".VersionInfoTemplate.rc2")));
                    using var reader = new StreamReader(stream);
                    VersionInfoTemplate = reader.ReadToEnd();
                }
            }
            // Our own FileVersionInfo is used to generate the VersionInfo.rc file.
            FileVersionInfo = FileVersionInfo.GetVersionInfo(assembly.Location);
        }
    }

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // We need to filter out generated sources, which either have no file path or end with .g.cs.
        var loggerMessages = context.SyntaxProvider.ForAttributeWithMetadataName(
            "Microsoft.Extensions.Logging.LoggerMessageAttribute",
            (syntaxNode, _) =>
                !string.IsNullOrEmpty(syntaxNode.SyntaxTree.FilePath)
                && !syntaxNode.SyntaxTree.FilePath.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase),
            (context, _) => LoggerMessage.Create(context));

        var buildOptions = context.AnalyzerConfigOptionsProvider.Select((provider, ct) =>
        {
            _ = provider.GlobalOptions.TryGetValue("build_property.ProjectDir", out var projectDir);
            _ = provider.GlobalOptions.TryGetValue("build_property.intermediateOutputPath", out var intermediateOutputPath);
            _ = provider.GlobalOptions.TryGetValue("build_property.Configuration", out var configuration);
            return new BuildOptions(projectDir ?? string.Empty, intermediateOutputPath ?? string.Empty, configuration ?? string.Empty);
        });

        var loggerMessagesWithOptions = loggerMessages.Collect().Combine(buildOptions);

        context.RegisterSourceOutput(loggerMessagesWithOptions, (context, loggerMessagesWithOptions) =>
        {
            var loggerMessages = loggerMessagesWithOptions.Left;
            var buildOptions = loggerMessagesWithOptions.Right;

            var seenIds = new Dictionary<int, LoggerMessage>();
            var hasErrors = false;

            foreach (var loggerMessage in loggerMessages)
            {
                if (loggerMessage.ReportDiagnostics(context))
                {
                    hasErrors = true;
                }
                else if (seenIds.TryGetValue(loggerMessage.Id, out var existingMethod))
                {
                    context.ReportDiagnostic(Diagnostic.Create(DiagnosticRules.DuplicateIdRule, loggerMessage.Location,
                        loggerMessage.Id, existingMethod.MethodName));
                    hasErrors = true;
                }
                else
                {
                    seenIds[loggerMessage.Id] = loggerMessage;
                }
            }

            if (hasErrors)
            {
                return;
            }

            var eventsXml = new StringBuilder();
            foreach (var loggerMessage in loggerMessages)
            {
                var isDebug = loggerMessage.Level == EventLevel.Verbose;
                _ = eventsXml.AppendLine(
                    $"          <event value=\"{loggerMessage.Id}\" channel=\"{(isDebug ? 'D' : 'O')}\" template=\"T\" level=\"win:{loggerMessage.Level}\" message=\"$(string.M)\" />"
                    );
            }

            var eventManifestXml = EventManifestTemplate
                .Replace($"{{{{{nameof(DefaultXmlNs)}}}}}", DefaultXmlNs)
                .Replace($"{{{{{nameof(WinXmlNs)}}}}}", WinXmlNs)
                .Replace($"{{{{{nameof(ProviderName)}}}}}", ProviderName)
                .Replace($"{{{{{nameof(ProviderGuid)}}}}}", ProviderGuid)
                .Replace($"{{{{{nameof(eventsXml)}}}}}", eventsXml.ToString().Trim())
                ;

            var eventManifestBytes = new UTF8Encoding(false).GetBytes(eventManifestXml);

            var generatorOutputDir = Path.Combine(
                buildOptions.ProjectDir,
                buildOptions.IntermediateOutputPath,
                "generated",
                nameof(Win32ResourceGenerator));
            var eventManifestPath = Path.Combine(generatorOutputDir, "EventManifest.xml");

            _ = Directory.CreateDirectory(generatorOutputDir);

            var manifestChanged = UpdateIfContentChanged(eventManifestPath, eventManifestBytes);
            if (manifestChanged)
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = Path.Combine(WindowsSDKBuildToolsBinVersionedArchFolder, "mc.exe"),
                    Arguments = "-um EventManifest.xml",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = generatorOutputDir,
                };

                using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start the process.");

                var output = process.StandardOutput.ReadToEnd();
                var error = process.StandardError.ReadToEnd();

                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException($"mc.exe error (Code {process.ExitCode}): {error}");
                }
            }

            _ = UpdateIfContentChanged(Path.Combine(generatorOutputDir, "Resources.rc"), Resources);

            var applicationManifest = File.ReadAllBytes(Path.Combine(buildOptions.ProjectDir, "app.manifest"));
            _ = UpdateIfContentChanged(Path.Combine(generatorOutputDir, "app.manifest"), applicationManifest);

            _ = GenerateVersionInfo(generatorOutputDir, buildOptions.Configuration == "Release");

            if (IsUpdateRequired(generatorOutputDir,
                ["Resources.rc", "app.manifest", "VersionInfo.rc", "EventManifest.rc", "EventManifestTEMP.BIN", "MSG00001.bin"],
                ["Resources.res"]))
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = Path.Combine(WindowsSDKBuildToolsBinVersionedArchFolder, "rc.exe"),
                    Arguments = "/NoLogo /8 Resources.rc",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = generatorOutputDir,
                };

                using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start the process.");

                var output = process.StandardOutput.ReadToEnd();
                var error = process.StandardError.ReadToEnd();

                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException($"rc.exe error (Code {process.ExitCode}): {error}");
                }
            }

        });
    }

    static bool IsUpdateRequired(string generatorOutputDir, string[] inputs, string[] outputs)
    {
        var outputFiles = outputs.Select(output => Path.Combine(generatorOutputDir, output)).ToArray();
        var inputFiles = inputs.Select(input => Path.Combine(generatorOutputDir, input)).ToArray();
        if (outputFiles.Any(outputFile => !File.Exists(outputFile)))
        {
            return true;
        }
        var outputLastWriteTime = outputFiles.Max(outputFile => File.GetLastWriteTimeUtc(outputFile));
        var inputLastWriteTime = inputFiles.Max(inputFile => File.GetLastWriteTimeUtc(inputFile));
        return inputLastWriteTime > outputLastWriteTime;
    }

    static bool UpdateIfContentChanged(string filePath, byte[] newContent)
    {
        if (File.Exists(filePath))
        {
            var currentContent = File.ReadAllBytes(filePath);
            if (currentContent.SequenceEqual(newContent))
            {
                return false;
            }
        }
        File.WriteAllBytes(filePath, newContent);
        return true;
    }

    bool GenerateVersionInfo(string generatorOutputDir, bool release)
    {
        var culture = new CultureInfo("en-US", false);

        var versionInfoPath = Path.Combine(generatorOutputDir, "VersionInfo.rc");

        var version = new Version(GitVersionInformation.AssemblySemFileVer);

        VS_FIXEDFILEINFO_FILE_FLAGS fileFlags = 0;
        string? privateBuild = null;
        if (!release)
        {
            fileFlags |= VS_FIXEDFILEINFO_FILE_FLAGS.VS_FF_DEBUG | VS_FIXEDFILEINFO_FILE_FLAGS.VS_FF_PRIVATEBUILD;
            privateBuild = "This is an unofficial (debug) build.";
        }
        else if (version.Revision == 0)
        {
            fileFlags |= VS_FIXEDFILEINFO_FILE_FLAGS.VS_FF_PRIVATEBUILD;
            privateBuild = "This is an unofficial (non-CI) build.";
        }

        privateBuild = privateBuild is not null
            ? $"""
                VALUE "PrivateBuild", "{privateBuild}"
                """
            : string.Empty;

        var versionInfoContent = VersionInfoTemplate
            .Replace("VS_VERSION_INFO", $"{PInvoke.VS_VERSION_INFO}")
            .Replace("VERSION_MAJOR", $"{version.Major}")
            .Replace("VERSION_MINOR", $"{version.Minor}")
            .Replace("VERSION_PATCH", $"{version.Build}")
            .Replace("CI_BUILD_NUMBER", $"{version.Revision}")
            .Replace("COMMITS_SINCE_VERSION_SOURCE", $"{GitVersionInformation.CommitsSinceVersionSource}")
            .Replace("VOS_NT_WINDOWS32", $"0x{(uint)VS_FIXEDFILEINFO_FILE_OS.VOS_NT_WINDOWS32:x8}L")
            .Replace("VFT_APP", $"0x{(uint)VS_FIXEDFILEINFO_FILE_TYPE.VFT_APP:x8}L")
            .Replace("VS_FFI_FILEFLAGSMASK", $"0x{PInvoke.VS_FFI_FILEFLAGSMASK:x8}L")
            .Replace("FILE_FLAGS", $"0x{(uint)fileFlags:x8}L")
            .Replace("{{OptionalPrivateBuild}}", privateBuild)
            .Replace("{{BlockCulture}}", $"{culture.LCID:x4}{Encoding.Unicode.CodePage:x4}")
            .Replace("{{CompanyName}}", $"{FileVersionInfo.CompanyName}")
            .Replace("{{FileVersion}}", $"{FileVersionInfo.FileVersion}")
            .Replace("{{LegalCopyright}}", $"{FileVersionInfo.LegalCopyright}")
            .Replace("{{ProductName}}", $"{FileVersionInfo.ProductName}")
            .Replace("{{ProductVersion}}", $"{FileVersionInfo.ProductVersion}")
            .Replace("{{LCID}}", $"{culture.LCID}")
            .Replace("{{CodePage}}", $"{Encoding.Unicode.CodePage}")
            ;

        var versionInfoBytes = new UTF8Encoding(false).GetBytes(versionInfoContent);
        return UpdateIfContentChanged(versionInfoPath, versionInfoBytes);
    }
}
