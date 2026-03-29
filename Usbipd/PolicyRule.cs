// SPDX-FileCopyrightText: 2024 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

using Microsoft.Win32;
using Usbipd.Automation;

namespace Usbipd;

abstract record PolicyRule(PolicyRuleEffect Effect, PolicyRuleOperation Operation)
{
    public abstract bool IsValid();

    public abstract bool Matches(Device device);

    public abstract void Save(RegistryKey registryKey);
}
