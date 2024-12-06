// SPDX-FileCopyrightText: 2023 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

namespace Usbipd;

readonly record struct RequestReply(uint Seqnum, byte[] Bytes);
