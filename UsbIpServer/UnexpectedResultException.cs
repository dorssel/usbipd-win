// SPDX-FileCopyrightText: 2020 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-2.0-only

using System;

namespace UsbIpServer
{
    public sealed class UnexpectedResultException : Exception
    {
        public UnexpectedResultException()
        {
        }

        public UnexpectedResultException(string message) : base(message)
        {
        }

        public UnexpectedResultException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
