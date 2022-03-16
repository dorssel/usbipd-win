// SPDX-FileCopyrightText: 2022 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-2.0-only

using System;
using System.Net;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Usbipd.Automation;
using UsbIpServer;

namespace UnitTests
{
    [TestClass]
    sealed class Automation_Tests
    {
        [TestMethod]
        public void State_Constructor()
        {
            _ = new State();
        }

        [TestMethod]
        public void State_Devices()
        {
            var state = new State()
            {
                Devices = new Device[]
                {
                    new(),
                    new(),
                },
            };
            Assert.AreEqual(2, state.Devices.Count);
        }

        [TestMethod]
        public void Device_Constructor()
        {
            var device = new Device();
            Assert.AreEqual(string.Empty, device.InstanceId);
            Assert.AreEqual(string.Empty, device.Description);
            Assert.IsFalse(device.IsForced);
            Assert.IsNull(device.BusId);
            Assert.IsNull(device.PersistedGuid);
            Assert.IsNull(device.StubInstanceId);
            Assert.IsNull(device.ClientIPAddress);
            Assert.IsNull(device.ClientWslInstance);
            Assert.IsFalse(device.IsBound);
            Assert.IsFalse(device.IsConnected);
            Assert.IsFalse(device.IsAttached);
            Assert.IsFalse(device.IsWslAttached);
        }

        [TestMethod]
        public void Device_InstanceId()
        {
            const string testInstanceId = "testInstanceId";

            var device = new Device()
            {
                InstanceId = testInstanceId,
            };
            Assert.AreEqual(testInstanceId, device.InstanceId);
        }

        [TestMethod]
        public void Device_Description()
        {
            const string testDescription = "testDescription";

            var device = new Device()
            {
                Description = testDescription,
            };
            Assert.AreEqual(testDescription, device.Description);
        }

        [TestMethod]
        public void Device_IsForced()
        {
            var device = new Device()
            {
                IsForced = true,
            };
            Assert.IsTrue(device.IsForced);
        }

        [TestMethod]
        public void Device_BusId()
        {
            var testBusId = new BusId() { Bus = 1, Port = 42 };

            var device = new Device()
            {
                BusId = testBusId.ToString(),
            };
            Assert.AreEqual(testBusId.ToString(), device.BusId);
            Assert.IsTrue(device.IsConnected);
        }

        [TestMethod]
        public void Device_PeristedGuid()
        {
            var testPersistedGuid = Guid.NewGuid();

            var device = new Device()
            {
                PersistedGuid = testPersistedGuid,
            };
            Assert.AreEqual(testPersistedGuid, device.PersistedGuid);
            Assert.IsTrue(device.IsBound);
        }

        [TestMethod]
        public void Device_StubInstanceId()
        {
            const string testStubInstanceId = "testStubInstanceId";

            var device = new Device()
            {
                StubInstanceId = testStubInstanceId,
            };
            Assert.AreEqual(testStubInstanceId, device.StubInstanceId);
        }

        [TestMethod]
        public void Device_ClientIPAddress()
        {
            var testClientIPAddress = IPAddress.Parse("1.2.3.4");

            var device = new Device()
            {
                ClientIPAddress = testClientIPAddress,
            };
            Assert.AreEqual(testClientIPAddress, device.ClientIPAddress);
            Assert.IsTrue(device.IsAttached);
        }

        [TestMethod]
        public void Device_ClientWslInstance()
        {
            var testClientWslInstance = "testClientWslInstance";

            var device = new Device()
            {
                ClientWslInstance = testClientWslInstance,
            };
            Assert.AreEqual(testClientWslInstance, device.ClientWslInstance);
            Assert.IsTrue(device.IsWslAttached);
        }
    }
}
