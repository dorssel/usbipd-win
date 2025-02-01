// SPDX-FileCopyrightText: Microsoft Corporation
//
// SPDX-License-Identifier: GPL-3.0-only

using System.Net;
using System.Security;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32;
using Usbipd.Automation;
using Windows.Win32;

namespace Usbipd;

static class RegistryUtilities
{
    const string DevicesRegistryPath = @"SOFTWARE\usbipd-win";

    static RegistryKey OpenBaseKey(bool writable)
    {
        return Registry.LocalMachine.OpenSubKey(DevicesRegistryPath, writable)
            ?? throw new UnexpectedResultException("Registry key not found; try reinstalling the software.");
    }

    static readonly Lazy<RegistryKey> ReadOnlyBaseKey = new(() => OpenBaseKey(false));
    static readonly Lazy<RegistryKey> WritableBaseKey = new(() => OpenBaseKey(true));

    static RegistryKey BaseKey(bool writable)
    {
        return (writable ? WritableBaseKey : ReadOnlyBaseKey).Value;
    }

    const string ApplicationFolderName = "APPLICATIONFOLDER";
    const string DevicesName = "Devices";
    const string InstanceIdName = "InstanceId";
    const string DescriptionName = "Description";
    const string AttachedName = "Attached";
    const string BusIdName = "BusId";
    const string IPAddressName = "IPAddress";
    const string PolicyName = "Policy";
    const string EffectName = "Effect";
    const string OperationName = "Operation";

    /// <summary>
    /// <see langword="null"/> if not installed
    /// </summary>
    public static string? InstallationFolder { get; } = Registry.LocalMachine.OpenSubKey(DevicesRegistryPath, false)?.GetValue(ApplicationFolderName) as string;

    static RegistryKey GetDevicesKey(bool writable)
    {
        return BaseKey(writable).OpenSubKey(DevicesName, writable)
            ?? throw new UnexpectedResultException("Registry key not found; try reinstalling the software.");
    }

    static RegistryKey? GetDeviceKey(Guid guid, bool writable)
    {
        using var devicesKey = GetDevicesKey(writable);
        return devicesKey.OpenSubKey(guid.ToString("B"), writable);
    }

    static RegistryKey GetPolicyKey(bool writable)
    {
        return BaseKey(writable).OpenSubKey(PolicyName, writable)
            ?? throw new UnexpectedResultException("Registry key not found; try reinstalling the software.");
    }

    static RegistryKey? GetPolicyRuleKey(Guid guid, bool writable)
    {
        using var devicesKey = GetPolicyKey(writable);
        return devicesKey.OpenSubKey(guid.ToString("B"), writable);
    }

    public static void Persist(string instanceId, string description)
    {
        var guid = Guid.NewGuid();
        using var deviceKey = GetDevicesKey(true).CreateSubKey($"{guid:B}");
        deviceKey.SetValue(InstanceIdName, instanceId);
        deviceKey.SetValue(DescriptionName, description);
    }

    public static void StopSharingDevice(Guid guid)
    {
        using var devicesKey = GetDevicesKey(true);
        devicesKey.DeleteSubKeyTree(guid.ToString("B"), false);
    }

    public static void StopSharingAllDevices()
    {
        using var devicesKey = GetDevicesKey(true);
        foreach (var subKeyName in devicesKey.GetSubKeyNames())
        {
            devicesKey.DeleteSubKeyTree(subKeyName, false);
        }
    }

    public static RegistryKey SetDeviceAsAttached(Guid guid, BusId busId, IPAddress address, string stubInstanceId)
    {
        using var key = GetDeviceKey(guid, true)
            ?? throw new UnexpectedResultException($"{nameof(SetDeviceAsAttached)}: Device key not found");
        var attached = key.CreateSubKey(AttachedName, true, RegistryOptions.Volatile)
            ?? throw new UnexpectedResultException($"{nameof(SetDeviceAsAttached)}: Unable to create ${AttachedName} subkey");
        // Allow users that are logged in on the console to delete the key (detach).
        var registrySecurity = attached.GetAccessControl(AccessControlSections.All);
        registrySecurity.AddAccessRule(new RegistryAccessRule(new SecurityIdentifier(WellKnownSidType.WinConsoleLogonSid, null),
            RegistryRights.Delete, AccessControlType.Allow));
        // Required for Windows 11 (WinConsoleLogonSid is not enough)
        registrySecurity.AddAccessRule(new RegistryAccessRule(new SecurityIdentifier(WellKnownSidType.InteractiveSid, null),
            RegistryRights.Delete, AccessControlType.Allow));
        attached.SetAccessControl(registrySecurity);
        try
        {
            attached.SetValue(BusIdName, busId);
            attached.SetValue(IPAddressName, address.ToString());
            attached.SetValue(InstanceIdName, stubInstanceId);
            return attached;
        }
        catch
        {
            attached.Dispose();
            throw;
        }
    }

    static bool RemoveAttachedSubKey(RegistryKey deviceKey)
    {
        // .NET does not have this functionality: delete a key to which you have rights while
        // you do not have rights to the containing key. So, we must use the API directly.
        // Instead of checking the return value we will check if the Attached key is actually gone.
        _ = PInvoke.RegDeleteKey(deviceKey.Handle, AttachedName);
        using var attached = deviceKey.OpenSubKey(AttachedName, false);
        return attached is null;
    }

    public static bool SetDeviceAsDetached(Guid guid)
    {
        using var deviceKey = GetDeviceKey(guid, false);
        return deviceKey is null || RemoveAttachedSubKey(deviceKey);
    }

    public static bool SetAllDevicesAsDetached()
    {
        using var devicesKey = GetDevicesKey(false);
        var deviceKeyNames = devicesKey?.GetSubKeyNames() ?? [];
        var failure = false;
        foreach (var deviceKeyName in deviceKeyNames)
        {
            using var deviceKey = devicesKey?.OpenSubKey(deviceKeyName, false);
            if (deviceKey is null)
            {
                continue;
            }
            if (!RemoveAttachedSubKey(deviceKey))
            {
                failure = true;
            }
        }
        return !failure;
    }

    /// <summary>
    /// Enumerates all bound devices.
    /// <para>
    /// This retrieves the entire (valid) registry state.
    /// </para>
    /// </summary>
    public static IEnumerable<UsbDevice> GetBoundDevices()
    {
        var guids = new SortedSet<Guid>();
        using var devicesKey = GetDevicesKey(false);
        foreach (var subKeyName in devicesKey.GetSubKeyNames())
        {
            if (Guid.TryParseExact(subKeyName, "B", out var guid))
            {
                // Sanitize uniqueness.
                _ = guids.Add(guid);
            }
        }
        var ignoreAttached = !Server.IsRunning();
        var persistedDevices = new Dictionary<string, UsbDevice>();
        foreach (var guid in guids)
        {
            using var deviceKey = GetDeviceKey(guid, false);
            if (deviceKey is null)
            {
                continue;
            }
            if (deviceKey.GetValue(InstanceIdName) is not string instanceId)
            {
                // Must exist.
                continue;
            }
            if (persistedDevices.ContainsKey(instanceId))
            {
                // Sanitize uniqueness.
                continue;
            }
            if (deviceKey.GetValue(DescriptionName) is not string description)
            {
                // Must exist.
                continue;
            }
            BusId? attachedBusId = null;
            IPAddress? attachedIPAddress = null;
            string? attachedStubInstanceId = null;
            if (!ignoreAttached)
            {
                // If the server is not running, ignore any left-over attaches as they are no longer valid.
                using var attachedKey = deviceKey.OpenSubKey(AttachedName, false);
                if (attachedKey is not null)
                {
                    if (BusId.TryParse(attachedKey.GetValue(BusIdName) as string ?? "", out var busId)
                        && IPAddress.TryParse(attachedKey.GetValue(IPAddressName) as string ?? "", out var ipAddress)
                        && attachedKey.GetValue(InstanceIdName) is string stubInstanceId)
                    {
                        attachedBusId = busId;
                        attachedIPAddress = ipAddress;
                        attachedStubInstanceId = stubInstanceId;
                    }
                }
            }
            persistedDevices.Add(instanceId, new(
                InstanceId: instanceId,
                Description: description,
                Guid: guid,
                IsForced: ConfigurationManager.HasVBoxDriver(instanceId),
                BusId: attachedBusId ?? ConfigurationManager.GetBusId(instanceId),
                IPAddress: attachedIPAddress,
                StubInstanceId: attachedStubInstanceId));
        }
        return persistedDevices.Values;
    }

    public static bool HasWriteAccess()
    {
        try
        {
            return BaseKey(true) is not null;
        }
        catch (SecurityException)
        {
            return false;
        }
    }

    public static Guid AddPolicyRule(PolicyRule rule)
    {
        if (!rule.IsValid())
        {
            throw new ArgumentException("Invalid policy rule", nameof(rule));
        }
        if (GetPolicyRules().ContainsValue(rule))
        {
            throw new ArgumentException("Duplicate policy rule", nameof(rule));
        }
        var guid = Guid.NewGuid();
        using var ruleKey = GetPolicyKey(true).CreateSubKey($"{guid:B}");
        ruleKey.SetValue(EffectName, rule.Effect.ToString());
        ruleKey.SetValue(OperationName, rule.Operation.ToString());
        rule.Save(ruleKey);
        return guid;
    }

    public static void RemovePolicyRule(Guid guid)
    {
        using var policyKey = GetPolicyKey(true);
        policyKey.DeleteSubKeyTree(guid.ToString("B"), false);
    }

    public static void RemovePolicyRuleAll()
    {
        using var policyKey = GetPolicyKey(true);
        foreach (var subKeyName in policyKey.GetSubKeyNames())
        {
            policyKey.DeleteSubKeyTree(subKeyName, false);
        }
    }

    /// <summary>
    /// Enumerates all rules.
    /// <para>
    /// This retrieves the entire (valid) registry state; it ignores invalid rules, as well as any duplicates.
    /// </para>
    /// </summary>
    public static SortedDictionary<Guid, PolicyRule> GetPolicyRules()
    {
        var guids = new SortedSet<Guid>();
        using var policyKey = GetPolicyKey(false);
        foreach (var subKeyName in policyKey.GetSubKeyNames())
        {
            if (Guid.TryParseExact(subKeyName, "B", out var guid))
            {
                // Sanitize uniqueness.
                _ = guids.Add(guid);
            }
        }

        var rules = new SortedDictionary<Guid, PolicyRule>();
        foreach (var guid in guids)
        {
            using var ruleKey = GetPolicyRuleKey(guid, false);
            if (ruleKey is null)
            {
                continue;
            }
            if (!Enum.TryParse<PolicyRuleEffect>(ruleKey.GetValue(EffectName) as string, true, out var effect))
            {
                // Must exist and be a valid enum string.
                continue;
            }
            if (!Enum.TryParse<PolicyRuleOperation>(ruleKey.GetValue(OperationName) as string, true, out var operation))
            {
                // Must exist and be a valid enum string.
                continue;
            }
            PolicyRule rule;
            switch (operation)
            {
                case PolicyRuleOperation.AutoBind:
                    rule = PolicyRuleAutoBind.Load(effect, ruleKey);
                    break;
                default:
                    // Invalid, ignore.
                    continue;
            }
            if (!rule.IsValid())
            {
                // Invalid, ignore.
                continue;
            }
            if (rules.ContainsValue(rule))
            {
                // Duplicate, ignore.
                continue;
            }
            rules.Add(guid, rule);
        }
        // All unique and valid.
        return rules;
    }
}
