// SPDX-FileCopyrightText: 2024 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

using System.CommandLine;
using System.CommandLine.Parsing;

namespace Usbipd;

static class ParseResultExtensions
{
    public static T? GetValueForOptionOrNull<T>(this ParseResult parseResult, Option<T> option) where T : struct
    {
        return parseResult.HasOption(option) ? parseResult.GetValueForOption(option) : null;
    }
}
