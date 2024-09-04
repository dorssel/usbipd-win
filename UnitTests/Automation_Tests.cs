// SPDX-FileCopyrightText: 2022 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

using System.Net;
using System.Text.Json;
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

    static readonly Device TestDevice = new(TestInstanceId, TestDescription, true, TestBusId, TestPersistedGuid, TestStubInstanceId, TestClientIPAddress);
    readonly Device[] TestDevices = [TestDevice, new()];
    const string TestJson = """
        {
            "Devices": [
                {
                    "BusId": "1-42",
                    "ClientIPAddress": "1.2.3.4",
                    "Description": "testDescription",
                    "InstanceId": "testInstanceId\\***VID_1234&PID_CDEF***",
                    "IsForced": true,
                    "PersistedGuid": "ad9d8376-6284-495e-a80b-ff1826d7447d",
                    "StubInstanceId": "testStubInstanceId"
                },
                {
                    "BusId": null,
                    "ClientIPAddress": null,
                    "Description": "",
                    "InstanceId": "",
                    "IsForced": false,
                    "PersistedGuid": null,
                    "StubInstanceId": null
                }
            ]
        }
        """;

    [TestMethod]
    public void State_Constructor()
    {
        _ = new State();
    }

    [TestMethod]
    public void State_Devices()
    {
        var state = new State(TestDevices);
        Assert.AreEqual(2, state.Devices.Count);
    }

    [TestMethod]
    public void State_DataContract_Serialize()
    {
        var state = new State(TestDevices);
        var json = JsonHelpers.DataContractSerialize(state);
        json = JsonHelpers.NormalizePretty(json);
        Assert.AreEqual(TestJson, json);
    }

    [TestMethod]
    public void State_JsonSerializer_Serialize()
    {
        var state = new State(TestDevices);
        var json = JsonHelpers.TextJsonSerialize(state, StateSerializerContext.Default.State);
        json = JsonHelpers.NormalizePretty(json);
        Assert.AreEqual(TestJson, json);
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
        Assert.IsFalse(device.IsBound);
        Assert.IsFalse(device.IsConnected);
        Assert.IsFalse(device.IsAttached);
    }

    [TestMethod]
    public void Device_JsonConstructor()
    {
        var device = new Device(TestInstanceId, TestDescription, true, TestBusId, TestPersistedGuid, TestStubInstanceId, TestClientIPAddress);
        Assert.AreEqual(TestInstanceId, device.InstanceId);
        Assert.AreEqual(TestHardwareId, device.HardwareId);
        Assert.AreEqual(TestDescription, device.Description);
        Assert.IsTrue(device.IsForced);
        Assert.AreEqual(TestBusId, device.BusId);
        Assert.AreEqual(TestPersistedGuid, device.PersistedGuid);
        Assert.AreEqual(TestStubInstanceId, device.StubInstanceId);
        Assert.AreEqual(TestClientIPAddress, device.ClientIPAddress);
        Assert.IsTrue(device.IsBound);
        Assert.IsTrue(device.IsConnected);
        Assert.IsTrue(device.IsAttached);
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
    public void Device_BusId_Null()
    {
        var device = new Device()
        {
            BusId = null,
        };
        Assert.IsNull(device.BusId);
        Assert.IsFalse(device.IsConnected);
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
    public void Device_ClientIPAddress_Null()
    {
        var device = new Device()
        {
            ClientIPAddress = null,
        };
        Assert.IsNull(device.ClientIPAddress);
        Assert.IsFalse(device.IsAttached);
    }

    [TestMethod]
    public void JsonConverterBusId_Read_Valid()
    {
        var reader = new Utf8JsonReader("\"1-42\""u8);
        reader.Read();

        var converter = new JsonConverterBusId();
        var busId = converter.Read(ref reader, typeof(string), JsonSerializerOptions.Default);
        Assert.AreEqual(new BusId(1, 42), busId);
    }

    [TestMethod]
    public void JsonConverterBusId_Read_Invalid()
    {
        var converter = new JsonConverterBusId();
        Assert.ThrowsException<FormatException>(() =>
        {
            var reader = new Utf8JsonReader("\"xxx\""u8);
            reader.Read();

            _ = converter.Read(ref reader, typeof(string), JsonSerializerOptions.Default);
        });
    }

    [TestMethod]
    public void JsonConverterBusId_Read_Null()
    {
        var converter = new JsonConverterBusId();
        Assert.ThrowsException<InvalidDataException>(() =>
        {
            var reader = new Utf8JsonReader("null"u8);
            reader.Read();
            var busId = converter.Read(ref reader, typeof(string), JsonSerializerOptions.Default);
        });
    }

    [TestMethod]
    public void JsonConverterBusId_Write_Valid()
    {
        using var memoryStream = new MemoryStream();
        using var writer = new Utf8JsonWriter(memoryStream, new() { SkipValidation = true });
        {
            var converter = new JsonConverterBusId();
            converter.Write(writer, new(1, 42), JsonSerializerOptions.Default);
        }
        writer.Flush();
        CollectionAssert.AreEqual("\"1-42\""u8.ToArray(), memoryStream.ToArray());
    }

    [TestMethod]
    public void JsonConverterBusId_Write_NullWriter()
    {
        var converter = new JsonConverterBusId();
        Assert.ThrowsException<ArgumentNullException>(() =>
        {
            converter.Write(null!, new(1, 42), JsonSerializerOptions.Default);
        });
    }

    [TestMethod]
    public void JsonConverterIPAddress_Read_Valid()
    {
        var reader = new Utf8JsonReader("\"1.2.3.4\""u8);
        reader.Read();

        var converter = new JsonConverterIPAddress();
        var address = converter.Read(ref reader, typeof(string), JsonSerializerOptions.Default);
        Assert.AreEqual(IPAddress.Parse("1.2.3.4"), address);
    }

    [TestMethod]
    public void JsonConverterIPAddress_Read_Invalid()
    {
        var converter = new JsonConverterIPAddress();
        Assert.ThrowsException<FormatException>(() =>
        {
            var reader = new Utf8JsonReader("\"xxx\""u8);
            reader.Read();

            _ = converter.Read(ref reader, typeof(string), JsonSerializerOptions.Default);
        });
    }

    [TestMethod]
    public void JsonConverterIPAddress_Read_Null()
    {
        var converter = new JsonConverterIPAddress();
        Assert.ThrowsException<InvalidDataException>(() =>
        {
            var reader = new Utf8JsonReader("null"u8);
            reader.Read();
            var address = converter.Read(ref reader, typeof(string), JsonSerializerOptions.Default);
        });
    }

    [TestMethod]
    public void JsonConverterIPAddress_Write_Valid()
    {
        using var memoryStream = new MemoryStream();
        using var writer = new Utf8JsonWriter(memoryStream, new() { SkipValidation = true });
        {
            var converter = new JsonConverterIPAddress();
            converter.Write(writer, IPAddress.Parse("1.2.3.4"), JsonSerializerOptions.Default);
        }
        writer.Flush();
        CollectionAssert.AreEqual("\"1.2.3.4\""u8.ToArray(), memoryStream.ToArray());
    }

    [TestMethod]
    public void JsonConverterIPAddress_Write_NullWriter()
    {
        var converter = new JsonConverterIPAddress();
        Assert.ThrowsException<ArgumentNullException>(() =>
        {
            converter.Write(null!, IPAddress.Parse("1.2.3.4"), JsonSerializerOptions.Default);
        });
    }

    [TestMethod]
    public void JsonConverterIPAddress_Write_NullValue()
    {
        using var memoryStream = new MemoryStream();
        using var writer = new Utf8JsonWriter(memoryStream, new() { SkipValidation = true });
        var converter = new JsonConverterIPAddress();
        Assert.ThrowsException<ArgumentNullException>(() =>
        {
            converter.Write(writer, null!, JsonSerializerOptions.Default);
        });
    }
}
