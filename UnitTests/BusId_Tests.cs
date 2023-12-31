// SPDX-FileCopyrightText: 2022 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

using Usbipd.Automation;

namespace UnitTests;

[TestClass]
sealed class BusId_Tests
{
    [TestMethod]
    public void DefaultConstructor()
    {
        var busId = new BusId();

        Assert.AreEqual(0, busId.Bus);
        Assert.AreEqual(0, busId.Port);
        Assert.IsTrue(busId.IsIncompatibleHub);
    }

    [TestMethod]
    public void ConstructorWithInvalidBusThrows()
    {
        Assert.ThrowsException<ArgumentOutOfRangeException>(() =>
        {
            var busId = new BusId(0, 1);
        });
    }

    [TestMethod]
    public void ConstructorWithInvalidPortThrows()
    {
        Assert.ThrowsException<ArgumentOutOfRangeException>(() =>
        {
            var busId = new BusId(1, 0);
        });
    }

    [TestMethod]
    public void JsonConstructor()
    {
        const ushort testBus = 1;
        const ushort testPort = 42;

        var busId = new BusId(testBus, testPort);

        Assert.AreEqual(testBus, busId.Bus);
        Assert.AreEqual(testPort, busId.Port);
        Assert.IsFalse(busId.IsIncompatibleHub);
    }

    [TestMethod]
    public void ÍncompatibleHub()
    {
        Assert.AreEqual(0, BusId.IncompatibleHub.Bus);
        Assert.AreEqual(0, BusId.IncompatibleHub.Port);
        Assert.IsTrue(BusId.IncompatibleHub.IsIncompatibleHub);
    }

    sealed class BusIdData
    {
        static readonly string[] _Invalid = [
            "",
            "1",
            "1-",
            "-1",
            " 1-1",
            "1 -1",
            "1- 1",
            "1-1 ",
            "a-1",
            "1-a",
            "0-0",
            "0-1",
            "1-0",
            "1-65536",
            "65536-1",
            "01-1",
            "1-01",
        ];

        public static IEnumerable<string[]> Invalid
        {
            get => from value in _Invalid select new string[] { value };
        }

        static readonly string[] _Valid = [
            "IncompatibleHub",
            "1-1",
            "1-2",
            "1-65534",
            "1-65535",
            "2-1",
            "2-2",
            "2-65534",
            "2-65535",
            "65534-1",
            "65534-2",
            "65534-65534",
            "65534-65535",
            "65535-1",
            "65535-2",
            "65535-65534",
            "65535-65535",
        ];

        public static IEnumerable<string[]> Valid => from value in _Valid select new string[] { value };

        static int ExpectedCompare(string left, string right) =>
            BusFromValidBusId(left) < BusFromValidBusId(right) ? -1 :
            BusFromValidBusId(left) > BusFromValidBusId(right) ? 1 :
            PortFromValidBusId(left) < PortFromValidBusId(right) ? -1 :
            PortFromValidBusId(left) > PortFromValidBusId(right) ? 1 : 0;

        public static IEnumerable<object[]> Compare => from left in _Valid from right in _Valid select new object[] { left, right, ExpectedCompare(left, right) };
    }

    static ushort BusFromValidBusId(string text) => (text == "IncompatibleHub") ? (ushort)0 : ushort.Parse(text.Split('-')[0]);

    static ushort PortFromValidBusId(string text) => (text == "IncompatibleHub") ? (ushort)0 : ushort.Parse(text.Split('-')[1]);


    [TestMethod]
    [DynamicData(nameof(BusIdData.Invalid), typeof(BusIdData))]
    public void TryParseInvalid(string text)
    {
        var result = BusId.TryParse(text, out var busId);
        Assert.IsFalse(result);
        Assert.AreEqual(0, busId.Bus);
        Assert.AreEqual(0, busId.Port);
        Assert.IsTrue(busId.IsIncompatibleHub);
    }

    [TestMethod]
    [DynamicData(nameof(BusIdData.Valid), typeof(BusIdData))]
    public void TryParseValid(string text)
    {
        var result = BusId.TryParse(text, out var busId);
        Assert.IsTrue(result);
        Assert.AreEqual(BusFromValidBusId(text), busId.Bus);
        Assert.AreEqual(PortFromValidBusId(text), busId.Port);
    }

    [TestMethod]
    [DynamicData(nameof(BusIdData.Invalid), typeof(BusIdData))]
    public void ParseInvalid(string text)
    {
        Assert.ThrowsException<FormatException>(() =>
        {
            var busId = BusId.Parse(text);
        });
    }

    [TestMethod]
    [DynamicData(nameof(BusIdData.Valid), typeof(BusIdData))]
    public void ParseValid(string text)
    {
        var busId = BusId.Parse(text);
        Assert.AreEqual(BusFromValidBusId(text), busId.Bus);
        Assert.AreEqual(PortFromValidBusId(text), busId.Port);
    }

    [TestMethod]
    [DynamicData(nameof(BusIdData.Compare), typeof(BusIdData))]
    public void Compare(string left, string right, int expected)
    {
        var result = BusId.Parse(left).CompareTo(BusId.Parse(right));
        Assert.AreEqual(expected, result);
    }

    [TestMethod]
    [DynamicData(nameof(BusIdData.Compare), typeof(BusIdData))]
    public void LessThan(string left, string right, int expected)
    {
        var result = BusId.Parse(left) < BusId.Parse(right);
        Assert.AreEqual(expected < 0, result);
    }

    [TestMethod]
    [DynamicData(nameof(BusIdData.Compare), typeof(BusIdData))]
    public void LessThanOrEqual(string left, string right, int expected)
    {
        var result = BusId.Parse(left) <= BusId.Parse(right);
        Assert.AreEqual(expected <= 0, result);
    }

    [TestMethod]
    [DynamicData(nameof(BusIdData.Compare), typeof(BusIdData))]
    public void GreaterThan(string left, string right, int expected)
    {
        var result = BusId.Parse(left) > BusId.Parse(right);
        Assert.AreEqual(expected > 0, result);
    }

    [TestMethod]
    [DynamicData(nameof(BusIdData.Compare), typeof(BusIdData))]
    public void GreaterThanOrEqual(string left, string right, int expected)
    {
        var result = BusId.Parse(left) >= BusId.Parse(right);
        Assert.AreEqual(expected >= 0, result);
    }

    [TestMethod]
    [DynamicData(nameof(BusIdData.Valid), typeof(BusIdData))]
    public void ToStringValid(string text)
    {
        var busId = BusId.Parse(text);
        var result = busId.ToString();
        Assert.AreEqual(text, result);
    }
}
