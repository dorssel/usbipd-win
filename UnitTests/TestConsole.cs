// SPDX-FileCopyrightText: 2025 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only


namespace UnitTests;

sealed class TestConsole : IConsole
{
    public TextWriter Out { get; } = new StringWriter();

    public TextWriter Error { get; private set; } = new StringWriter();

    public bool IsOutputRedirected => true;

    public bool IsErrorRedirected => true;

    public int WindowWidth => 80;

    public int CursorLeft { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

    public void SetError(TextWriter newError)
    {
        Error = newError;
    }

    public string OutText => (Out as StringWriter)?.ToString() ?? string.Empty;

    public string ErrorText => (Error as StringWriter)?.ToString() ?? string.Empty;
}
