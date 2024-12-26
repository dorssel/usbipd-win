// SPDX-FileCopyrightText: 2024 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

using System.Net;

namespace Usbipd;

static class Policy
{
    // If client == null, then it is ignored. This should only be used for listing the devices locally
    // to determine if the device is allowed for at least one client.
    // NOTE: client == null is currently also used by 'attach -wsl', until we can reliably detect WSL connections.
    public static bool IsAutoBindAllowed(UsbDevice device, IPAddress? client = null)
    {
        // Firewalling is not supported yet.
        _ = client;

        var rules = RegistryUtilities.GetPolicyRules();
        var allowed = rules.Values.Where(r => r.Effect == PolicyRuleEffect.Allow);
        var denied = rules.Values.Where(r => r.Effect == PolicyRuleEffect.Deny);

        return allowed.Any(r => r.Matches(device)) && !denied.Any(r => r.Matches(device));
    }
}
