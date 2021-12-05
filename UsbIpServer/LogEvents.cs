// SPDX-FileCopyrightText: 2021 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-2.0-only

using System;
using System.Net;
using Microsoft.Extensions.Logging;

namespace UsbIpServer
{
    static partial class LogEvents
    {
        [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Client {clientAddress} claimed device at {busId} ({instanceId}).")]
        public static partial void ClientAttach(this ILogger logger, IPAddress clientAddress, BusId busId, string instanceId);

        [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Client {clientAddress} released device at {busId} ({instanceId}).")]
        public static partial void ClientDetach(this ILogger logger, IPAddress clientAddress, BusId busId, string instanceId);

        [LoggerMessage(EventId = 3, Level = LogLevel.Error, Message = "An exception occurred while communicating with the client:")]
        public static partial void ClientError(this ILogger logger, Exception ex);

        [LoggerMessage(EventId = 4, Level = LogLevel.Error, Message = "An internal error occurred: {text}")]
        public static partial void InternalError(this ILogger logger, string text, Exception? ex = null);

        [LoggerMessage(EventId = 1000, Level = LogLevel.Debug, Message = "{text}")]
        public static partial void Debug(this ILogger logger, string text);

        [LoggerMessage(EventId = 1001, Level = LogLevel.Trace, Message = "{text}")]
        public static partial void Trace(this ILogger logger, string text);
    }
}
