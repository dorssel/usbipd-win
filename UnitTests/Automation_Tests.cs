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

    static readonly Device TestDevice = new(TestInstanceId, TestDescription, true, TestBusId, TestPersistedGuid, TestStubInstanceId, TestClientIPAddress, true);
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
                    "IsWslAttached": true,
                    "PersistedGuid": "ad9d8376-6284-495e-a80b-ff1826d7447d",
                    "StubInstanceId": "testStubInstanceId"
                },
                {
                    "BusId": null,
                    "ClientIPAddress": null,
                    "Description": "",
                    "InstanceId": "",
                    "IsForced": false,
                    "IsWslAttached": false,
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
        Assert.IsFalse(device.IsWslAttached);
        Assert.IsFalse(device.IsBound);
        Assert.IsFalse(device.IsConnected);
        Assert.IsFalse(device.IsAttached);
    }

    [TestMethod]
    public void Device_JsonConstructor()
    {
        var device = new Device(TestInstanceId, TestDescription, true, TestBusId, TestPersistedGuid, TestStubInstanceId, TestClientIPAddress, true);
        Assert.AreEqual(TestInstanceId, device.InstanceId);
        Assert.AreEqual(TestHardwareId, device.HardwareId);
        Assert.AreEqual(TestDescription, device.Description);
        Assert.IsTrue(device.IsForced);
        Assert.AreEqual(TestBusId, device.BusId);
        Assert.AreEqual(TestPersistedGuid, device.PersistedGuid);
        Assert.AreEqual(TestStubInstanceId, device.StubInstanceId);
        Assert.AreEqual(TestClientIPAddress, device.ClientIPAddress);
        Assert.IsTrue(device.IsWslAttached);
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
    public void IsWslAttached()
    {
        var device = new Device()
        {
            IsWslAttached = true,
        };
        Assert.IsTrue(device.IsWslAttached);
    }

    [TestMethod]
    public void NullableBusIdJsonConverter_Read_Valid()
    {
        var reader = new Utf8JsonReader("\"1-42\""u8);
        reader.Read();

        var converter = new NullableBusIdJsonConverter();
        var busId = converter.Read(ref reader, typeof(string), JsonSerializerOptions.Default);
        Assert.AreEqual(new BusId(1, 42), busId);
    }

    [TestMethod]
    public void NullableBusIdJsonConverter_Read_Invalid()
    {
        var converter = new NullableBusIdJsonConverter();
        Assert.ThrowsException<FormatException>(() =>
        {
            var reader = new Utf8JsonReader("\"xxx\""u8);
            reader.Read();

            _ = converter.Read(ref reader, typeof(string), JsonSerializerOptions.Default);
        });
    }

    [TestMethod]
    public void NullableBusIdJsonConverter_Read_Null()
    {
        var reader = new Utf8JsonReader("null"u8);
        reader.Read();

        var converter = new NullableBusIdJsonConverter();
        var busId = converter.Read(ref reader, typeof(string), JsonSerializerOptions.Default);
        Assert.IsNull(busId);
    }

    [TestMethod]
    public void NullableBusIdJsonConverter_Write_Valid()
    {
        using var memoryStream = new MemoryStream();
        using var writer = new Utf8JsonWriter(memoryStream, new() { SkipValidation = true });
        {
            var converter = new NullableBusIdJsonConverter();
            converter.Write(writer, new(1, 42), JsonSerializerOptions.Default);
        }
        writer.Flush();
        CollectionAssert.AreEqual("\"1-42\""u8.ToArray(), memoryStream.ToArray());
    }

    [TestMethod]
    public void NullableBusIdJsonConverter_Write_Null()
    {
        using var memoryStream = new MemoryStream();
        using var writer = new Utf8JsonWriter(memoryStream, new() { SkipValidation = true });
        {
            var converter = new NullableBusIdJsonConverter();
            converter.Write(writer, null, JsonSerializerOptions.Default);
        }
        writer.Flush();
        CollectionAssert.AreEqual("null"u8.ToArray(), memoryStream.ToArray());
    }

    [TestMethod]
    public void NullableIPAddressJsonConverter_Read_Valid()
    {
        var reader = new Utf8JsonReader("\"1.2.3.4\""u8);
        reader.Read();

        var converter = new NullableIPAddressJsonConverter();
        var address = converter.Read(ref reader, typeof(string), JsonSerializerOptions.Default);
        Assert.AreEqual(IPAddress.Parse("1.2.3.4"), address);
    }

    [TestMethod]
    public void NullableIPAddressJsonConverter_Read_Invalid()
    {
        var converter = new NullableIPAddressJsonConverter();
        Assert.ThrowsException<FormatException>(() =>
        {
            var reader = new Utf8JsonReader("\"xxx\""u8);
            reader.Read();

            _ = converter.Read(ref reader, typeof(string), JsonSerializerOptions.Default);
        });
    }

    [TestMethod]
    public void NullableIPAddressJsonConverter_Read_Null()
    {
        var reader = new Utf8JsonReader("null"u8);
        reader.Read();

        var converter = new NullableIPAddressJsonConverter();
        var address = converter.Read(ref reader, typeof(string), JsonSerializerOptions.Default);
        Assert.IsNull(address);
    }

    [TestMethod]
    public void NullableIPAddressJsonConverter_Write_Valid()
    {
        using var memoryStream = new MemoryStream();
        using var writer = new Utf8JsonWriter(memoryStream, new() { SkipValidation = true });
        {
            var converter = new NullableIPAddressJsonConverter();
            converter.Write(writer, IPAddress.Parse("1.2.3.4"), JsonSerializerOptions.Default);
        }
        writer.Flush();
        CollectionAssert.AreEqual("\"1.2.3.4\""u8.ToArray(), memoryStream.ToArray());
    }

    [TestMethod]
    public void NullableIPAddressJsonConverter_Write_Null()
    {
        using var memoryStream = new MemoryStream();
        using var writer = new Utf8JsonWriter(memoryStream, new() { SkipValidation = true });
        {
            var converter = new NullableIPAddressJsonConverter();
            converter.Write(writer, null, JsonSerializerOptions.Default);
        }
        writer.Flush();
        CollectionAssert.AreEqual("null"u8.ToArray(), memoryStream.ToArray());
    }
}
