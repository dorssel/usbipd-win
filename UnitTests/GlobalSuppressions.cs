﻿// SPDX-FileCopyrightText: 2022 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

// This file is used by Code Analysis to maintain SuppressMessage
// attributes that are applied to this project.
// Project-level suppressions either have no target or are given
// a specific target and scoped to a namespace, type, member, etc.

using System.Diagnostics.CodeAnalysis;

[assembly: SuppressMessage("Reliability", "CA2007:Consider calling ConfigureAwait on the awaited task",
    Justification = "there is never a synchronization context")]
[assembly: SuppressMessage("Performance", "CA1812:Internal class is never instantiated", Justification = "we use internal test classes")]
[assembly: SuppressMessage("Trimming",
    "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code",
    Justification = "Some unit tests require reflection")]
[assembly: SuppressMessage("Style", "IDE0053:Use expression body for lambda expression", Justification = "Better readability for Assert.ThrowsExactly")]
[assembly: SuppressMessage("Style", "IDE0058:Expression value is never used", Justification = "Not useful for tests")]
