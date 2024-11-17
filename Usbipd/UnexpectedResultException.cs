// SPDX-FileCopyrightText: 2020 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

namespace Usbipd;

sealed class UnexpectedResultException : Exception
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
