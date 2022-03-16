// SPDX-FileCopyrightText: 2020 Frans van Dorsselaer
// SPDX-FileCopyrightText: Microsoft Corporation
//
// SPDX-License-Identifier: GPL-2.0-only

using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Completions;
using System.CommandLine.Help;
using System.CommandLine.Invocation;
using System.CommandLine.IO;
using System.CommandLine.Parsing;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using static UsbIpServer.ConsoleTools;

[assembly: CLSCompliant(true)]

[assembly: InternalsVisibleTo("UnitTests")]
[assembly: InternalsVisibleTo("DynamicProxyGenAssembly2")]

namespace UsbIpServer
{
    static class Program
    {
        public static readonly string Product = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyProductAttribute>()!.Product;
        public static readonly string Copyright = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyCopyrightAttribute>()!.Copyright;
        public static readonly string ApplicationName = Path.GetFileName(Process.GetCurrentProcess().ProcessName);

        public enum ExitCode
        {
            Success = 0,
            Failure = 1,
            ParseError = 2,
            AccessDenied = 3,
            Canceled = 4,
        };

        static BusId ParseBusId(ArgumentResult argumentResult)
        {
            if (!BusId.TryParse(argumentResult.Tokens[0].Value, out var busId))
            {
                argumentResult.ErrorMessage = LocalizationResources.Instance.ArgumentConversionCannotParseForOption(argumentResult.Tokens[0].Value,
                    (argumentResult.Parent as OptionResult)?.Token.Value ?? string.Empty, typeof(BusId));
            }
            return busId;
        }

        static Guid ParseGuid(ArgumentResult argumentResult)
        {
            if (!Guid.TryParse(argumentResult.Tokens[0].Value, out var guid))
            {
                argumentResult.ErrorMessage = LocalizationResources.Instance.ArgumentConversionCannotParseForOption(argumentResult.Tokens[0].Value,
                    (argumentResult.Parent as OptionResult)?.Token.Value ?? string.Empty, typeof(Guid));
            }
            return guid;
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
                return complete()?.Where(s => s.StartsWith(completionContext.WordToComplete)) ?? Array.Empty<string>();
            }
#pragma warning disable CA1031 // Do not catch general exception types (justification: completions are supposed to help, not crash)
            catch
#pragma warning restore CA1031 // Do not catch general exception types
            {
                return Array.Empty<string>();
            }
        }

        internal static int Main(string[] args)
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
            rootCommand.SetHandler((IConsole console, HelpBuilder helpBuilder) =>
            {
                helpBuilder.Write(rootCommand, console.Out.CreateTextWriter());
            });

            {
                //
                //  bind --busid <BUSID>
                //
                var busIdOption = new Option<BusId>(
                    aliases: new[] { "--busid", "-b" },
                    parseArgument: ParseBusId
                )
                {
                    IsRequired = true,
                    ArgumentHelpName = "BUSID",
                    Description = "Share device having <BUSID>",
                }.AddCompletions(completionContext => CompletionGuard(completionContext, () =>
                    UsbDevice.GetAll().Where(d => d.BusId.HasValue).Select(d => d.BusId.GetValueOrDefault().ToString())));
                //
                //  bind [--force]
                //
                var forceOption = new Option(
                    aliases: new[] { "--force", "-f" }
                )
                {
                    Description = "Force binding; the host cannot use the device",
                };
                //
                //  bind
                //
                var bindCommand = new Command("bind", "Bind device\0"
                    + "Registers a single USB device for sharing, so it can be "
                    + "attached to other machines. Unless the --force option is used, "
                    + "shared devices remain available to the host "
                    + "until they are attached to another machine.")
                {
                    busIdOption,
                    forceOption,
                };
                bindCommand.SetHandler(async (InvocationContext invocationContext) =>
                {
                    invocationContext.ExitCode = (int)(
                        await commandHandlers.Bind(invocationContext.ParseResult.GetValueForOption(busIdOption),
                            invocationContext.ParseResult.HasOption(forceOption),
                            invocationContext.Console, invocationContext.GetCancellationToken())
                        );
                });
                rootCommand.AddCommand(bindCommand);
            }
            {
                //
                //  license
                //
                var licenseCommand = new Command("license", "Display license information\0"
                    + "Displays license information.");
                licenseCommand.SetHandler(async (InvocationContext invocationContext) =>
                {
                    invocationContext.ExitCode = (int)(
                        await commandHandlers.License(invocationContext.Console, invocationContext.GetCancellationToken())
                        );
                });
                rootCommand.AddCommand(licenseCommand);
            }
            {
                //
                //  list
                //
                var listCommand = new Command("list", "List USB devices\0"
                    + "Lists currently connected USB devices as well as USB devices that are shared but are not currently connected.");
                listCommand.SetHandler(async (InvocationContext invocationContext) =>
                {
                    invocationContext.ExitCode = (int)(
                        await commandHandlers.List(invocationContext.Console, invocationContext.GetCancellationToken())
                        );
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
                serverCommand.SetHandler(async (InvocationContext invocationContext) =>
                {
                    invocationContext.ExitCode = (int)await commandHandlers.Server(
                        invocationContext.ParseResult.GetValueForArgument(keyValueArgument) ?? Array.Empty<string>(),
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
                stateCommand.SetHandler(async (InvocationContext invocationContext) =>
                {
                    invocationContext.ExitCode = (int)(
                        await commandHandlers.State(
                            invocationContext.Console, invocationContext.GetCancellationToken())
                        );
                });
                rootCommand.AddCommand(stateCommand);
            }
            {
                //
                //  unbind [--all]
                //
                var allOption = new Option(
                    aliases: new[] { "--all", "-a" }
                )
                {
                    Description = "Stop sharing all devices",
                };
                //
                //  unbind [--busid <BUSID>]
                //
                var busIdOption = new Option<BusId>(
                    aliases: new[] { "--busid", "-b" },
                    parseArgument: ParseBusId
                )
                {
                    ArgumentHelpName = "BUSID",
                    Description = "Stop sharing device having <BUSID>",
                }.AddCompletions(completionContext => CompletionGuard(completionContext, () =>
                    UsbDevice.GetAll().Where(d => d.BusId.HasValue).Select(d => d.BusId.GetValueOrDefault().ToString())));
                //
                //  unbind [--guid <GUID>]
                //
                var guidOption = new Option<Guid>(
                    aliases: new[] { "--guid", "-g" },
                    parseArgument: ParseGuid
                )
                {
                    ArgumentHelpName = "GUID",
                    Description = "Stop sharing persisted device having <GUID>",
                }.AddCompletions(completionContext => CompletionGuard(completionContext, () =>
                    RegistryUtils.GetBoundDevices().Where(d => !d.BusId.HasValue).Select(d => d.Guid.GetValueOrDefault().ToString("D"))));
                //
                //  unbind
                //
                var unbindCommand = new Command("unbind", "Unbind device\0"
                    + "Unregisters one (or all) USB devices for sharing. If the device is currently "
                    + "attached to another machine, it will immediately be detached and it becomes available to the "
                    + "host again; the remote machine will see this as a surprise removal event.\n"
                    + "\n"
                    + OneOfRequiredText(allOption, busIdOption, guidOption))
                {
                    allOption,
                    busIdOption,
                    guidOption,
                };
                unbindCommand.AddValidator(commandResult =>
                {
                    ValidateOneOf(commandResult, allOption, busIdOption, guidOption);
                });
                unbindCommand.SetHandler(async (InvocationContext invocationContext) =>
                {
                    if (invocationContext.ParseResult.HasOption(allOption))
                    {
                        invocationContext.ExitCode = (int)(
                            await commandHandlers.UnbindAll(invocationContext.Console, invocationContext.GetCancellationToken())
                            );
                    }
                    else if (invocationContext.ParseResult.HasOption(busIdOption))
                    {
                        invocationContext.ExitCode = (int)(
                            await commandHandlers.Unbind(invocationContext.ParseResult.GetValueForOption(busIdOption),
                                invocationContext.Console, invocationContext.GetCancellationToken())
                            );
                    }
                    else
                    {
                        invocationContext.ExitCode = (int)(
                            await commandHandlers.Unbind(invocationContext.ParseResult.GetValueForOption(guidOption),
                                invocationContext.Console, invocationContext.GetCancellationToken())
                            );
                    }
                });
                rootCommand.AddCommand(unbindCommand);
            }
            {
                //
                //  wsl
                //
                var wslCommand = new Command("wsl", "Convenience commands for WSL\0"
                    + "Convenience commands for attaching and detaching devices to Windows Subsystem for Linux.");
                wslCommand.SetHandler((IConsole console, HelpBuilder helpBuilder) =>
                {
                    // 'wsl' always expects a subcommand. Without a subcommand, just act as if '--help' was provided.
                    helpBuilder.Write(wslCommand, console.Out.CreateTextWriter());
                });
                rootCommand.AddCommand(wslCommand);
                {
                    //
                    //  wsl attach --busid <BUSID>
                    //
                    var busIdOption = new Option<BusId>(
                        aliases: new[] { "--busid", "-b" },
                        parseArgument: ParseBusId
                    )
                    {
                        IsRequired = true,
                        ArgumentHelpName = "BUSID",
                        Description = "Attach device having <BUSID>",
                    }.AddCompletions(completionContext => CompletionGuard(completionContext, () =>
                        UsbDevice.GetAll().Where(d => d.BusId.HasValue).Select(d => d.BusId.GetValueOrDefault().ToString())));
                    //
                    //  wsl attach --distribution <NAME>
                    //
                    var distributionOption = new Option<string>(
                        aliases: new[] { "--distribution", "-d" }
                    )
                    {
                        ArgumentHelpName = "NAME",
                        Description = "Name of the WSL distribution to attach to",
                    }.AddCompletions(completionContext => CompletionGuard(completionContext, () =>
                        WslDistributions.CreateAsync(CancellationToken.None).Result?.Distributions.Select(d => d.Name)));
                    //
                    //  wsl attach --usbip-path <PATH>
                    //
                    var usbipPathOption = new Option<string>(
                        aliases: new[] { "--usbip-path", "-u" }
                    )
                    {
                        ArgumentHelpName = "PATH",
                        Description = "Path to the 'usbip' client tool in the WSL distribution",
                    };
                    //
                    //  wsl attach
                    //
                    var attachCommand = new Command("attach", "Attach a USB device to a WSL instance\0"
                        + "Attaches a USB device to a WSL instance.\n"
                        + "\n"
                        + "The first time a device is attached this command will include a 'bind', for "
                        + "which administrator privileges are required. Subsequent attaches can be "
                        + "done with standard user privileges."
                        )
                    {
                        busIdOption,
                        distributionOption,
                        usbipPathOption,
                    };
                    attachCommand.SetHandler(async (InvocationContext invocationContext) =>
                    {
                        invocationContext.ExitCode = (int)(
                            await commandHandlers.WslAttach(invocationContext.ParseResult.GetValueForOption(busIdOption),
                                invocationContext.ParseResult.GetValueForOption(distributionOption),
                                invocationContext.ParseResult.GetValueForOption(usbipPathOption),
                                invocationContext.Console, invocationContext.GetCancellationToken())
                            );
                    });
                    wslCommand.AddCommand(attachCommand);
                }
                {
                    //
                    //  wsl detach [--all]
                    //
                    var allOption = new Option(
                        aliases: new[] { "--all", "-a" }
                    )
                    {
                        Description = "Detach all devices",
                    };
                    //
                    //  wsl detach [--busid <BUSID>]
                    //
                    var busIdOption = new Option<BusId>(
                        aliases: new[] { "--busid", "-b" },
                        parseArgument: ParseBusId
                    )
                    {
                        ArgumentHelpName = "BUSID",
                        Description = "Detach device having <BUSID>",
                    }.AddCompletions(completionContext => CompletionGuard(completionContext, () =>
                        UsbDevice.GetAll().Where(d => d.BusId.HasValue).Select(d => d.BusId.GetValueOrDefault().ToString())));
                    //
                    //  wsl detach
                    //
                    var detachCommand = new Command("detach", "Detach a USB device from a WSL instance\0"
                        + "Detaches one (or all) USB devices. The WSL instance sees this as a surprise "
                        + "removal event. A detached device becomes available again in Windows, "
                        + "unless it was bound using the --force option.\n"
                        + "\n"
                        + OneOfRequiredText(allOption, busIdOption))
                    {
                        allOption,
                        busIdOption,
                    };
                    detachCommand.AddValidator(commandResult =>
                    {
                        ValidateOneOf(commandResult, allOption, busIdOption);
                    });
                    detachCommand.SetHandler(async (InvocationContext invocationContext) =>
                    {
                        if (invocationContext.ParseResult.HasOption(allOption))
                        {
                            invocationContext.ExitCode = (int)(
                                await commandHandlers.WslDetachAll(invocationContext.Console, invocationContext.GetCancellationToken())
                                );
                        }
                        else
                        {
                            invocationContext.ExitCode = (int)(
                                await commandHandlers.WslDetach(invocationContext.ParseResult.GetValueForOption(busIdOption),
                                    invocationContext.Console, invocationContext.GetCancellationToken())
                                );
                        }
                    });
                    wslCommand.AddCommand(detachCommand);
                }
                {
                    //
                    //  wsl list
                    //
                    var listCommand = new Command("list", "List USB devices\0"
                        + "Lists all USB devices that are available for being attached to a WSL instance.");
                    listCommand.SetHandler(async (InvocationContext invocationContext) =>
                    {
                        invocationContext.ExitCode = (int)(
                            await commandHandlers.WslList(invocationContext.Console, invocationContext.GetCancellationToken())
                            );
                    });
                    wslCommand.AddCommand(listCommand);
                }
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
                .UseHelp()
                .UseHelp(helpContext =>
                {
                    foreach (var subCommand in helpContext.Command.Children.OfType<Command>())
                    {
                        var subDescriptions = subCommand.Description?.Split('\0', 2) ?? Array.Empty<string>();
                        if (subDescriptions.Length > 1)
                        {
                            // Only use the short description for subcommands.
                            helpContext.HelpBuilder.CustomizeSymbol(subCommand, subCommand.Name, subDescriptions[0]);
                        }
                    }
                    var descriptions = helpContext.Command.Description?.Split('\0', 2) ?? Array.Empty<string>();
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
}
