// SPDX-FileCopyrightText: 2022 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

using System.Net;
using Usbipd.Automation;

namespace UnitTests;

[TestClass]
sealed class Automation_Tests
{
    const string TestInstanceId = @"testInstanceId\***VID_1234&PID_CDEF***";
    static readonly VidPid TestHardwareId = new(0x1234, 0xcdef);
    const string TestDescription = "testDescription";
    static readonly BusId TestBusId = new(1, 42);
    static readonly Guid TestPersistedGuid = Guid.Parse("{AD9D8376-6284-495E-A80B-FF1826D7447D}");
    const string TestStubInstanceId = "testStubInstanceId";
    static readonly IPAddress TestClientIPAddress = IPAddress.Parse("1.2.3.4");
    const string TestClientWslInstance = "testClientWslInstance";

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
    public void State_DataContract_Serialize()
    {
        var state = new State();
        var json = JsonHelpers.DataContractSerialize(state);
        json = JsonHelpers.NormalizePretty(json);
        Assert.AreEqual("""
            {
                "Devices": []
            }
            """, json);
    }

    [TestMethod]
    public void State_JsonSerializer_Serialize()
    {
        var state = new State();
        var json = JsonHelpers.TextJsonSerialize(state, StateSerializerContext.Default.State);
        json = JsonHelpers.NormalizePretty(json);
        Assert.AreEqual("""
            {
                "Devices": []
            }
            """, json);
    }

    [TestMethod]
    public void Device_Constructor()
    {
        var device = new Device();
        Assert.AreEqual(string.Empty, device.InstanceId);
        Assert.AreEqual(new VidPid(), device.HardwareId);
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
    public void Device_JsonConstructor()
    {
        var device = new Device(TestInstanceId, TestDescription, true, TestBusId, TestPersistedGuid, TestStubInstanceId, TestClientIPAddress, TestClientWslInstance);
        Assert.AreEqual(TestInstanceId, device.InstanceId);
        Assert.AreEqual(TestHardwareId, device.HardwareId);
        Assert.AreEqual(TestDescription, device.Description);
        Assert.IsTrue(device.IsForced);
        Assert.AreEqual(TestBusId, device.BusId);
        Assert.AreEqual(TestPersistedGuid, device.PersistedGuid);
        Assert.AreEqual(TestStubInstanceId, device.StubInstanceId);
        Assert.AreEqual(TestClientIPAddress, device.ClientIPAddress);
        Assert.AreEqual(TestClientWslInstance, device.ClientWslInstance);
        Assert.IsTrue(device.IsBound);
        Assert.IsTrue(device.IsConnected);
        Assert.IsTrue(device.IsAttached);
        Assert.IsTrue(device.IsWslAttached);
    }

    [TestMethod]
    public void Device_InstanceId()
    {
        var device = new Device()
        {
            InstanceId = TestInstanceId,
        };
        Assert.AreEqual(TestInstanceId, device.InstanceId);
    }

    [TestMethod]
    public void Device_HardwareId()
    {
        var device = new Device()
        {
            InstanceId = TestInstanceId,
        };
        Assert.AreEqual(TestHardwareId, device.HardwareId);
    }

    [TestMethod]
    public void Device_Description()
    {
        var device = new Device()
        {
            Description = TestDescription,
        };
        Assert.AreEqual(TestDescription, device.Description);
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
        var device = new Device()
        {
            BusId = TestBusId,
        };
        Assert.AreEqual(TestBusId, device.BusId);
        Assert.IsTrue(device.IsConnected);
    }

    [TestMethod]
    public void Device_PeristedGuid()
    {
        var device = new Device()
        {
            PersistedGuid = TestPersistedGuid,
        };
        Assert.AreEqual(TestPersistedGuid, device.PersistedGuid);
        Assert.IsTrue(device.IsBound);
    }

    [TestMethod]
    public void Device_StubInstanceId()
    {
        var device = new Device()
        {
            StubInstanceId = TestStubInstanceId,
        };
        Assert.AreEqual(TestStubInstanceId, device.StubInstanceId);
    }

    [TestMethod]
    public void Device_ClientIPAddress()
    {
        var device = new Device()
        {
            ClientIPAddress = TestClientIPAddress,
        };
        Assert.AreEqual(TestClientIPAddress, device.ClientIPAddress);
        Assert.IsTrue(device.IsAttached);
    }

    [TestMethod]
    public void Device_ClientWslInstance()
    {
        var device = new Device()
        {
            ClientWslInstance = TestClientWslInstance,
        };
        Assert.AreEqual(TestClientWslInstance, device.ClientWslInstance);
        Assert.IsTrue(device.IsWslAttached);
    }
}
