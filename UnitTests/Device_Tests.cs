// SPDX-FileCopyrightText: 2022 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

using System.Net;
using Usbipd.Automation;

namespace UnitTests;

[TestClass]
sealed class Device_Tests
{
    const string TestInstanceId = @"SOME\Device\Path\01234567";
    const string TestDescription = "Some Device Description";
    static readonly BusId TestBusId = BusId.Parse("3-42");
    static readonly Guid TestGuid = Guid.NewGuid();
    const string TestStubInstanceId = @"SOME\Device\Path\Bogus";
    static readonly IPAddress TestIPAddress = IPAddress.Parse("1.2.3.4");

    [TestMethod]
    public void Constructor()
    {
        var device = new Device(
            instanceId: TestInstanceId,
            description: TestDescription,
            isForced: false,
            busId: TestBusId,
            persistedGuid: TestGuid,
            stubInstanceId: TestStubInstanceId,
            clientIPAddress: TestIPAddress
            );
        Assert.AreEqual(TestInstanceId, device.InstanceId);
        Assert.AreEqual(TestDescription, device.Description);
        Assert.IsFalse(device.IsForced);
        Assert.AreEqual(TestBusId, device.BusId);
        Assert.AreEqual(TestGuid, device.PersistedGuid);
        Assert.AreEqual(TestIPAddress, device.ClientIPAddress);
        Assert.AreEqual(TestStubInstanceId, device.StubInstanceId);
    }
}
