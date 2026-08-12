// SPDX-FileCopyrightText: 2026 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

using Windows.Win32;
using Windows.Win32.System.Diagnostics.Etw;

namespace Usbipd;

[ProviderAlias("Etw")]
sealed class EtwLoggerProvider : ILoggerProvider
{
    REGHANDLE RegistrationHandle;

    public EtwLoggerProvider()
    {
        // The GUID is derived from the provider name "usbipd" using the algorithm described in
        // https://learn.microsoft.com/en-us/windows/win32/etw/guid-generation-algorithm

        unsafe // DevSkim: ignore DS172412
        {
            var result = PInvoke.EventRegister(new("{766A7E49-AC4D-5405-97C9-C9A2F7C4C458}"), null, null, out RegistrationHandle);
            if (result != 0)
            {
                RegistrationHandle = default;
            }
        }
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new EtwLogger(RegistrationHandle);
    }

    bool IsDisposed;
    public void Dispose()
    {
        if (!IsDisposed)
        {
            if (RegistrationHandle != default)
            {
                unsafe // DevSkim: ignore DS172412
                {
                    _ = PInvoke.EventUnregister(RegistrationHandle);
                    RegistrationHandle = default;
                }
            }
            IsDisposed = true;
        }
    }
}
