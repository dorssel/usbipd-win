// SPDX-FileCopyrightText: 2025 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

namespace Usbipd;

/// <summary>
/// Just the stuff we actually use.
/// </summary>
interface IConsole
{
    TextWriter Out { get; }
    TextWriter Error { get; }

    bool IsOutputRedirected { get; }
    bool IsErrorRedirected { get; }

    void SetError(TextWriter newError);

    int WindowWidth { get; }
    int CursorLeft { get; set; }
}
