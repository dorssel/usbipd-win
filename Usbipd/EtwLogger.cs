// SPDX-FileCopyrightText: 2026 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

using System.Diagnostics.Eventing.Reader;
using Windows.Win32;
using Windows.Win32.System.Diagnostics.Etw;

namespace Usbipd;

sealed class EtwLogger : ILogger
{
    readonly REGHANDLE RegistrationHandle;

    public EtwLogger(REGHANDLE registrationHandle)
    {
        RegistrationHandle = registrationHandle;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        return null;
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return RegistrationHandle != default && logLevel switch
        {
            LogLevel.Critical => PInvoke.EventProviderEnabled(RegistrationHandle, (byte)StandardEventLevel.Critical, 0x8000000000000000)
                || PInvoke.EventProviderEnabled(RegistrationHandle, (byte)StandardEventLevel.Critical, 0x4000000000000000),
            LogLevel.Error => PInvoke.EventProviderEnabled(RegistrationHandle, (byte)StandardEventLevel.Error, 0x8000000000000000)
                || PInvoke.EventProviderEnabled(RegistrationHandle, (byte)StandardEventLevel.Error, 0x4000000000000000),
            LogLevel.Warning => PInvoke.EventProviderEnabled(RegistrationHandle, (byte)StandardEventLevel.Warning, 0x8000000000000000)
                || PInvoke.EventProviderEnabled(RegistrationHandle, (byte)StandardEventLevel.Warning, 0x4000000000000000),
            LogLevel.Information => PInvoke.EventProviderEnabled(RegistrationHandle, (byte)StandardEventLevel.Informational, 0x8000000000000000)
                || PInvoke.EventProviderEnabled(RegistrationHandle, (byte)StandardEventLevel.Informational, 0x4000000000000000),
            LogLevel.Debug or LogLevel.Trace => (bool)PInvoke.EventProviderEnabled(RegistrationHandle, (byte)StandardEventLevel.Verbose, 0x4000000000000000),
            LogLevel.None or _ => false,
        };
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if ((RegistrationHandle == default) || !IsEnabled(logLevel))
        {
            return;
        }

        // We want to call the formatter at most once.
        string? message = null;

        // Log to the Operational channel, but only events that are not specifically Debug or Trace level.
        if (logLevel is not (LogLevel.Debug or LogLevel.Trace))
        {
            var descriptor = new EVENT_DESCRIPTOR
            {
                Id = (ushort)eventId.Id, // This works for Event Viewer
                Version = 0, // Not used by us
                Channel = 16, // Must be 16 to match the manifest Operational channel
                Level = (byte)(logLevel switch
                {
                    LogLevel.Critical => StandardEventLevel.Critical,
                    LogLevel.Error => StandardEventLevel.Error,
                    LogLevel.Warning => StandardEventLevel.Warning,
                    _ => StandardEventLevel.Informational
                }),
                Opcode = 0,
                Task = 0,
                Keyword = 0x8000000000000000
            };
            if (PInvoke.EventEnabled(RegistrationHandle, descriptor))
            {
                message = formatter(state, exception);
                unsafe // DevSkim: ignore DS172412
                {
                    fixed (char* pMessageChars = message)
                    {
                        Span<EVENT_DATA_DESCRIPTOR> dataDescriptors = [
                            new EVENT_DATA_DESCRIPTOR {
                                Ptr = (ulong)pMessageChars,
                                Size = (uint)((message.Length + 1) * sizeof(char)),
                                Reserved = 0
                            },
                        ];
                        _ = PInvoke.EventWrite(RegistrationHandle, descriptor, dataDescriptors);
                    }
                }
            }
        }

        // Log to the Debug channel.
        {
            var descriptor = new EVENT_DESCRIPTOR
            {
                Id = (ushort)eventId.Id, // This works for Event Viewer
                Version = 0, // Not used by us
                Channel = 17, // Must be 17 to match the manifest Debug channel
                Level = (byte)(logLevel switch
                {
                    LogLevel.Critical => StandardEventLevel.Critical,
                    LogLevel.Error => StandardEventLevel.Error,
                    LogLevel.Warning => StandardEventLevel.Warning,
                    LogLevel.Information => StandardEventLevel.Informational,
                    _ => StandardEventLevel.Verbose
                }),
                Opcode = 0,
                Task = 0,
                Keyword = 0x4000000000000000
            };
            if (PInvoke.EventEnabled(RegistrationHandle, descriptor))
            {
                message ??= formatter(state, exception);
                unsafe // DevSkim: ignore DS172412
                {
                    fixed (char* pMessageChars = message)
                    {
                        Span<EVENT_DATA_DESCRIPTOR> dataDescriptors = [
                            new EVENT_DATA_DESCRIPTOR {
                                    Ptr = (ulong)pMessageChars,
                                    Size = (uint)((message.Length + 1) * sizeof(char)),
                                    Reserved = 0
                                },
                            ];
                        _ = PInvoke.EventWrite(RegistrationHandle, descriptor, dataDescriptors);
                    }
                }
            }
        }
    }
}
