// SPDX-FileCopyrightText: 2024 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

using Microsoft.Win32;
using Usbipd.Automation;

namespace UnitTests;

[TestClass]
sealed class Policy_Tests
{
    const string TestBusIdString = "3-42";
    const string TestHardwareIdString = "0123:cdef";
    const string OtherBusIdString = "1-1";
    const string OtherHardwareIdString = "4567:89ab";

    static readonly BusId TestBusId = BusId.Parse(TestBusIdString);
    static readonly VidPid TestHardwareId = VidPid.Parse(TestHardwareIdString);
    static readonly BusId OtherBusId = BusId.Parse(OtherBusIdString);
    static readonly VidPid OtherHardwareId = VidPid.Parse(OtherHardwareIdString);

    static TempRegistry BaseTempRegistryKey = null!;

    [ClassInitialize]
    public static void ClassInitialize(TestContext testContext)
    {
        _ = testContext;

        BaseTempRegistryKey = new TempRegistry(Registry.CurrentUser);
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        BaseTempRegistryKey.Dispose();
    }

    static TempRegistry CreateTempRegistry()
    {
        return new(BaseTempRegistryKey.Key);
    }

    [TestMethod]
    public void Constructor_Success()
    {
        _ = new PolicyRuleAutoBind(PolicyRuleEffect.Allow, BusId.Parse(TestBusIdString), VidPid.Parse(TestHardwareIdString));
    }

    // NOTE: MSTest cannot serialize PolicyRuleAutoBind, so we have to deconstruct/reconstruct.
    // NOTE: VS cannot serialize BusId or VidPid (no DataContract), so we pass them as strings.

    public static IEnumerable<(PolicyRuleEffect, string?, string?)> ValidAutoBinds = [
        (PolicyRuleEffect.Allow, TestBusIdString, null),
        (PolicyRuleEffect.Allow, null, TestHardwareIdString),
        (PolicyRuleEffect.Allow, TestBusIdString, TestHardwareIdString),
    ];

    public static IEnumerable<(PolicyRuleEffect, string?, string?)> InvalidAutoBinds = [
        (PolicyRuleEffect.Allow, null, null),
        (PolicyRuleEffect.Allow, nameof(BusId.IncompatibleHub), null),
        (PolicyRuleEffect.Allow, nameof(BusId.IncompatibleHub), TestHardwareIdString),
    ];

    static PolicyRuleAutoBind ConstructPolicyRuleAutoBind(PolicyRuleEffect effect, string? busIdString, string? vidPidString)
    {
        return new(effect, busIdString is null ? null : BusId.Parse(busIdString), vidPidString is null ? null : VidPid.Parse(vidPidString));
    }

    [TestMethod]
    [DynamicData(nameof(ValidAutoBinds))]
    public void Persist_AutoBind(PolicyRuleEffect effect, string? busIdString, string? vidPidString)
    {
        var rule = ConstructPolicyRuleAutoBind(effect, busIdString, vidPidString);

        using var tempRegistry = CreateTempRegistry();
        rule.Save(tempRegistry.Key);

        var verify = PolicyRuleAutoBind.Load(rule.Effect, tempRegistry.Key);

        Assert.AreEqual(rule.BusId, verify.BusId);
        Assert.AreEqual(rule.HardwareId, verify.HardwareId);
    }

    [TestMethod]
    [DynamicData(nameof(ValidAutoBinds))]
    public void Valid_AutoBind(PolicyRuleEffect effect, string? busIdString, string? vidPidString)
    {
        var rule = ConstructPolicyRuleAutoBind(effect, busIdString, vidPidString);

        Assert.IsTrue(rule.IsValid());
    }

    [TestMethod]
    [DynamicData(nameof(InvalidAutoBinds))]
    public void Invalid_AutoBind(PolicyRuleEffect effect, string? busIdString, string? vidPidString)
    {
        var rule = ConstructPolicyRuleAutoBind(effect, busIdString, vidPidString);

        Assert.IsFalse(rule.IsValid());
    }

    static UsbDevice CreateTestUsbDevice(BusId busId, VidPid hardwareId)
    {
        return new UsbDevice($"VID_{hardwareId.Vid:X04}&PID_{hardwareId.Pid:X04}", "Description", false, busId, null, null, null);
    }

    [TestMethod]
    [DynamicData(nameof(InvalidAutoBinds))]
    public void Match_Invalid_AutoBind(PolicyRuleEffect effect, string? busIdString, string? vidPidString)
    {
        var rule = ConstructPolicyRuleAutoBind(effect, busIdString, vidPidString);

        Assert.ThrowsExactly<InvalidOperationException>(() =>
        {
            rule.Matches(CreateTestUsbDevice(TestBusId, TestHardwareId));
        });
    }

    [TestMethod]
    [DynamicData(nameof(ValidAutoBinds))]
    public void Match_Valid_AutoBind(PolicyRuleEffect effect, string? busIdString, string? vidPidString)
    {
        var rule = ConstructPolicyRuleAutoBind(effect, busIdString, vidPidString);

        Assert.IsTrue(rule.Matches(CreateTestUsbDevice(TestBusId, TestHardwareId)));
        Assert.IsFalse(rule.Matches(CreateTestUsbDevice(OtherBusId, OtherHardwareId)));
        if (rule.BusId is null)
        {
            Assert.IsTrue(rule.Matches(CreateTestUsbDevice(OtherBusId, TestHardwareId)));
        }
        else
        {
            Assert.IsFalse(rule.Matches(CreateTestUsbDevice(OtherBusId, TestHardwareId)));
        }
        if (rule.HardwareId is null)
        {
            Assert.IsTrue(rule.Matches(CreateTestUsbDevice(TestBusId, OtherHardwareId)));
        }
        else
        {
            Assert.IsFalse(rule.Matches(CreateTestUsbDevice(TestBusId, OtherHardwareId)));
        }
    }
}
