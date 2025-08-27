// SPDX-FileCopyrightText: 2025 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

using System.CommandLine;

namespace Usbipd;

static class ParseResultExtensions
{
    public static T? GetValueOrNull<T>(this ParseResult parseResult, Option<T> option) where T : struct
    {
        return parseResult.GetResult(option) is null ? null : parseResult.GetValue(option);
    }
}
