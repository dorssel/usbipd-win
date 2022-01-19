// SPDX-FileCopyrightText: 2022 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-2.0-only

using System;
using System.Net;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using UsbIpServer;

namespace UnitTests
{
    [TestClass]
    sealed class UsbDevice_Tests
    {
        const string TestInstanceId = @"SOME\Device\Path\01234567";
        const string TestDescription = "Some Device Description";
        static readonly BusId TestBusId = BusId.Parse("3-42");
        static readonly Guid TestGuid = Guid.NewGuid();
        static readonly IPAddress TestIPAddress = IPAddress.Parse("1.2.3.4");
        const string TestStubInstanceId = @"SOME\Device\Path\abcd";

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
            Assert.AreEqual(device.InstanceId, TestInstanceId);
            Assert.AreEqual(device.Description, TestDescription);
            Assert.AreEqual(device.BusId, TestBusId);
            Assert.AreEqual(device.Guid, TestGuid);
            Assert.IsFalse(device.IsForced);
            Assert.AreEqual(device.IPAddress, TestIPAddress);
            Assert.AreEqual(device.StubInstanceId, TestStubInstanceId);
        }
    }
}
