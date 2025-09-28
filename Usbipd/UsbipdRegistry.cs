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

sealed class UsbipdRegistry : IDisposable
{
    static readonly UsbipdRegistry _Instance = new(Registry.LocalMachine);
    public static UsbipdRegistry Instance => TestInstance ?? _Instance;

#pragma warning disable CS0649 // Field is never assigned to. Used only by UnitTests.
    internal static UsbipdRegistry? TestInstance;
#pragma warning restore CS0649

    readonly RegistryKey? ReadOnlyBaseKey;
    readonly RegistryKey? WritableBaseKey;

    public void Dispose()
    {
        ReadOnlyBaseKey?.Dispose();
        WritableBaseKey?.Dispose();
    }

    const string RegistryPath = @"SOFTWARE\usbipd-win";

    public UsbipdRegistry(RegistryKey parentKey)
    {
        try
        {
            ReadOnlyBaseKey = parentKey.OpenSubKey(RegistryPath, false);
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException) { }
        try
        {
            WritableBaseKey = parentKey.OpenSubKey(RegistryPath, true);
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException) { }
    }

    RegistryKey BaseKey(bool writable)
    {
        return ReadOnlyBaseKey is null
            ? throw new UnexpectedResultException("Registry key not found; try reinstalling the software.")
            : writable ? WritableBaseKey ?? throw new SecurityException("No write access to registry key.") : ReadOnlyBaseKey;
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
    public string? InstallationFolder => ReadOnlyBaseKey?.GetValue(ApplicationFolderName) as string;

    RegistryKey GetDevicesKey(bool writable)
    {
        return BaseKey(writable).OpenSubKey(DevicesName, writable)
            ?? throw new UnexpectedResultException("Registry key not found; try reinstalling the software.");
    }

    RegistryKey? GetDeviceKey(Guid guid, bool writable)
    {
        using var devicesKey = GetDevicesKey(writable);
        return devicesKey.OpenSubKey(guid.ToString("B"), writable);
    }

    RegistryKey GetPolicyKey(bool writable)
    {
        return BaseKey(writable).OpenSubKey(PolicyName, writable)
            ?? throw new UnexpectedResultException("Registry key not found; try reinstalling the software.");
    }

    RegistryKey? GetPolicyRuleKey(Guid guid, bool writable)
    {
        using var devicesKey = GetPolicyKey(writable);
        return devicesKey.OpenSubKey(guid.ToString("B"), writable);
    }

    public void Persist(string instanceId, string description)
    {
        var guid = Guid.NewGuid();
        using var deviceKey = GetDevicesKey(true).CreateSubKey($"{guid:B}");
        deviceKey.SetValue(InstanceIdName, instanceId);
        deviceKey.SetValue(DescriptionName, description);
    }

    public void StopSharingDevice(Guid guid)
    {
        using var devicesKey = GetDevicesKey(true);
        devicesKey.DeleteSubKeyTree(guid.ToString("B"), false);
    }

    public void StopSharingAllDevices()
    {
        using var devicesKey = GetDevicesKey(true);
        foreach (var subKeyName in devicesKey.GetSubKeyNames())
        {
            devicesKey.DeleteSubKeyTree(subKeyName, false);
        }
    }

    public RegistryKey SetDeviceAsAttached(Guid guid, BusId busId, IPAddress address, string stubInstanceId)
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

    public bool SetDeviceAsDetached(Guid guid)
    {
        using var deviceKey = GetDeviceKey(guid, false);
        return deviceKey is null || RemoveAttachedSubKey(deviceKey);
    }

    public bool SetAllDevicesAsDetached()
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
    public IEnumerable<UsbDevice> GetBoundDevices()
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
            // In the past, instance IDs where not normalized to upper case. Entries may still exist that have lower case letters.
            instanceId = instanceId.ToUpperInvariant();
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
            if (WindowsDevice.TryCreate(instanceId, out var device))
            {
                // The device exists. Note that it may not be present (as the original device), it could be attached via a stub device.
                // If the server is not running, ignore any left-over attaches as they are no longer valid.
                if (!ignoreAttached)
                {
                    using var attachedKey = deviceKey.OpenSubKey(AttachedName, false);
                    if (attachedKey is not null)
                    {
                        if (BusId.TryParse(attachedKey.GetValue(BusIdName) as string ?? "", out var busId)
                            && IPAddress.TryParse(attachedKey.GetValue(IPAddressName) as string ?? "", out var ipAddress)
                            && attachedKey.GetValue(InstanceIdName) is string stubInstanceId)
                        {
                            // In the past, instance IDs where not normalized to upper case. Entries may still exist that have lower case letters.
                            stubInstanceId = stubInstanceId.ToUpperInvariant();

                            // Everything checks out, report the device as attached.
                            persistedDevices.Add(instanceId, new(
                                InstanceId: instanceId,
                                Description: description,
                                Guid: guid,
                                IsForced: device.HasVBoxDriver,
                                BusId: busId,
                                IPAddress: ipAddress,
                                StubInstanceId: stubInstanceId));
                            continue;
                        }
                    }
                }
                // This device is not attached.
                persistedDevices.Add(instanceId, new(
                    InstanceId: instanceId,
                    Description: description,
                    Guid: guid,
                    IsForced: device.HasVBoxDriver,
                    BusId: device.IsPresent ? device.BusId : null,
                    IPAddress: null,
                    StubInstanceId: null));
            }
            else
            {
                // This device no longer exists (uninstalled), but we still have it persisted, it could be installed again.
                persistedDevices.Add(instanceId, new(
                    InstanceId: instanceId,
                    Description: description,
                    Guid: guid,
                    IsForced: false,
                    BusId: null,
                    IPAddress: null,
                    StubInstanceId: null));
            }
        }
        return persistedDevices.Values;
    }

    public bool HasWriteAccess => WritableBaseKey is not null;

    public Guid AddPolicyRule(PolicyRule rule)
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

    public void RemovePolicyRule(Guid guid)
    {
        using var policyKey = GetPolicyKey(true);
        policyKey.DeleteSubKeyTree(guid.ToString("B"), false);
    }

    public void RemovePolicyRuleAll()
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
    public SortedDictionary<Guid, PolicyRule> GetPolicyRules()
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
