// SPDX-FileCopyrightText: 2025 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

namespace Usbipd;

sealed class DefaultConsole : IConsole
{
    public TextWriter Out => Console.Out;

    public TextWriter Error => Console.Error;

    public bool IsOutputRedirected => Console.IsOutputRedirected;

    public bool IsErrorRedirected => Console.IsErrorRedirected;

    public int WindowWidth => Console.WindowWidth;

    public int CursorLeft { get => Console.CursorLeft; set => Console.CursorLeft = value; }

    public void SetError(TextWriter newError)
    {
        Console.SetError(newError);
    }
}
