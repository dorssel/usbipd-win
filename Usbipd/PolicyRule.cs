// SPDX-FileCopyrightText: 2024 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

using Microsoft.Win32;

namespace Usbipd;

abstract record PolicyRule(bool Allow, PolicyRuleType Type)
{
    public abstract bool IsValid();

    public abstract bool Matches(UsbDevice usbDevice);

    public abstract void Save(RegistryKey registryKey);
}
