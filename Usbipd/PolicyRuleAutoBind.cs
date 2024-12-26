// SPDX-FileCopyrightText: 2024 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

using Microsoft.Win32;
using Usbipd.Automation;

namespace Usbipd;

sealed record PolicyRuleAutoBind(PolicyRuleEffect Effect, BusId? BusId, VidPid? HardwareId)
    : PolicyRule(Effect, PolicyRuleOperation.AutoBind)
{
    const string BusIdName = "BusId";
    const string HardwareIdName = "HardwareId";

    public override bool IsValid()
    {
        return (BusId.HasValue || HardwareId.HasValue) && !(BusId.HasValue && BusId.Value.IsIncompatibleHub);
    }

    public override bool Matches(UsbDevice usbDevice)
    {
        return IsValid()
            ? (!BusId.HasValue || BusId.Value == usbDevice.BusId) && (!HardwareId.HasValue || HardwareId.Value == usbDevice.HardwareId)
            : throw new InvalidOperationException("Invalid policy rule");
    }

    public override void Save(RegistryKey registryKey)
    {
        if (BusId.HasValue)
        {
            registryKey.SetValue(BusIdName, BusId.Value.ToString());
        }
        if (HardwareId.HasValue)
        {
            registryKey.SetValue(HardwareIdName, HardwareId.Value.ToString());
        }
    }

    public static PolicyRuleAutoBind Load(PolicyRuleEffect access, RegistryKey registryKey)
    {
        BusId? busId = null;
        if (Automation.BusId.TryParse(registryKey.GetValue(BusIdName) as string ?? string.Empty, out var parsedBusId))
        {
            busId = parsedBusId;
        }
        VidPid? hardwareId = null;
        if (VidPid.TryParse(registryKey.GetValue(HardwareIdName) as string ?? string.Empty, out var parsedHardwareId))
        {
            hardwareId = parsedHardwareId;
        }
        return new(access, busId, hardwareId);
    }
}
