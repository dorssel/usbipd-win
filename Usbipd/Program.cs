// SPDX-FileCopyrightText: 2020 Frans van Dorsselaer
// SPDX-FileCopyrightText: Microsoft Corporation
//
// SPDX-License-Identifier: GPL-3.0-only

using System.CommandLine;
using System.CommandLine.Completions;
using System.CommandLine.Help;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using Usbipd.Automation;

namespace Usbipd;

static class Program
{
    public static readonly string Product = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyProductAttribute>()!.Product;
    public static readonly string Copyright = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyCopyrightAttribute>()!.Copyright;
    public static readonly string ApplicationName = Path.GetFileName(Process.GetCurrentProcess().ProcessName);

    static void AddParseError<T>(ArgumentResult argumentResult)
    {
        var value = argumentResult.Tokens[0].Value;
        var option = (argumentResult.Parent as OptionResult)?.Option.Name ?? string.Empty;
        var type = nameof(T);
        argumentResult.AddError($"Cannot parse argument '{value}' for option '{option}' as expected type '{type}'.");
    }

    static BusId ParseCompatibleBusId(ArgumentResult argumentResult)
    {
        if (!BusId.TryParse(argumentResult.Tokens[0].Value, out var busId) || busId.IsIncompatibleHub)
        {
            AddParseError<BusId>(argumentResult);
        }
        return busId;
    }

    static Guid ParseGuid(ArgumentResult argumentResult)
    {
        if (!Guid.TryParse(argumentResult.Tokens[0].Value, out var guid))
        {
            AddParseError<Guid>(argumentResult);
        }
        return guid;
    }

    static VidPid ParseVidPid(ArgumentResult argumentResult)
    {
        if (!VidPid.TryParse(argumentResult.Tokens[0].Value, out var vidPid))
        {
            AddParseError<VidPid>(argumentResult);
        }
        return vidPid;
    }

    static IPAddress ParseIPAddress(ArgumentResult argumentResult)
    {
        if (!IPAddress.TryParse(argumentResult.Tokens[0].Value, out var ipAddress))
        {
            AddParseError<IPAddress>(argumentResult);
        }
        return ipAddress ?? IPAddress.None;
    }

    static string OneOfRequiredText(params Option[] options)
    {
        Debug.Assert(options.Length >= 2);

        var names = options.Select(o => $"'{o.Name}'").ToArray();
        var list = names.Length == 2
            ? $"{names[0]} or {names[1]}"
            : string.Join(", ", names[0..(names.Length - 1)]) + ", or " + names[^1];
        return $"Exactly one of the options {list} is required.";
    }

    static string AtLeastOneOfRequiredText(params Option[] options)
    {
        Debug.Assert(options.Length >= 2);

        var names = options.Select(o => $"'{o.Name}'").ToArray();
        var list = names.Length == 2
            ? $"{names[0]} or {names[1]}"
            : string.Join(", ", names[0..(names.Length - 1)]) + ", or " + names[^1];
        return $"At least one of the options {list} is required.";
    }

    static string OptionRequiresText(Option option, params Option[] options)
    {
        Debug.Assert(options.Length >= 1);

        var names = options.Select(o => $"'{o.Name}'").ToArray();
        var list = names.Length == 1
            ? names[0]
            : names.Length == 2
                ? $"{names[0]} and {names[1]}"
                : string.Join(", ", names[0..(names.Length - 1)]) + ", and " + names[^1];
        return $"Option '{option.Name}' requires {list}.";
    }

    static void ValidateOneOf(CommandResult commandResult, params Option[] options)
    {
        Debug.Assert(options.Length >= 2);

        if (options.Count(option => commandResult.GetResult(option) is not null) != 1)
        {
            commandResult.AddError(OneOfRequiredText(options));
        }
    }

    static void ValidateAtLeastOneOf(CommandResult commandResult, params Option[] options)
    {
        Debug.Assert(options.Length >= 2);

        if (!options.Any(option => commandResult.GetResult(option) is not null))
        {
            commandResult.AddError(AtLeastOneOfRequiredText(options));
        }
    }

    static void ValidateOptionRequires(CommandResult commandResult, Option option, params Option[] options)
    {
        Debug.Assert(options.Length >= 1);

        if (commandResult.GetResult(option) is not null && options.Any(option => commandResult.GetResult(option) is null))
        {
            commandResult.AddError(OptionRequiresText(option, options));
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

    internal static async Task<int> Main(params string[] args)
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

        return (int)await Run(new DefaultConsole(), new CommandHandlers(), args);
    }

    class CustomHelpAction(HelpAction defaultAction) : SynchronousCommandLineAction
    {
        readonly HelpAction DefaultAction = defaultAction;

        public override int Invoke(ParseResult parseResult)
        {
            // Always prepend the product and version.
            parseResult.InvocationConfiguration.Output.WriteLine($"{Product} {GitVersionInformation.MajorMinorPatch}");
            parseResult.InvocationConfiguration.Output.WriteLine();

            var command = parseResult.CommandResult.Command;
            foreach (var subCommand in command.Children.OfType<Command>())
            {
                var subDescriptions = subCommand.Description?.Split('\0', 2) ?? [];
                if (subDescriptions.Length > 1)
                {
                    // Only use the short description for subcommands.
                    subCommand.Description = subDescriptions[0];
                }

                // Do not display subcommand argument help.
                subCommand.Arguments.Clear();
            }
            var descriptions = command.Description?.Split('\0', 2) ?? [];
            if (descriptions.Length > 1)
            {
                // Use the long description for the command itself.
                command.Description = descriptions[1];
            }

            return DefaultAction.Invoke(parseResult);
        }
    }

    internal static async Task<ExitCode> Run(IConsole console, ICommandHandlers commandHandlers, params string[] args)
    {
        var rootCommand = new RootCommand("Shares locally connected USB devices to other machines, including Hyper-V guests and WSL 2.");
        {
            var helpOption = rootCommand.Options.Single(option => option is HelpOption);
            helpOption.Action = new CustomHelpAction((HelpAction)helpOption.Action!);
        }
        {
            //
            //  attach [--auto-attach]
            //
            var autoAttachOption = new Option<bool>("--auto-attach", "-a")
            {
                Description = "Automatically re-attach when the device is detached or unplugged",
                Arity = ArgumentArity.Zero,
            };
            //
            //  attach --busid <BUSID>
            //
            var busIdOption = new Option<BusId>("--busid", "-b")
            {
                Description = "Attach device having <BUSID>",
                HelpName = "BUSID",
                CustomParser = ParseCompatibleBusId,
            };
            busIdOption.CompletionSources.Add(CompatibleBusIdCompletions);
            //
            //  attach --wsl [<DISTRIBUTION>]
            //
            var wslOption = new Option<string>("--wsl", "-w")
            {
                Description = "Attach to WSL, optionally specifying the distribution to use",
                HelpName = "[DISTRIBUTION]",
                Required = true,
                Arity = ArgumentArity.ZeroOrOne,
            };
            wslOption.CompletionSources.Add(completionContext => CompletionGuard(completionContext, () =>
                Wsl.CreateAsync(CancellationToken.None).Result?.Select(d => d.Name)));
            //
            //  attach [--hardware-id <VID>:<PID>]
            //
            // NOTE: the alias '-h' is already for '--help'
            var hardwareIdOption = new Option<VidPid>("--hardware-id", "-i")
            {
                Description = "Attach device having <VID>:<PID>",
                HelpName = "VID:PID",
                CustomParser = ParseVidPid,
            };
            hardwareIdOption.CompletionSources.Add(completionContext => CompletionGuard(completionContext, () =>
                UsbDevice.GetAll().Where(d => d.BusId.HasValue).GroupBy(d => d.HardwareId).Select(g => g.Key.ToString())));
            //
            //  attach [--host-ip <IPADDRESS>]
            //
            // NOTE: the alias '-h' is already for '--help' and '-i' is already for '--hardware-id'.
            var hostIpOption = new Option<IPAddress>("--host-ip", "-o")
            {
                Description = "Use <IPADDRESS> for WSL to connect back to the host",
                HelpName = "IPADDRESS",
                CustomParser = ParseIPAddress,
            };
            hostIpOption.CompletionSources.Add(completionContext => CompletionGuard(completionContext, () =>
                // Get all non-loopback unicast IPv4 addresses.
                NetworkInterface.GetAllNetworkInterfaces()
                    .Where(ni => ni.OperationalStatus == OperationalStatus.Up)
                    .Select(ni => ni.GetIPProperties().UnicastAddresses)
                    .SelectMany(uac => uac)
                    .Where(ua => ua.Address.AddressFamily == AddressFamily.InterNetwork)
                    .Where(ua => ua.Address.GetAddressBytes()[0] != 127)
                    .Select(ua => ua.Address.ToString())
                    .Order()
                    .Distinct()));
            //
            //  attach [--unplugged]
            //
            var unpluggedOption = new Option<bool>("--unplugged", "-u")
            {
                Description = "Allows auto-attaching a currently unplugged device",
                Arity = ArgumentArity.Zero,
            };
            //
            //  attach
            //
            var attachCommand = new Command("attach", "Attach a USB device to a client\0" + $"""
                Attaches a USB device to a client.

                Currently, only WSL is supported. Other clients need to perform an attach using client-side tooling.

                {OneOfRequiredText(busIdOption, hardwareIdOption)}
                {OptionRequiresText(unpluggedOption, autoAttachOption, busIdOption)}
                """)
                {
                    autoAttachOption,
                    busIdOption,
                    hardwareIdOption,
                    wslOption,
                    hostIpOption,
                    unpluggedOption,
                };
            attachCommand.Validators.Add(commandResult => ValidateOneOf(commandResult, busIdOption, hardwareIdOption));
            attachCommand.Validators.Add(commandResult => ValidateOptionRequires(commandResult, unpluggedOption, autoAttachOption, busIdOption));
            attachCommand.SetAction(async (parseResult, cancellationToken) => (int)(
                parseResult.GetResult(busIdOption) is not null
                    ? await commandHandlers.AttachWsl(parseResult.GetRequiredValue(busIdOption),
                            parseResult.GetValue(autoAttachOption),
                            parseResult.GetValue(unpluggedOption),
                            parseResult.GetValue(wslOption),
                            parseResult.GetValue(hostIpOption),
                            console, cancellationToken)
                    : await commandHandlers.AttachWsl(parseResult.GetRequiredValue(hardwareIdOption),
                            parseResult.GetValue(autoAttachOption),
                            parseResult.GetValue(wslOption),
                            parseResult.GetValue(hostIpOption),
                            console, cancellationToken)
            ));
            rootCommand.Subcommands.Add(attachCommand);
        }
        {
            //
            //  bind [--busid <BUSID>]
            //
            var busIdOption = new Option<BusId>("--busid", "-b")
            {
                Description = "Share device having <BUSID>",
                HelpName = "BUSID",
                CustomParser = ParseCompatibleBusId,
            };
            busIdOption.CompletionSources.Add(CompatibleBusIdCompletions);
            //
            //  bind [--force]
            //
            var forceOption = new Option<bool>("--force", "-f")
            {
                Description = "Force binding; the host cannot use the device",
                Arity = ArgumentArity.Zero,
            };
            //
            //  bind [--hardware-id <VID>:<PID>]
            //
            // NOTE: the alias '-h' is already for '--help'
            var hardwareIdOption = new Option<VidPid>("--hardware-id", "-i")
            {
                Description = "Share device having <VID>:<PID>",
                HelpName = "VID:PID",
                CustomParser = ParseVidPid,
            };
            hardwareIdOption.CompletionSources.Add(completionContext => CompletionGuard(completionContext, () =>
                UsbDevice.GetAll().Where(d => d.BusId.HasValue).GroupBy(d => d.HardwareId).Select(g => g.Key.ToString())));
            //
            //  bind
            //
            var bindCommand = new Command("bind", "Bind device\0" + $"""
                Registers a single USB device for sharing, so it can be attached to other machines. \
                Unless the '--force' option is used, shared devices remain available to the host \
                until they are attached to another machine.

                {OneOfRequiredText(busIdOption, hardwareIdOption)}
                """.Unwrap())
            {
                busIdOption,
                forceOption,
                hardwareIdOption,
            };
            bindCommand.Validators.Add(commandResult => ValidateOneOf(commandResult, busIdOption, hardwareIdOption));
            bindCommand.SetAction(async (parseResult, cancellationToken) => (int)(
                parseResult.GetResult(busIdOption) is not null
                    ? await commandHandlers.Bind(parseResult.GetRequiredValue(busIdOption),
                            parseResult.GetValue(forceOption),
                            console, cancellationToken)
                    : await commandHandlers.Bind(parseResult.GetRequiredValue(hardwareIdOption),
                            parseResult.GetValue(forceOption),
                            console, cancellationToken)
            ));
            rootCommand.Subcommands.Add(bindCommand);
        }
        {
            //
            //  detach [--all]
            //
            var allOption = new Option<bool>("--all", "-a")
            {
                Description = "Detach all devices",
                Arity = ArgumentArity.Zero,
            };
            //
            //  detach [--busid <BUSID>]
            //
            var busIdOption = new Option<BusId>("--busid", "-b")
            {
                Description = "Detach device having <BUSID>",
                HelpName = "BUSID",
                CustomParser = ParseCompatibleBusId,
            };
            busIdOption.CompletionSources.Add(CompatibleBusIdCompletions);
            //
            //  detach [--hardware-id <VID>:<PID>]
            //
            // NOTE: the alias '-h' is already for '--help'
            var hardwareIdOption = new Option<VidPid>("--hardware-id", "-i")
            {
                Description = "Detach all devices having <VID>:<PID>",
                HelpName = "VID:PID",
                CustomParser = ParseVidPid,
            };
            hardwareIdOption.CompletionSources.Add(completionContext => CompletionGuard(completionContext, () =>
                UsbDevice.GetAll().Where(d => d.BusId.HasValue).GroupBy(d => d.HardwareId).Select(g => g.Key.ToString())));
            //
            //  detach
            //
            var detachCommand = new Command("detach", "Detach a USB device from a client\0" + $"""
                Detaches one or more USB devices. The client sees this as a surprise removal event. \
                A detached device becomes available again in Windows, unless it was bound using the '--force' option.

                {OneOfRequiredText(allOption, busIdOption, hardwareIdOption)}
                """.Unwrap())
                {
                    allOption,
                    busIdOption,
                    hardwareIdOption,
                };
            detachCommand.Validators.Add(commandResult => ValidateOneOf(commandResult, allOption, busIdOption, hardwareIdOption));
            detachCommand.SetAction(async (parseResult, cancellationToken) => (int)(
                parseResult.GetValue(allOption)
                    ? await commandHandlers.DetachAll(console, cancellationToken)
                    : parseResult.GetResult(busIdOption) is not null
                        ? await commandHandlers.Detach(parseResult.GetRequiredValue(busIdOption), console, cancellationToken)
                        : await commandHandlers.Detach(parseResult.GetRequiredValue(hardwareIdOption), console, cancellationToken)
            ));
            rootCommand.Subcommands.Add(detachCommand);
        }
        {
            //
            //  license
            //
            var licenseCommand = new Command("license", "Display license information\0" +
                "Displays license information.");
            licenseCommand.SetAction(async (parseResult, cancellationToken) => (int)
                await commandHandlers.License(console, cancellationToken)
            );
            rootCommand.Subcommands.Add(licenseCommand);
        }
        {
            //
            //  list [--usbids]
            //
            var usbidsOption = new Option<bool>("--usbids", "-u")
            {
                Description = "Show device description from Linux database",
                Arity = ArgumentArity.Zero,
            };
            //
            //  list
            //
            var listCommand = new Command("list", "List USB devices\0" +
                "Lists currently connected USB devices as well as USB devices that are shared but are not currently connected.")
            {
                usbidsOption,
            };
            listCommand.SetAction(async (parseResult, cancellationToken) => (int)
                await commandHandlers.List(parseResult.GetValue(usbidsOption), console, cancellationToken)
            );
            rootCommand.Subcommands.Add(listCommand);
        }
        {
            //
            //  policy
            //
            var policyCommand = new Command("policy", "Manage policy rules\0" +
                "Policy rules allow or deny specific operations.");
            {
                //
                //  policy add --effect <EFFECT>
                //
                var effectOption = new Option<PolicyRuleEffect>("--effect", "-e")
                {
                    Description = "Allow or Deny",
                    HelpName = "EFFECT",
                    Required = true,
                };
                //
                //  policy add --operation <OPERATION>
                //
                var operationOption = new Option<PolicyRuleOperation>("--operation", "-o")
                {
                    Description = "Currently only supports 'AutoBind'",
                    HelpName = "OPERATION",
                    Required = true,
                };
                //
                //  policy add [--busid <BUSID>]
                //
                var busIdOption = new Option<BusId>("--busid", "-b")
                {
                    Description = "Add a policy for device having <BUSID>",
                    HelpName = "BUSID",
                    CustomParser = ParseCompatibleBusId,
                };
                busIdOption.CompletionSources.Add(CompatibleBusIdCompletions);
                //
                //  policy add [--hardware-id <VID>:<PID>]
                //
                // NOTE: the alias '-h' is already for '--help'
                var hardwareIdOption = new Option<VidPid>("--hardware-id", "-i")
                {
                    Description = "Add a policy for device having <VID>:<PID>",
                    HelpName = "VID:PID",
                    CustomParser = ParseVidPid,
                };
                hardwareIdOption.CompletionSources.Add(completionContext => CompletionGuard(completionContext, () =>
                    UsbDevice.GetAll().GroupBy(d => d.HardwareId).Select(g => g.Key.ToString())));
                //
                //  policy add
                //
                var addCommand = new Command("add", "Add a policy rule\0" + $"""
                    Add a new policy rule. The resulting policy will be effective immediately.

                    {AtLeastOneOfRequiredText(busIdOption, hardwareIdOption)}
                    """)
                {
                    effectOption,
                    operationOption,
                    busIdOption,
                    hardwareIdOption,
                };
                addCommand.Validators.Add(commandResult => ValidateAtLeastOneOf(commandResult, busIdOption, hardwareIdOption));
                addCommand.SetAction(async (parseResult, cancellationToken) =>
                {
                    var operation = parseResult.GetRequiredValue(operationOption);
                    return (int)await (operation switch
                    {
                        PolicyRuleOperation.AutoBind =>
                            commandHandlers.PolicyAdd(new PolicyRuleAutoBind(parseResult.GetRequiredValue(effectOption),
                                    parseResult.GetResult(busIdOption)?.GetRequiredValue(busIdOption),
                                    parseResult.GetResult(hardwareIdOption)?.GetRequiredValue(hardwareIdOption)),
                                console, cancellationToken),
                        _ => throw new UnexpectedResultException($"Unexpected policy rule operation '{operation}'."),
                    });
                });
                policyCommand.Subcommands.Add(addCommand);
            }
            {
                //
                //  policy list
                //
                var listCommand = new Command("list", "List policy rules\0" +
                    "List all policy rules.");
                listCommand.SetAction(async (parseResult, cancellationToken) => (int)
                    await commandHandlers.PolicyList(console, cancellationToken)
                );
                policyCommand.Subcommands.Add(listCommand);
            }
            {
                //
                //  policy remove [--all]
                //
                var allOption = new Option<bool>("--all", "-a")
                {
                    Description = "Remove all policy rules",
                    Arity = ArgumentArity.Zero,
                };
                //
                //  policy remove [--guid <GUID>]
                //
                var guidOption = new Option<Guid>("--guid", "-g")
                {
                    Description = "Remove the policy rule having <GUID>",
                    HelpName = "GUID",
                    CustomParser = ParseGuid,
                };
                guidOption.CompletionSources.Add(completionContext => CompletionGuard(completionContext, () =>
                    RegistryUtilities.GetPolicyRules().Select(r => r.Key.ToString("D"))));
                //
                //  policy remove
                //
                var removeCommand = new Command("remove", "Remove a policy rule\0" + $"""
                    Remove existing policy rules. The resulting policy will be effective immediately.

                    {OneOfRequiredText(allOption, guidOption)}
                    """)
                {
                    allOption,
                    guidOption,
                };
                removeCommand.Validators.Add(commandResult => ValidateOneOf(commandResult, allOption, guidOption));
                removeCommand.SetAction(async (parseResult, cancellationToken) => (int)(
                    parseResult.GetValue(allOption)
                        ? await commandHandlers.PolicyRemoveAll(console, cancellationToken)
                        : await commandHandlers.PolicyRemove(parseResult.GetRequiredValue(guidOption), console, cancellationToken)
                ));
                policyCommand.Subcommands.Add(removeCommand);
            }
            rootCommand.Subcommands.Add(policyCommand);
        }
        {
            //
            //  server [<KEY=VALUE>...]
            //
            Argument<string[]> keyValueArgument = new("KEY=VALUE")
            {
                Arity = ArgumentArity.ZeroOrMore,
                Description = ".NET configuration override\n  Example: \"Logging:LogLevel:Default=Trace\"",
            };
            //
            //  server
            //
            var serverCommand = new Command("server", "Run the server on the console\0" + $"""
                Runs the server stand-alone on the console.

                This command is intended for debugging purposes. \
                Only one instance of the server can be active; you may have to stop the background service first.
                """.Unwrap())
            {
                keyValueArgument,
            };
            serverCommand.SetAction(async (parseResult, cancellationToken) => (int)
                await commandHandlers.Server(parseResult.GetValue(keyValueArgument) ?? [], console, cancellationToken)
            );
            rootCommand.Subcommands.Add(serverCommand);
        }
        {
            //
            //  state
            //
            var stateCommand = new Command("state", "Output state in JSON\0" +
                "Outputs the current state of all USB devices in machine-readable JSON suitable for scripted automation.");
            stateCommand.SetAction(async (parseResult, cancellationToken) => (int)
                await commandHandlers.State(console, cancellationToken)
            );
            rootCommand.Subcommands.Add(stateCommand);
        }
        {
            //
            //  unbind [--all]
            //
            var allOption = new Option<bool>("--all", "-a")
            {
                Description = "Stop sharing all devices",
                Arity = ArgumentArity.Zero,
            };
            //
            //  unbind [--busid <BUSID>]
            //
            var busIdOption = new Option<BusId>("--busid", "-b")
            {
                Description = "Stop sharing device having <BUSID>",
                HelpName = "BUSID",
                CustomParser = ParseCompatibleBusId,
            };
            busIdOption.CompletionSources.Add(CompatibleBusIdCompletions);
            //
            //  unbind [--guid <GUID>]
            //
            var guidOption = new Option<Guid>("--guid", "-g")
            {
                Description = "Stop sharing persisted device having <GUID>",
                HelpName = "GUID",
                CustomParser = ParseGuid,
            };
            guidOption.CompletionSources.Add(completionContext => CompletionGuard(completionContext, () =>
                RegistryUtilities.GetBoundDevices().Where(d => !d.BusId.HasValue).Select(d => d.Guid.GetValueOrDefault().ToString("D"))));
            //
            //  unbind [--hardware-id <VID>:<PID>]
            //
            // NOTE: the alias '-h' is already for '--help'
            var hardwareIdOption = new Option<VidPid>("--hardware-id", "-i")
            {
                Description = "Stop sharing all devices having <VID>:<PID>",
                HelpName = "VID:PID",
                CustomParser = ParseVidPid,
            };
            hardwareIdOption.CompletionSources.Add(completionContext => CompletionGuard(completionContext, () =>
                UsbDevice.GetAll().GroupBy(d => d.HardwareId).Select(g => g.Key.ToString())));
            //
            //  unbind
            //
            var unbindCommand = new Command("unbind", "Unbind device\0" + $"""
                Unregisters one or more USB devices for sharing. If the device is currently attached to another \
                machine, it will immediately be detached and it becomes available to the host again; the remote \
                machine will see this as a surprise removal event.

                {OneOfRequiredText(allOption, busIdOption, guidOption, hardwareIdOption)}
                """.Unwrap())
            {
                allOption,
                busIdOption,
                guidOption,
                hardwareIdOption,
            };
            unbindCommand.Validators.Add(commandResult => ValidateOneOf(commandResult, allOption, busIdOption, guidOption, hardwareIdOption));
            unbindCommand.SetAction(async (parseResult, cancellationToken) => (int)(
                parseResult.GetValue(allOption)
                    ? await commandHandlers.UnbindAll(console, cancellationToken)
                    : parseResult.GetResult(busIdOption) is not null
                        ? await commandHandlers.Unbind(parseResult.GetRequiredValue(busIdOption), console, cancellationToken)
                        : parseResult.GetResult(guidOption) is not null
                            ? await commandHandlers.Unbind(parseResult.GetRequiredValue(guidOption), console, cancellationToken)
                            : await commandHandlers.Unbind(parseResult.GetRequiredValue(hardwareIdOption), console, cancellationToken)
            ));
            rootCommand.Subcommands.Add(unbindCommand);
        }
        {
            //
            //  wsl
            //
            //  NOTE: This command is obsolete; we just need to inform the user where to find the changes.
            //
            var wslCommand = new Command("wsl")
            {
                Hidden = true,
            };
            wslCommand.Options.Add(new Option<bool>("--help", "-h", "-?")
            {
                Arity = ArgumentArity.Zero,
            });
            wslCommand.Arguments.Add(new Argument<string[]>("ANY")
            {
                Arity = ArgumentArity.ZeroOrMore,
            });
            wslCommand.SetAction(async (parseResult, cancellationToken) =>
            {
                // System.CommandLine requires *all* actions to be either asynchronous or synchronous.
                await Task.CompletedTask;
                console.ReportError($"The 'wsl' subcommand has been removed. Learn about the new syntax at {Wsl.AttachWslUrl}.");
                return (int)ExitCode.ParseError;
            });
            rootCommand.Subcommands.Add(wslCommand);
        }
        {
            //
            //  install
            //
            var installCommand = new Command("install")
            {
                Hidden = true,
            };
            installCommand.SetAction(async (parseResult, cancellationToken) => (int)
                await commandHandlers.Install(console, cancellationToken)
            );
            rootCommand.Subcommands.Add(installCommand);
        }
        {
            //
            //  uninstall
            //
            var uninstallCommand = new Command("uninstall")
            {
                Hidden = true,
            };
            uninstallCommand.SetAction(async (parseResult, cancellationToken) => (int)
                await commandHandlers.Uninstall(console, cancellationToken)
            );
            rootCommand.Subcommands.Add(uninstallCommand);
        }

        try
        {
            var parseResult = rootCommand.Parse(args);
            // System.CommandLine requires InvokeAsync if the actions are asynchronous.
            var exitCode = (ExitCode)await parseResult.InvokeAsync(new()
            {
                Output = console.Out,
                Error = console.Error,
                EnableDefaultExceptionHandler = false,
            });
            if (parseResult.Action is ParseErrorAction)
            {
                // ParseErrorAction returns 1. We want to return ExitCode.ParseError (2) instead.
                exitCode = ExitCode.ParseError;
            }
            if ((int)exitCode is 130 or 143) // 128 + SIGINT or SIGTERM
            {
                // Happens when graceful cancelation times out, or at sign off / shutdown.
                console.ReportInfo("Canceled");
                return ExitCode.Canceled;
            }
            return Enum.IsDefined(exitCode) ? exitCode : throw new UnexpectedResultException($"Unknown exit code {exitCode}");
        }
        catch (AggregateException ex) when (ex.Flatten().InnerExceptions.Any(e => e is OperationCanceledException or TaskCanceledException))
        {
            console.ReportInfo("Canceled");
            return ExitCode.Canceled;
        }
        catch (Exception ex) when (ex is OperationCanceledException or TaskCanceledException)
        {
            console.ReportInfo("Canceled");
            return ExitCode.Canceled;
        }
    }
}
