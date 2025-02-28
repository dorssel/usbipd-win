// SPDX-FileCopyrightText: 2024 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

using System.Data;
using Microsoft.Win32;
using Usbipd.Automation;

namespace UnitTests;

[TestClass]
sealed class Policy_Tests
{
    static readonly BusId TestBusId = BusId.Parse("3-42");
    static readonly VidPid TestHardwareId = VidPid.Parse("0123:cdef");
    static readonly BusId OtherBusId = BusId.Parse("1-1");
    static readonly VidPid OtherHardwareId = VidPid.Parse("4567:89ab");

    static TempRegistry BaseTempRegistryKey = null!;

    [ClassInitialize]
    public static void ClassInitialize(TestContext testContext)
    {
        _ = testContext;

        BaseTempRegistryKey = new TempRegistry(Registry.CurrentUser);
    }

    [ClassCleanup(ClassCleanupBehavior.EndOfClass)]
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
        _ = new PolicyRuleAutoBind(PolicyRuleEffect.Allow, TestBusId, TestHardwareId);
    }

    sealed class AutoBindData
    {
        // NOTE: MSTest cannot serialize PolicyRuleAutoBind (yet), so we have to deconstruct/reconstruct.

        static readonly PolicyRuleAutoBind[] _Valid = [
            new(PolicyRuleEffect.Allow, TestBusId, null),
            new(PolicyRuleEffect.Allow, null, TestHardwareId),
            new(PolicyRuleEffect.Allow, TestBusId, TestHardwareId),
        ];

        static readonly PolicyRuleAutoBind[] _Invalid = [
            new(PolicyRuleEffect.Allow, null, null),
            new(PolicyRuleEffect.Allow, BusId.IncompatibleHub, null),
            new(PolicyRuleEffect.Allow, BusId.IncompatibleHub, TestHardwareId),
        ];

        public static IEnumerable<object?[]> Valid
            => from rule in _Valid select new object?[] { rule.Effect, rule.BusId, rule.HardwareId };

        public static IEnumerable<object?[]> Invalid
            => from rule in _Invalid select new object?[] { rule.Effect, rule.BusId, rule.HardwareId };
    }

    [TestMethod]
    [DynamicData(nameof(AutoBindData.Valid), typeof(AutoBindData))]
    public void Persist_AutoBind(PolicyRuleEffect effect, BusId? busId, VidPid? hardwareId)
    {
        var rule = new PolicyRuleAutoBind(effect, busId, hardwareId);

        using var tempRegistry = CreateTempRegistry();
        rule.Save(tempRegistry.Key);

        var verify = PolicyRuleAutoBind.Load(rule.Effect, tempRegistry.Key);

        Assert.AreEqual(rule.BusId, verify.BusId);
        Assert.AreEqual(rule.HardwareId, verify.HardwareId);
    }

    [TestMethod]
    [DynamicData(nameof(AutoBindData.Valid), typeof(AutoBindData))]
    public void Valid_AutoBind(PolicyRuleEffect effect, BusId? busId, VidPid? hardwareId)
    {
        var rule = new PolicyRuleAutoBind(effect, busId, hardwareId);

        Assert.IsTrue(rule.IsValid());
    }

    [TestMethod]
    [DynamicData(nameof(AutoBindData.Invalid), typeof(AutoBindData))]
    public void Invalid_AutoBind(PolicyRuleEffect effect, BusId? busId, VidPid? hardwareId)
    {
        var rule = new PolicyRuleAutoBind(effect, busId, hardwareId);

        Assert.IsFalse(rule.IsValid());
    }

    static UsbDevice CreateTestUsbDevice(BusId busId, VidPid hardwareId)
    {
        return new UsbDevice($"VID_{hardwareId.Vid:X04}&PID_{hardwareId.Pid:X04}", "Description", false, busId, null, null, null);
    }

    [TestMethod]
    [DynamicData(nameof(AutoBindData.Invalid), typeof(AutoBindData))]
    public void Match_Invalid_AutoBind(PolicyRuleEffect effect, BusId? busId, VidPid? hardwareId)
    {
        var rule = new PolicyRuleAutoBind(effect, busId, hardwareId);

        Assert.ThrowsExactly<InvalidOperationException>(() =>
        {
            rule.Matches(CreateTestUsbDevice(TestBusId, TestHardwareId));
        });
    }

    [TestMethod]
    [DynamicData(nameof(AutoBindData.Valid), typeof(AutoBindData))]
    public void Match_Valid_AutoBind(PolicyRuleEffect effect, BusId? busId, VidPid? hardwareId)
    {
        var rule = new PolicyRuleAutoBind(effect, busId, hardwareId);

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
