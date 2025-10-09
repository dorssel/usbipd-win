// SPDX-FileCopyrightText: 2025 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

using Microsoft.Win32;

namespace UnitTests;

[TestClass]
[DoNotParallelize]
sealed class UsbipdRegistry_Tests
{
    static RegistryKey TestBaseKey = null!;

    const string TestBaseKeyName = @"usbipd-win-tests-can-be-removed";

    [ClassInitialize]
    public static void ClassInitialize(TestContext testContext)
    {
        _ = testContext;

        TestBaseKey = Registry.CurrentUser.CreateSubKey(TestBaseKeyName);
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        Registry.CurrentUser.DeleteSubKeyTree(TestBaseKeyName, false);
    }

    string TestRegistryName = null!;
    RegistryKey TestRegistry = null!;

    [TestInitialize]
    public void TestInitialize()
    {
        // NOTE: None of these can be volatile, because UsbipdRegistry needs to be able to create stable subkeys in them.

        TestRegistryName = Guid.NewGuid().ToString("B");
        TestRegistry = TestBaseKey.CreateSubKey(TestRegistryName);
        using var usbipdKey = TestRegistry.CreateSubKey(@"SOFTWARE\usbipd-win");
        usbipdKey.SetValue("APPLICATIONFOLDER", AppDomain.CurrentDomain.BaseDirectory);
#pragma warning disable CS0436 // Type conflicts with imported type
        usbipdKey.SetValue("Version", GitVersionInformation.MajorMinorPatch);
#pragma warning restore CS0436 // Type conflicts with imported type
        using var devicesKey = usbipdKey.CreateSubKey("Devices");
        using var policyKey = usbipdKey.CreateSubKey("Policy");
        UsbipdRegistry.TestInstance?.Dispose();
        UsbipdRegistry.TestInstance = new(TestRegistry);
    }

    void SetUninstalled()
    {
        TestRegistry.DeleteSubKeyTree(@"SOFTWARE\usbipd-win", false);
        UsbipdRegistry.TestInstance?.Dispose();
        UsbipdRegistry.TestInstance = new(TestRegistry);
    }

    [TestCleanup]
    public void TestCleanup()
    {
        UsbipdRegistry.TestInstance?.Dispose();
        UsbipdRegistry.TestInstance = null;
        TestRegistry?.Dispose();
        TestRegistry = null!;
        TestBaseKey.DeleteSubKeyTree(TestRegistryName, false);
        TestRegistryName = null!;
    }

    [TestMethod]
    public void InstallationFolder()
    {
        Assert.IsNotNull(UsbipdRegistry.Instance.InstallationFolder);
    }

    [TestMethod]
    public void InstallationFolder_NotInstalled()
    {
        SetUninstalled();

        Assert.IsNull(UsbipdRegistry.Instance.InstallationFolder);
    }

    [TestMethod]
    public void Persist()
    {
        Assert.HasCount(0, UsbipdRegistry.Instance.GetBoundDevices());

        UsbipdRegistry.Instance.Persist(@"USB\VID_1111&PID_1111\111111", "Test 1");
        UsbipdRegistry.Instance.Persist(@"USB\VID_2222&PID_2222\222222", "Test 2");

        Assert.HasCount(2, UsbipdRegistry.Instance.GetBoundDevices());
    }

    [TestMethod]
    public void GetBoundDevices_MissingInstanceId()
    {
        Assert.HasCount(0, UsbipdRegistry.Instance.GetBoundDevices());

        UsbipdRegistry.Instance.Persist(@"USB\VID_1111&PID_1111\111111", "Test 1");
        UsbipdRegistry.Instance.Persist(@"USB\VID_2222&PID_2222\222222", "Test 2");

        var bound = UsbipdRegistry.Instance.GetBoundDevices();

        // Manually remove the InstanceId value from one of the devices.
        TestRegistry.OpenSubKey($@"SOFTWARE\usbipd-win\Devices\{bound.First().Guid:B}", true)!.DeleteValue("InstanceId");

        Assert.HasCount(1, UsbipdRegistry.Instance.GetBoundDevices());
        Assert.DoesNotContain(d => d.Guid == bound.First().Guid, UsbipdRegistry.Instance.GetBoundDevices());
    }

    [TestMethod]
    public void GetBoundDevices_DuplicateInstanceId()
    {
        Assert.HasCount(0, UsbipdRegistry.Instance.GetBoundDevices());

        // We test case invariance as well.
        UsbipdRegistry.Instance.Persist(@"USB\VID_1111&PID_1111\111111", "Test 1");
        UsbipdRegistry.Instance.Persist(@"usb\vid_1111&pid_1111\111111", "Test 2");

        Assert.HasCount(1, UsbipdRegistry.Instance.GetBoundDevices());
    }

    [TestMethod]
    public void GetBoundDevices_MissingDescription()
    {
        Assert.HasCount(0, UsbipdRegistry.Instance.GetBoundDevices());

        UsbipdRegistry.Instance.Persist(@"USB\VID_1111&PID_1111\111111", "Test 1");
        UsbipdRegistry.Instance.Persist(@"USB\VID_2222&PID_2222\222222", "Test 2");

        var bound = UsbipdRegistry.Instance.GetBoundDevices();

        // Manually remove the InstanceId value from one of the devices.
        TestRegistry.OpenSubKey($@"SOFTWARE\usbipd-win\Devices\{bound.First().Guid:B}", true)!.DeleteValue("Description");

        Assert.HasCount(1, UsbipdRegistry.Instance.GetBoundDevices());
        Assert.DoesNotContain(d => d.Guid == bound.First().Guid, UsbipdRegistry.Instance.GetBoundDevices());
    }

    [TestMethod]
    public void StopSharingDevice()
    {
        UsbipdRegistry.Instance.Persist(@"USB\VID_1111&PID_1111\111111", "Test 1");
        UsbipdRegistry.Instance.Persist(@"USB\VID_2222&PID_2222\222222", "Test 2");

        var bound = UsbipdRegistry.Instance.GetBoundDevices();

        UsbipdRegistry.Instance.StopSharingDevice(bound.First().Guid!.Value);

        Assert.HasCount(1, UsbipdRegistry.Instance.GetBoundDevices());
        Assert.DoesNotContain(d => d.Guid == bound.First().Guid, UsbipdRegistry.Instance.GetBoundDevices());
    }

    [TestMethod]
    public void StopSharingAllDevices()
    {
        UsbipdRegistry.Instance.Persist(@"USB\VID_1111&PID_1111\111111", "Test 1");
        UsbipdRegistry.Instance.Persist(@"USB\VID_2222&PID_2222\222222", "Test 2");

        Assert.HasCount(2, UsbipdRegistry.Instance.GetBoundDevices());

        UsbipdRegistry.Instance.StopSharingAllDevices();

        Assert.HasCount(0, UsbipdRegistry.Instance.GetBoundDevices());
    }

    [TestMethod]
    public void HasWriteAccess()
    {
        // The test registry is always writable.
        Assert.IsTrue(UsbipdRegistry.Instance.HasWriteAccess);
    }

    [TestMethod]
    public void AddPolicyRule()
    {
        var testRule = new PolicyRuleAutoBind(PolicyRuleEffect.Allow, new(1, 2), new(0x1111, 0x2222));

        UsbipdRegistry.Instance.AddPolicyRule(testRule);

        Assert.HasCount(1, UsbipdRegistry.Instance.GetPolicyRules());
    }

    [TestMethod]
    public void AddPolicyRule_Invalid()
    {
        var invalidRule = new PolicyRuleAutoBind(PolicyRuleEffect.Allow, null, null);

        Assert.ThrowsExactly<ArgumentException>(() =>
        {
            UsbipdRegistry.Instance.AddPolicyRule(invalidRule);
        });
        Assert.HasCount(0, UsbipdRegistry.Instance.GetPolicyRules());
    }

    [TestMethod]
    public void AddPolicyRule_Duplicate()
    {
        var testRule = new PolicyRuleAutoBind(PolicyRuleEffect.Allow, new(1, 2), new(0x1111, 0x2222));

        UsbipdRegistry.Instance.AddPolicyRule(testRule);

        Assert.ThrowsExactly<ArgumentException>(() =>
        {
            UsbipdRegistry.Instance.AddPolicyRule(testRule);
        });
        Assert.HasCount(1, UsbipdRegistry.Instance.GetPolicyRules());
    }

    [TestMethod]
    public void RemovePolicyRule()
    {
        var testRule1 = new PolicyRuleAutoBind(PolicyRuleEffect.Allow, new(1, 1), new(0x1111, 0x1111));
        var testRule2 = new PolicyRuleAutoBind(PolicyRuleEffect.Allow, new(2, 2), new(0x2222, 0x2222));

        UsbipdRegistry.Instance.AddPolicyRule(testRule1);
        UsbipdRegistry.Instance.AddPolicyRule(testRule2);
        Assert.HasCount(2, UsbipdRegistry.Instance.GetPolicyRules());

        var rules = UsbipdRegistry.Instance.GetPolicyRules();

        UsbipdRegistry.Instance.RemovePolicyRule(rules.First().Key);

        Assert.HasCount(1, UsbipdRegistry.Instance.GetPolicyRules());
        Assert.DoesNotContain(d => d.Key == rules.First().Key, UsbipdRegistry.Instance.GetPolicyRules());
    }

    [TestMethod]
    public void RemovePolicyRule_NonExistent()
    {
        var testRule1 = new PolicyRuleAutoBind(PolicyRuleEffect.Allow, new(1, 1), new(0x1111, 0x1111));
        var testRule2 = new PolicyRuleAutoBind(PolicyRuleEffect.Allow, new(2, 2), new(0x2222, 0x2222));

        UsbipdRegistry.Instance.AddPolicyRule(testRule1);
        UsbipdRegistry.Instance.AddPolicyRule(testRule2);
        Assert.HasCount(2, UsbipdRegistry.Instance.GetPolicyRules());

        var bogusGuid = Guid.Parse("12345678-0000-4000-0000-000000000000");
        UsbipdRegistry.Instance.RemovePolicyRule(bogusGuid);

        Assert.HasCount(2, UsbipdRegistry.Instance.GetPolicyRules());
    }

    [TestMethod]
    public void RemovePolicyRule_All()
    {
        var testRule1 = new PolicyRuleAutoBind(PolicyRuleEffect.Allow, new(1, 1), new(0x1111, 0x1111));
        var testRule2 = new PolicyRuleAutoBind(PolicyRuleEffect.Allow, new(2, 2), new(0x2222, 0x2222));

        UsbipdRegistry.Instance.AddPolicyRule(testRule1);
        UsbipdRegistry.Instance.AddPolicyRule(testRule2);
        Assert.HasCount(2, UsbipdRegistry.Instance.GetPolicyRules());

        UsbipdRegistry.Instance.RemovePolicyRuleAll();

        Assert.HasCount(0, UsbipdRegistry.Instance.GetPolicyRules());
    }

    [TestMethod]
    public void GetPolicyRules_NotInstalled()
    {
        SetUninstalled();

        Assert.ThrowsExactly<UnexpectedResultException>(() =>
        {
            _ = UsbipdRegistry.Instance.GetPolicyRules();
        });
    }
}
