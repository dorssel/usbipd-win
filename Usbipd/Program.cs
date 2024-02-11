// SPDX-FileCopyrightText: 2020 Frans van Dorsselaer
// SPDX-FileCopyrightText: Microsoft Corporation
//
// SPDX-License-Identifier: GPL-3.0-only

using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Completions;
using System.CommandLine.Help;
using System.CommandLine.IO;
using System.CommandLine.Parsing;
using System.Diagnostics;
using System.Reflection;
using Usbipd.Automation;
using static Usbipd.ConsoleTools;

namespace Usbipd;

static class Program
{
    public static readonly string Product = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyProductAttribute>()!.Product;
    public static readonly string Copyright = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyCopyrightAttribute>()!.Copyright;
    public static readonly string ApplicationName = Path.GetFileName(Process.GetCurrentProcess().ProcessName);

    static BusId ParseCompatibleBusId(ArgumentResult argumentResult)
    {
        if (!BusId.TryParse(argumentResult.Tokens[0].Value, out var busId) || busId.IsIncompatibleHub)
        {
            argumentResult.ErrorMessage = LocalizationResources.Instance.ArgumentConversionCannotParseForOption(argumentResult.Tokens[0].Value,
                (argumentResult.Parent as OptionResult)?.Token?.Value ?? string.Empty, typeof(BusId));
        }
        return busId;
    }

    static Guid ParseGuid(ArgumentResult argumentResult)
    {
        if (!Guid.TryParse(argumentResult.Tokens[0].Value, out var guid))
        {
            argumentResult.ErrorMessage = LocalizationResources.Instance.ArgumentConversionCannotParseForOption(argumentResult.Tokens[0].Value,
                (argumentResult.Parent as OptionResult)?.Token?.Value ?? string.Empty, typeof(Guid));
        }
        return guid;
    }

    static VidPid ParseVidPid(ArgumentResult argumentResult)
    {
        if (!VidPid.TryParse(argumentResult.Tokens[0].Value, out var vidPid))
        {
            argumentResult.ErrorMessage = LocalizationResources.Instance.ArgumentConversionCannotParseForOption(argumentResult.Tokens[0].Value,
                (argumentResult.Parent as OptionResult)?.Token?.Value ?? string.Empty, typeof(VidPid));
        }
        return vidPid;
    }

    static string OneOfRequiredText(params Option[] options)
    {
        Debug.Assert(options.Length >= 2);

        var names = options.Select(o => $"'--{o.Name}'").ToArray();
        var list = names.Length == 2
            ? $"{names[0]} or {names[1]}"
            : string.Join(", ", names[0..(names.Length - 1)]) + ", or " + names[^1];
        return $"Exactly one of the options {list} is required.";
    }

    static void ValidateOneOf(CommandResult commandResult, params Option[] options)
    {
        Debug.Assert(options.Length >= 2);

        if (options.Count(option => commandResult.FindResultFor(option) is not null) != 1)
        {
            commandResult.ErrorMessage = OneOfRequiredText(options);
        }
    }

    internal static IEnumerable<string> CompletionGuard(CompletionContext completionContext, Func<IEnumerable<string>?> complete)
    {
        try
        {
            return complete()?.Where(s => s.StartsWith(completionContext.WordToComplete)) ?? [];
        }
#pragma warning disable CA1031 // Do not catch general exception types (justification: completions are supposed to help, not crash)
        catch
#pragma warning restore CA1031 // Do not catch general exception types
        {
            return [];
        }
    }

    internal static IEnumerable<string> CompatibleBusIdCompletions(CompletionContext completionContext)
    {
        return CompletionGuard(completionContext, () =>
            UsbDevice.GetAll().Where(d => d.BusId.HasValue && !d.BusId.Value.IsIncompatibleHub).Select(d => d.BusId.GetValueOrDefault().ToString()));
    }

    internal static int Main(params string[] args)
    {
        if (!Console.IsInputRedirected)
        {
            // This is required for Windows Terminal.
            Console.TreatControlCAsInput = false;
        }
        // All our own texts are ASCII only, but device descriptions support full unicode.
        try
        {
            // This will fail when running as a service; we can silently ignore that.
            Console.InputEncoding = System.Text.Encoding.UTF8;
            Console.OutputEncoding = System.Text.Encoding.UTF8;
        }
        catch (IOException) { }

        return (int)Run(null, new CommandHandlers(), args);
    }

    internal static ExitCode Run(IConsole? optionalTestConsole, ICommandHandlers commandHandlers, params string[] args)
    {
        var rootCommand = new RootCommand("Shares locally connected USB devices to other machines, including Hyper-V guests and WSL 2.");
        rootCommand.SetHandler(invocationContext =>
        {
            invocationContext.HelpBuilder.Write(rootCommand, invocationContext.Console.Out.CreateTextWriter());
        });

        {
            //
            //  attach [--auto-attach]
            //
            var autoAttachOption = new Option<bool>(
                aliases: ["--auto-attach", "-a"]
            )
            {
                Description = "Automatically re-attach when the device is detached or unplugged",
                Arity = ArgumentArity.Zero,
            };
            //
            //  attach --busid <BUSID>
            //
            var busIdOption = new Option<BusId>(
                aliases: ["--busid", "-b"],
                parseArgument: ParseCompatibleBusId
            )
            {
                ArgumentHelpName = "BUSID",
                Description = "Attach device having <BUSID>",
            }.AddCompletions(CompatibleBusIdCompletions);
            //
            //  attach --wsl [<DISTRIBUTION>]
            //
            var wslOption = new Option<string>(
                aliases: ["--wsl", "-w"]
            )
            {
                ArgumentHelpName = "[DISTRIBUTION]",
                Description = "Attach to WSL, optionally specifying the distribution to use",
                IsRequired = true,
                Arity = ArgumentArity.ZeroOrOne,
            }.AddCompletions(completionContext => CompletionGuard(completionContext, () =>
                Wsl.CreateAsync(CancellationToken.None).Result?.Select(d => d.Name)));
            //
            //  attach [--hardware-id <VID>:<PID>]
            //
            var hardwareIdOption = new Option<VidPid>(
                // NOTE: the alias '-h' is already for '--help'
                aliases: ["--hardware-id", "-i"],
                parseArgument: ParseVidPid
            )
            {
                ArgumentHelpName = "VID:PID",
                Description = "Attach device having <VID>:<PID>",
            }.AddCompletions(completionContext => CompletionGuard(completionContext, () =>
                UsbDevice.GetAll().Where(d => d.BusId.HasValue).GroupBy(d => d.HardwareId).Select(g => g.Key.ToString())));
            //
            //  wsl attach
            //
            var attachCommand = new Command("attach", "Attach a USB device to a client\0"
                + "Attaches a USB device to a client.\n"
                + "\n"
                + "Currently, only WSL is supported. Other clients need to perform an attach using client-side tooling.\n"
                + "\n"
                + OneOfRequiredText(busIdOption, hardwareIdOption))
                {
                    autoAttachOption,
                    busIdOption,
                    hardwareIdOption,
                    wslOption,
                };
            attachCommand.AddValidator(commandResult =>
            {
                ValidateOneOf(commandResult, busIdOption, hardwareIdOption);
            });
            attachCommand.SetHandler(async invocationContext =>
            {
                if (invocationContext.ParseResult.HasOption(busIdOption))
                {
                    invocationContext.ExitCode = (int)
                        await commandHandlers.AttachWsl(invocationContext.ParseResult.GetValueForOption(busIdOption),
                            invocationContext.ParseResult.HasOption(autoAttachOption),
                            invocationContext.ParseResult.GetValueForOption(wslOption),
                            invocationContext.Console, invocationContext.GetCancellationToken());
                }
                else
                {
                    invocationContext.ExitCode = (int)
                        await commandHandlers.AttachWsl(invocationContext.ParseResult.GetValueForOption(hardwareIdOption),
                            invocationContext.ParseResult.HasOption(autoAttachOption),
                            invocationContext.ParseResult.GetValueForOption(wslOption),
                            invocationContext.Console, invocationContext.GetCancellationToken());
                }
            });
            rootCommand.AddCommand(attachCommand);
        }
        {
            //
            //  bind [--busid <BUSID>]
            //
            var busIdOption = new Option<BusId>(
                aliases: ["--busid", "-b"],
                parseArgument: ParseCompatibleBusId
            )
            {
                ArgumentHelpName = "BUSID",
                Description = "Share device having <BUSID>",
            }.AddCompletions(CompatibleBusIdCompletions);
            //
            //  bind [--force]
            //
            var forceOption = new Option<bool>(
                aliases: ["--force", "-f"]
            )
            {
                Description = "Force binding; the host cannot use the device",
                Arity = ArgumentArity.Zero,
            };
            //
            //  bind [--hardware-id <VID>:<PID>]
            //
            var hardwareIdOption = new Option<VidPid>(
                // NOTE: the alias '-h' is already for '--help'
                aliases: ["--hardware-id", "-i"],
                parseArgument: ParseVidPid
            )
            {
                ArgumentHelpName = "VID:PID",
                Description = "Share device having <VID>:<PID>",
            }.AddCompletions(completionContext => CompletionGuard(completionContext, () =>
                UsbDevice.GetAll().Where(d => d.BusId.HasValue).GroupBy(d => d.HardwareId).Select(g => g.Key.ToString())));
            //
            //  bind
            //
            var bindCommand = new Command("bind", "Bind device\0"
                + "Registers a single USB device for sharing, so it can be "
                + "attached to other machines. Unless the --force option is used, "
                + "shared devices remain available to the host "
                + "until they are attached to another machine.\n"
                + "\n"
                + OneOfRequiredText(busIdOption, hardwareIdOption))
            {
                busIdOption,
                forceOption,
                hardwareIdOption,
            };
            bindCommand.AddValidator(commandResult =>
            {
                ValidateOneOf(commandResult, busIdOption, hardwareIdOption);
            });
            bindCommand.SetHandler(async invocationContext =>
            {
                if (invocationContext.ParseResult.HasOption(busIdOption))
                {
                    invocationContext.ExitCode = (int)
                        await commandHandlers.Bind(invocationContext.ParseResult.GetValueForOption(busIdOption),
                            invocationContext.ParseResult.HasOption(forceOption),
                            invocationContext.Console, invocationContext.GetCancellationToken());
                }
                else
                {
                    invocationContext.ExitCode = (int)
                        await commandHandlers.Bind(invocationContext.ParseResult.GetValueForOption(hardwareIdOption),
                            invocationContext.ParseResult.HasOption(forceOption),
                            invocationContext.Console, invocationContext.GetCancellationToken());
                }
            });
            rootCommand.AddCommand(bindCommand);
        }
        {
            //
            //  detach [--all]
            //
            var allOption = new Option<bool>(
                aliases: ["--all", "-a"]
            )
            {
                Description = "Detach all devices",
                Arity = ArgumentArity.Zero,
            };
            //
            //  detach [--busid <BUSID>]
            //
            var busIdOption = new Option<BusId>(
                aliases: ["--busid", "-b"],
                parseArgument: ParseCompatibleBusId
            )
            {
                ArgumentHelpName = "BUSID",
                Description = "Detach device having <BUSID>",
            }.AddCompletions(CompatibleBusIdCompletions);
            //
            //  detach [--hardware-id <VID>:<PID>]
            //
            var hardwareIdOption = new Option<VidPid>(
                // NOTE: the alias '-h' is already for '--help'
                aliases: ["--hardware-id", "-i"],
                parseArgument: ParseVidPid
            )
            {
                ArgumentHelpName = "VID:PID",
                Description = "Detach all devices having <VID>:<PID>",
            }.AddCompletions(completionContext => CompletionGuard(completionContext, () =>
                UsbDevice.GetAll().Where(d => d.BusId.HasValue).GroupBy(d => d.HardwareId).Select(g => g.Key.ToString())));
            //
            //  wsl detach
            //
            var detachCommand = new Command("detach", "Detach a USB device from a client\0"
                + "Detaches one or more USB devices. The client sees this as a surprise "
                + "removal event. A detached device becomes available again in Windows, "
                + "unless it was bound using the --force option.\n"
                + "\n"
                + OneOfRequiredText(allOption, busIdOption, hardwareIdOption))
                {
                    allOption,
                    busIdOption,
                    hardwareIdOption,
                };
            detachCommand.AddValidator(commandResult =>
            {
                ValidateOneOf(commandResult, allOption, busIdOption, hardwareIdOption);
            });
            detachCommand.SetHandler(async invocationContext =>
            {
                if (invocationContext.ParseResult.HasOption(allOption))
                {
                    invocationContext.ExitCode = (int)
                        await commandHandlers.DetachAll(invocationContext.Console, invocationContext.GetCancellationToken());
                }
                else if (invocationContext.ParseResult.HasOption(busIdOption))
                {
                    invocationContext.ExitCode = (int)
                        await commandHandlers.Detach(invocationContext.ParseResult.GetValueForOption(busIdOption),
                            invocationContext.Console, invocationContext.GetCancellationToken());
                }
                else
                {
                    invocationContext.ExitCode = (int)
                        await commandHandlers.Detach(invocationContext.ParseResult.GetValueForOption(hardwareIdOption),
                            invocationContext.Console, invocationContext.GetCancellationToken());
                }
            });
            rootCommand.AddCommand(detachCommand);
        }
        {
            //
            //  license
            //
            var licenseCommand = new Command("license", "Display license information\0"
                + "Displays license information.");
            licenseCommand.SetHandler(async invocationContext =>
            {
                invocationContext.ExitCode = (int)
                    await commandHandlers.License(invocationContext.Console, invocationContext.GetCancellationToken());
            });
            rootCommand.AddCommand(licenseCommand);
        }
        {
            //
            //  list [--usbids]
            //
            var usbidsOption = new Option<bool>(
                aliases: ["--usbids", "-u"]
            )
            {
                Description = "Show device description from Linux database",
                Arity = ArgumentArity.Zero,
            };
            //
            //  list
            //
            var listCommand = new Command("list", "List USB devices\0"
                + "Lists currently connected USB devices as well as USB devices that are shared but are not currently connected.")
            {
                usbidsOption,
            };
            listCommand.SetHandler(async invocationContext =>
            {
                invocationContext.ExitCode = (int)
                    await commandHandlers.List(invocationContext.ParseResult.HasOption(usbidsOption),
                        invocationContext.Console, invocationContext.GetCancellationToken());
            });
            rootCommand.AddCommand(listCommand);
        }
        {
            //
            //  server [<KEY=VALUE>...]
            //
            Argument<string[]> keyValueArgument = new()
            {
                Arity = ArgumentArity.ZeroOrMore,
                Name = "KEY=VALUE",
                Description = ".NET configuration override\n  Example: \"Logging:LogLevel:Default=Trace\"",
            };
            //
            //  server
            //
            var serverCommand = new Command("server", "Run the server on the console\0"
                + "Runs the server stand-alone on the console.\n"
                + " \n"
                + "This command is intended for debugging purposes. "
                + "Only one instance of the server can be active; "
                + "you may have to stop the background service first.")
            {
                keyValueArgument,
            };
            serverCommand.SetHandler(async invocationContext =>
            {
                invocationContext.ExitCode = (int)
                    await commandHandlers.Server(invocationContext.ParseResult.GetValueForArgument(keyValueArgument) ?? [],
                        invocationContext.Console, invocationContext.GetCancellationToken());
            });
            rootCommand.AddCommand(serverCommand);
        }
        {
            //
            //  state
            //
            var stateCommand = new Command("state", "Output state in JSON\0"
                + "Outputs the current state of all USB devices in machine-readable JSON suitable for scripted automation.");
            stateCommand.SetHandler(async invocationContext =>
            {
                invocationContext.ExitCode = (int)
                    await commandHandlers.State(invocationContext.Console, invocationContext.GetCancellationToken());
            });
            rootCommand.AddCommand(stateCommand);
        }
        {
            //
            //  unbind [--all]
            //
            var allOption = new Option<bool>(
                aliases: ["--all", "-a"]
            )
            {
                Description = "Stop sharing all devices",
                Arity = ArgumentArity.Zero,
            };
            //
            //  unbind [--busid <BUSID>]
            //
            var busIdOption = new Option<BusId>(
                aliases: ["--busid", "-b"],
                parseArgument: ParseCompatibleBusId
            )
            {
                ArgumentHelpName = "BUSID",
                Description = "Stop sharing device having <BUSID>",
            }.AddCompletions(CompatibleBusIdCompletions);
            //
            //  unbind [--guid <GUID>]
            //
            var guidOption = new Option<Guid>(
                aliases: ["--guid", "-g"],
                parseArgument: ParseGuid
            )
            {
                ArgumentHelpName = "GUID",
                Description = "Stop sharing persisted device having <GUID>",
            }.AddCompletions(completionContext => CompletionGuard(completionContext, () =>
                RegistryUtils.GetBoundDevices().Where(d => !d.BusId.HasValue).Select(d => d.Guid.GetValueOrDefault().ToString("D"))));
            //
            //  unbind [--hardware-id <VID>:<PID>]
            //
            var hardwareIdOption = new Option<VidPid>(
                // NOTE: the alias '-h' is already for '--help'
                aliases: ["--hardware-id", "-i"],
                parseArgument: ParseVidPid
            )
            {
                ArgumentHelpName = "VID:PID",
                Description = "Stop sharing all devices having <VID>:<PID>",
            }.AddCompletions(completionContext => CompletionGuard(completionContext, () =>
                UsbDevice.GetAll().GroupBy(d => d.HardwareId).Select(g => g.Key.ToString())));
            //
            //  unbind
            //
            var unbindCommand = new Command("unbind", "Unbind device\0"
                + "Unregisters one or more USB devices for sharing. If the device is currently "
                + "attached to another machine, it will immediately be detached and it becomes available to the "
                + "host again; the remote machine will see this as a surprise removal event.\n"
                + "\n"
                + OneOfRequiredText(allOption, busIdOption, guidOption, hardwareIdOption))
            {
                allOption,
                busIdOption,
                guidOption,
                hardwareIdOption,
            };
            unbindCommand.AddValidator(commandResult =>
            {
                ValidateOneOf(commandResult, allOption, busIdOption, guidOption, hardwareIdOption);
            });
            unbindCommand.SetHandler(async invocationContext =>
            {
                if (invocationContext.ParseResult.HasOption(allOption))
                {
                    invocationContext.ExitCode = (int)
                        await commandHandlers.UnbindAll(invocationContext.Console, invocationContext.GetCancellationToken());
                }
                else if (invocationContext.ParseResult.HasOption(busIdOption))
                {
                    invocationContext.ExitCode = (int)
                        await commandHandlers.Unbind(invocationContext.ParseResult.GetValueForOption(busIdOption),
                            invocationContext.Console, invocationContext.GetCancellationToken());
                }
                else if (invocationContext.ParseResult.HasOption(guidOption))
                {
                    invocationContext.ExitCode = (int)
                        await commandHandlers.Unbind(invocationContext.ParseResult.GetValueForOption(guidOption),
                            invocationContext.Console, invocationContext.GetCancellationToken());
                }
                else
                {
                    invocationContext.ExitCode = (int)
                        await commandHandlers.Unbind(invocationContext.ParseResult.GetValueForOption(hardwareIdOption),
                            invocationContext.Console, invocationContext.GetCancellationToken());
                }
            });
            rootCommand.AddCommand(unbindCommand);
        }
        {
            //
            //  wsl
            //
            //  NOTE: This command is obsolete; we just need to inform the user where to find the changes.
            //
            var wslCommand = new Command("wsl")
            {
                IsHidden = true,
            };
            wslCommand.AddOption(new Option<bool>(
                aliases: ["--help", "-h", "-?"]
            )
            {
                Arity = ArgumentArity.Zero,
            });
            wslCommand.AddArgument(new Argument<string[]>()
            {
                Arity = ArgumentArity.ZeroOrMore,
            });
            wslCommand.SetHandler(invocationContext =>
            {
                ConsoleTools.ReportError(invocationContext.Console, $"The 'wsl' subcommand has been removed. Learn about the new syntax at {Wsl.AttachWslUrl}.");
                invocationContext.ExitCode = (int)ExitCode.ParseError;
            });
            rootCommand.AddCommand(wslCommand);
        }
        {
            //
            //  install
            //
            var installCommand = new Command("install")
            {
                IsHidden = true,
            };
            installCommand.SetHandler(async invocationContext =>
            {
                invocationContext.ExitCode = (int)
                    await commandHandlers.Install(invocationContext.Console, invocationContext.GetCancellationToken());
            });
            rootCommand.AddCommand(installCommand);
        }
        {
            //
            //  uninstall
            //
            var uninstallCommand = new Command("uninstall")
            {
                IsHidden = true,
            };
            uninstallCommand.SetHandler(async invocationContext =>
            {
                invocationContext.ExitCode = (int)
                    await commandHandlers.Uninstall(invocationContext.Console, invocationContext.GetCancellationToken());
            });
            rootCommand.AddCommand(uninstallCommand);
        }

        // Same as UseDefaults() minus exception handling.
        var commandLine = new CommandLineBuilder(rootCommand)
            .UseVersionOption()
            .UseEnvironmentVariableDirective()
            .UseParseDirective((int)ExitCode.ParseError)
            .UseSuggestDirective()
            .RegisterWithDotnetSuggest()
            .UseTypoCorrections()
            .UseParseErrorReporting((int)ExitCode.ParseError)
            .CancelOnProcessTermination()
            .UseHelp(helpContext =>
            {
                foreach (var subCommand in helpContext.Command.Children.OfType<Command>())
                {
                    var subDescriptions = subCommand.Description?.Split('\0', 2) ?? [];
                    if (subDescriptions.Length > 1)
                    {
                        // Only use the short description for subcommands.
                        helpContext.HelpBuilder.CustomizeSymbol(subCommand, subCommand.Name, subDescriptions[0]);
                    }
                }
                var descriptions = helpContext.Command.Description?.Split('\0', 2) ?? [];
                helpContext.HelpBuilder.CustomizeLayout(_ =>
                {
                    var layout = HelpBuilder.Default.GetLayout();
                    if (descriptions.Length > 1)
                    {
                        // Use the long description for the command itself.
                        layout = layout.Skip(1).Prepend(_ =>
                        {
                            helpContext.Output.WriteLine(helpContext.HelpBuilder.LocalizationResources.HelpDescriptionTitle());
                            var indent = new string(' ', 2);
                            var wrappedLines = Wrap(descriptions[1].Trim(), helpContext.HelpBuilder.MaxWidth - indent.Length);
                            foreach (var wrappedLine in wrappedLines)
                            {
                                helpContext.Output.WriteLine(indent + wrappedLine);
                            }
                        });
                    }
                    // Always prepend the product and version.
                    layout = layout.Prepend(_ => helpContext.Output.WriteLine($"{Product} {GitVersionInformation.MajorMinorPatch}"));
                    return layout;
                });
            })
            .Build();

        try
        {
            var exitCode = (ExitCode)commandLine.InvokeAsync(args, optionalTestConsole).Result;
            if (!Enum.IsDefined(exitCode))
            {
                throw new UnexpectedResultException($"Unknown exit code {exitCode}");
            }
            return exitCode;
        }
        catch (AggregateException ex) when (ex.Flatten().InnerExceptions.Any(e => e is OperationCanceledException))
        {
            return ExitCode.Canceled;
        }
    }
}
