// SPDX-FileCopyrightText: 2023 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

// This file is used by Code Analysis to maintain SuppressMessage
// attributes that are applied to this project.
// Project-level suppressions either have no target or are given
// a specific target and scoped to a namespace, type, member, etc.

using System.Diagnostics.CodeAnalysis;

#if !NETSTANDARD
[assembly: SuppressMessage("Performance", "SYSLIB1045:Convert to 'GeneratedRegexAttribute'.", Justification = "Not available in netstandard2.0.",
    Scope = "NamespaceAndDescendants", Target = "~N:Usbipd.Automation")]
[assembly: SuppressMessage("Style", "IDE0057:Use range operator", Justification = "Not available in netstandard2.0.",
    Scope = "NamespaceAndDescendants", Target = "~N:Usbipd.Automation")]
#endif
