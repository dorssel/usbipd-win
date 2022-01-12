// SPDX-FileCopyrightText: 2020 Frans van Dorsselaer
// SPDX-FileCopyrightText: Microsoft Corporation
//
// SPDX-License-Identifier: GPL-2.0-only

using System;
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Help;
using System.CommandLine.Invocation;
using System.CommandLine.IO;
using System.CommandLine.Parsing;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

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

        static string OneOfRequiredText(params IOption[] options)
        {
            Debug.Assert(options.Length >= 2);

            var names = options.Select(o => $"'--{o.Name}'").ToArray();
            var list = names.Length == 2
                ? $"{names[0]} or {names[1]}"
                : string.Join(", ", names[0..(names.Length - 1)]) + ", or " + names[^1];
            return $"Exactly one of the options {list} is required.";
        }

        static string? ValidateOneOf(CommandResult commandResult, params IOption[] options)
        {
            Debug.Assert(options.Length >= 2);

            if (options.Count(option => commandResult.FindResultFor(option) is not null) != 1)
            {
                return OneOfRequiredText(options);
            }
            return null;
        }

        internal static int Main(string[] args)
        {
            if (!Console.IsInputRedirected)
            {
                // This is required for Windows Terminal.
                Console.TreatControlCAsInput = false;
            }
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
                };
                //
                //  bind
                //
                var bindCommand = new Command("bind", "Bind device\0"
                    + "Registers a single compatible USB devices for sharing, so it can be "
                    + "attached by other machines. Bound devices remain available to the host "
                    + "until they are attached by another machine, at which time they "
                    + "become unavailable to the host.")
                {
                    busIdOption,
                };
                bindCommand.SetHandler(async (InvocationContext invocationContext) =>
                {
                    invocationContext.ExitCode = (int)(
                        await commandHandlers.Bind(invocationContext.ParseResult.GetValueForOption(busIdOption),
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
                }.AddCompletions(completionContext =>
                {
                    return new string[] { "1-2", "3-4" };
                });
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
                };
                //
                //  unbind
                //
                var unbindCommand = new Command("unbind", "Unbind device\0"
                    + "Unregisters one (or all) USB devices for sharing. If the device is currently "
                    + "attached, it will immediately be detached and it becomes available to the "
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
                    return ValidateOneOf(commandResult, allOption, busIdOption, guidOption);
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
                    };
                    //
                    //  wsl attach --distribution <NAME>
                    //
                    var distributionOption = new Option<string>(
                        aliases: new[] { "--distribution", "-d" }
                    )
                    {
                        ArgumentHelpName = "NAME",
                        Description = "Name of the WSL distribution to attach to",
                    };
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
                        + "The 'wsl attach' command is equivalent to the 'bind' command followed by "
                        + "a 'usbip attach' command on the Linux side."
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
                    }.AddCompletions(completionContext =>
                    {
                        return new string[] { "1-2", "3-4" };
                    });
                    //
                    //  wsl detach
                    //
                    var detachCommand = new Command("detach", "Detach a USB device from a WSL instance\0"
                        + "Detaches one (or all) USB devices. The WSL instance sees this as a surprise "
                        + "removal event. A detached device becomes available again in Windows.\n"
                        + "\n"
                        + "The 'wsl detach' command is equivalent to the 'unbind' command.\n"
                        + "\n"
                        + OneOfRequiredText(allOption, busIdOption))
                    {
                        allOption,
                        busIdOption,
                    };
                    detachCommand.AddValidator(commandResult =>
                    {
                        return ValidateOneOf(commandResult, allOption, busIdOption);
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
                .UseDebugDirective()
                .UseSuggestDirective()
                .RegisterWithDotnetSuggest()
                .UseTypoCorrections()
                .UseParseErrorReporting((int)ExitCode.ParseError)
                .CancelOnProcessTermination()
                .UseHelp()
                .UseHelp(helpContext =>
                {
                    foreach (var subCommand in helpContext.Command.Children.OfType<ICommand>())
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
