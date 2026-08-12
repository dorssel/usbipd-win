// SPDX-FileCopyrightText: 2026 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

using System.Diagnostics.Tracing;
using Microsoft.CodeAnalysis;

namespace Win32ResourceGenerator;

public class LoggerMessage
{
    LoggerMessage(string methodName, Location location,
        int? constructorId, int? constructorLevel, string? constructorMessage,
        int? namedId, int? namedLevel, string? namedMessage)
    {
        MethodName = methodName;
        Location = location;
        ConstructorId = constructorId;
        ConstructorLevel = constructorLevel;
        ConstructorMessage = constructorMessage;
        NamedId = namedId;
        NamedLevel = namedLevel;
        NamedMessage = namedMessage;
    }

    public string MethodName { get; }

    public Location Location { get; }

    readonly int? ConstructorId;
    readonly int? ConstructorLevel;
    readonly string? ConstructorMessage;

    readonly int? NamedId;
    readonly int? NamedLevel;
    readonly string? NamedMessage;

    public bool IsValid { get; private set; }

    public ushort Id => IsValid ? (ushort)(ConstructorId ?? NamedId)! : throw new InvalidOperationException("Not valid");

    public EventLevel Level => (IsValid ? (ConstructorLevel ?? NamedLevel)! : throw new InvalidOperationException("Not valid")) switch
    {
        // NOTE: We do not want to reference Microsoft.Extensions.Logging.Abstractions in this project, so we cannot use LogLevel enum directly.

        5 /* LogLevel.Critical */ => EventLevel.Critical,
        4 /* LogLevel.Error */ => EventLevel.Error,
        3 /* LogLevel.Warning */ => EventLevel.Warning,
        2 /* LogLevel.Information */ => EventLevel.Informational,
        1 /* LogLevel.Debug */ => EventLevel.Verbose,
        0 /* LogLevel.Trace */ => EventLevel.Verbose,
        // NOTE: We cannot support LogLevel.None (6).
        6 or _ => throw new InvalidOperationException("Not valid"),
    };

    public string Message => IsValid ? (ConstructorMessage ?? NamedMessage)! : throw new InvalidOperationException("Not valid");

    static public LoggerMessage Create(GeneratorAttributeSyntaxContext context)
    {
        var diagnosticDescriptors = new List<DiagnosticDescriptor>();

        int? constructorId = null;
        int? constructorLevel = null;
        string? constructorMessage = null;

        var attribute = context.Attributes.Single();

        foreach (var arg in attribute.ConstructorArguments)
        {
            switch (arg.Type?.ToDisplayString())
            {
                case "int":
                    constructorId = (int?)arg.Value;
                    break;
                case "Microsoft.Extensions.Logging.LogLevel":
                    constructorLevel = (int?)arg.Value;
                    break;
                case "string":
                    constructorMessage = (string?)arg.Value;
                    break;
            }
        }

        int? namedId = null;
        int? namedLevel = null;
        string? namedMessage = null;

        foreach (var arg in attribute.NamedArguments)
        {
            switch (arg.Key)
            {
                case "EventId":
                    namedId = (int)(arg.Value.Value ?? 0);
                    break;
                case "Level":
                    namedLevel = (int?)arg.Value.Value;
                    break;
                case "Message":
                    namedMessage = (string?)arg.Value.Value;
                    break;
            }
        }

        // Decouple the location details from the syntax to allow caching and avoid holding onto syntax trees.
        var syntaxLocation = context.TargetNode.GetLocation();
        return new(context.TargetSymbol.Name,
            Location.Create(syntaxLocation.SourceTree!.FilePath, syntaxLocation.SourceSpan, syntaxLocation.GetLineSpan().Span),
            constructorId, constructorLevel, constructorMessage,
            namedId, namedLevel, namedMessage);
    }

    public bool ReportDiagnostics(SourceProductionContext context)
    {
        var hasErrors = false;

        if (ConstructorId is null && NamedId is null)
        {
            context.ReportDiagnostic(Diagnostic.Create(DiagnosticRules.MissingPropertyRule, Location, "EventId"));
            hasErrors = true;
        }
        else if (ConstructorId is not null && NamedId is not null)
        {
            context.ReportDiagnostic(Diagnostic.Create(DiagnosticRules.DuplicatePropertyRule, Location, "EventId"));
            hasErrors = true;
        }
        else if ((ConstructorId ?? NamedId) is <= 0 or > ushort.MaxValue)
        {
            context.ReportDiagnostic(Diagnostic.Create(DiagnosticRules.InvalidEventIdRule, Location, ConstructorId ?? NamedId));
            hasErrors = true;
        }

        if (ConstructorLevel is null && NamedLevel is null)
        {
            context.ReportDiagnostic(Diagnostic.Create(DiagnosticRules.MissingPropertyRule, Location, "Level"));
            hasErrors = true;
        }
        else if (ConstructorLevel is not null && NamedLevel is not null)
        {
            context.ReportDiagnostic(Diagnostic.Create(DiagnosticRules.DuplicatePropertyRule, Location, "Level"));
            hasErrors = true;
        }
        else if ((ConstructorLevel ?? NamedLevel) is < 0 or > 5)
        {
            context.ReportDiagnostic(Diagnostic.Create(DiagnosticRules.InvalidLevelRule, Location, ConstructorLevel ?? NamedLevel));
            hasErrors = true;
        }

        if (ConstructorMessage is null && NamedMessage is null)
        {
            context.ReportDiagnostic(Diagnostic.Create(DiagnosticRules.MissingPropertyRule, Location, "Message"));
            hasErrors = true;
        }
        else if (ConstructorMessage is not null && NamedMessage is not null)
        {
            context.ReportDiagnostic(Diagnostic.Create(DiagnosticRules.DuplicatePropertyRule, Location, "Message"));
            hasErrors = true;
        }
        else if (string.IsNullOrWhiteSpace(ConstructorMessage ?? NamedMessage))
        {
            context.ReportDiagnostic(Diagnostic.Create(DiagnosticRules.InvalidMessageRule, Location, ConstructorMessage ?? NamedMessage));
            hasErrors = true;
        }

        IsValid = !hasErrors;

        return hasErrors;
    }
}
