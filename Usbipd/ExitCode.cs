// SPDX-FileCopyrightText: 2024 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

namespace Usbipd;

enum ExitCode
{
    Success = 0,
    Failure = 1,
    ParseError = 2,
    AccessDenied = 3,
    Canceled = 4,
};
