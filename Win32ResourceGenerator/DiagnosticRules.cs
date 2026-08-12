// SPDX-FileCopyrightText: 2026 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

using Microsoft.CodeAnalysis;

namespace Win32ResourceGenerator;

static class DiagnosticRules
{
    const string Category = "Win32ResourceGenerator.LoggerMessage";

#pragma warning disable RS2008 // Enable analyzer release tracking

    public static readonly DiagnosticDescriptor DuplicateIdRule = new("EM001", "Duplicate EventId detected",
        "The EventId '{0}' is already used by method '{1}'. Event IDs must be unique across the project.",
        Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor MissingPropertyRule = new("EM002", "Property missing",
        "The property '{0}' is missing",
        Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor DuplicatePropertyRule = new("EM003", "Duplicate Property",
        "The property '{0}' is specified more than once. Properties should be set either by constructor arguments or by named arguments, but not both.",
        Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor InvalidEventIdRule = new("EM004", "Invalid EventId",
        "The EventId '{0}' is not valid. Event IDs must be between 1 and 65535, id 0 is not allowed.",
        Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor InvalidLevelRule = new("EM005", "Invalid Level",
        "The level '{0}' is not valid. Levels must be between LogLevel.Trace and LogLevel.Critical, LogLevel.None is not supported.",
        Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor InvalidMessageRule = new("EM006", "Invalid Message",
        "The message '{0}' is not valid. Messages must be non-empty and non-whitespace.",
        Category, DiagnosticSeverity.Error, true);

#pragma warning restore RS2008 // Enable analyzer release tracking
}
