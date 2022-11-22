// SPDX-FileCopyrightText: 2022 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

using System.Text.Json.Serialization;
using Usbipd.Automation;

namespace Usbipd;

[JsonSerializable(typeof(State))]
sealed partial class StateSerializerContext : JsonSerializerContext
{
}
