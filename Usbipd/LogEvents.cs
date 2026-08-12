// SPDX-FileCopyrightText: 2021 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

using System.Net;
using Usbipd.Automation;

namespace Usbipd;

static partial class LogEvents
{
    [LoggerMessage(1, LogLevel.Information, "Client {clientAddress} claimed device at {busId} ({instanceId}).")]
    public static partial void ClientAttach(this ILogger logger, IPAddress clientAddress, BusId busId, string instanceId);

    [LoggerMessage(2, LogLevel.Information, "Client {clientAddress} released device at {busId} ({instanceId}).")]
    public static partial void ClientDetach(this ILogger logger, IPAddress clientAddress, BusId busId, string instanceId);

    [LoggerMessage(3, LogLevel.Error, "An exception occurred while communicating with the client.")]
    public static partial void ClientError(this ILogger logger, Exception ex);

    [LoggerMessage(4, LogLevel.Error, "An internal error occurred: {text}")]
    public static partial void InternalError(this ILogger logger, string text, Exception? ex = null);

    [LoggerMessage(5, LogLevel.Information, "Auto-bind of device at {busId} ({instanceId}) by client {clientAddress}.")]
    public static partial void AutoBind(this ILogger logger, IPAddress clientAddress, BusId busId, string instanceId);

    [LoggerMessage(1000, LogLevel.Debug, "{text}")]
    public static partial void Debug(this ILogger logger, string text);

    [LoggerMessage(1001, LogLevel.Trace, "{text}")]
    public static partial void Trace(this ILogger logger, string text);
}
