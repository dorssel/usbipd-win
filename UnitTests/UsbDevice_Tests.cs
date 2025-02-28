// SPDX-FileCopyrightText: 2022 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

using System.Net;
using Usbipd.Automation;

namespace UnitTests;

[TestClass]
sealed class UsbDevice_Tests
{
    const string TestInstanceId = @"SOME\Device\Path\01234567";
    const string TestDescription = "Some Device Description";
    static readonly BusId TestBusId = BusId.Parse("3-42");
    static readonly Guid TestGuid = Guid.NewGuid();
    static readonly IPAddress TestIPAddress = IPAddress.Parse("1.2.3.4");
    const string TestStubInstanceId = @"SOME\Device\Path\Bogus";

    [TestMethod]
    public void Constructor()
    {
        var device = new UsbDevice(
            InstanceId: TestInstanceId,
            Description: TestDescription,
            IsForced: false,
            BusId: TestBusId,
            Guid: TestGuid,
            IPAddress: TestIPAddress,
            StubInstanceId: TestStubInstanceId
        );
        Assert.AreEqual(TestInstanceId, device.InstanceId);
        Assert.AreEqual(TestDescription, device.Description);
        Assert.AreEqual(TestBusId, device.BusId);
        Assert.AreEqual(TestGuid, device.Guid);
        Assert.IsFalse(device.IsForced);
        Assert.AreEqual(TestIPAddress, device.IPAddress);
        Assert.AreEqual(TestStubInstanceId, device.StubInstanceId);
    }
}
