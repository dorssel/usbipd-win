// SPDX-FileCopyrightText: 2026 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Usbipd;

static class EtwLoggerExtensions
{
    public static ILoggingBuilder AddEtwLogger(this ILoggingBuilder builder)
    {
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<ILoggerProvider, EtwLoggerProvider>());
        return builder;
    }
}
