// SPDX-FileCopyrightText: 2022 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

using System.ComponentModel;
using System.IO.Pipelines;
using System.Net;
using Windows.Win32.Devices.DeviceAndDriverInstallation;
using Windows.Win32.Devices.Usb;
using Windows.Win32.Foundation;
using static Usbipd.Interop.Linux;
using static Usbipd.Interop.UsbIp;
using static Usbipd.Interop.VBoxUsb;
using static Usbipd.Tools;

namespace UnitTests;

[TestClass]
sealed class Tools_Tests
{
    public TestContext TestContext { get; set; }

    static readonly byte[] TestStreamBytes = [
#pragma warning disable format
        0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08,
#pragma warning restore format
    ];

    [TestMethod]
    public async Task ReadMessageAsync_Success()
    {
        using var memoryStream = new MemoryStream(TestStreamBytes);
        var buf = new byte[TestStreamBytes.Length - 1];
        await memoryStream.ReadMessageAsync(buf, TestContext.CancellationToken);
        Assert.AreEqual(TestStreamBytes.Length - 1, memoryStream.Position);
        Assert.IsTrue(buf.SequenceEqual(TestStreamBytes.AsSpan(0..^1)));
    }

    [TestMethod]
    public async Task ReadMessageAsync_Nothing()
    {
        using var memoryStream = new MemoryStream(TestStreamBytes);
        await memoryStream.ReadMessageAsync(Array.Empty<byte>(), TestContext.CancellationToken);
    }

    [TestMethod]
    public async Task ReadMessageAsync_EndOfStream()
    {
        using var memoryStream = new MemoryStream();
        var buf = new byte[TestStreamBytes.Length];
        var exception = await Assert.ThrowsExactlyAsync<EndOfStreamException>(async () =>
        {
            await memoryStream.ReadMessageAsync(buf, TestContext.CancellationToken);
        });
    }

    [TestMethod]
    public async Task ReadMessageAsync_ProtocolViolation()
    {
        using var memoryStream = new MemoryStream(TestStreamBytes);
        var buf = new byte[TestStreamBytes.Length + 1];
        var exception = await Assert.ThrowsExactlyAsync<ProtocolViolationException>(async () =>
        {
            await memoryStream.ReadMessageAsync(buf, TestContext.CancellationToken);
        });
    }

    [TestMethod]
    public async Task ReadMessageAsync_Parts()
    {
        var pipe = new Pipe();
        using var readStream = pipe.Reader.AsStream();
        using var writeStream = pipe.Writer.AsStream();

        var buf = new byte[TestStreamBytes.Length - 1];
        writeStream.Write(TestStreamBytes.AsSpan(0, 1));
#pragma warning disable CA2025 // Do not pass 'IDisposable' instances into unawaited tasks
        var task = readStream.ReadMessageAsync(buf, CancellationToken.None);
#pragma warning restore CA2025 // Do not pass 'IDisposable' instances into unawaited tasks
        await Task.Delay(100, TestContext.CancellationToken);
        Assert.AreEqual(TaskStatus.WaitingForActivation, task.Status);
        writeStream.Write(TestStreamBytes.AsSpan(1));
        await task.WaitAsync(TestContext.CancellationToken);
        Assert.IsTrue(buf.SequenceEqual(TestStreamBytes.AsSpan(0..^1)));
    }

    struct TestStructType
    {
        public int i;
        public bool b;
        public long l;
    }

    static readonly TestStructType TestStruct = new()
    {
        i = 0x01020304,
        b = true,
        l = 0x1112131415161718,
    };

    static readonly byte[] TestStructBytes = [
#pragma warning disable format
        0x04, 0x03, 0x02, 0x01, // i
        0x01, 0x00, 0x00, 0x00, // b
        0x18, 0x17, 0x16, 0x015, 0x14, 0x13, 0x12, 0x11, // l
#pragma warning restore format
    ];

    [TestMethod]
    public void StructToBytes_Span_Success()
    {
        var buf = new byte[TestStructBytes.Length];
        StructToBytes(TestStruct, buf);
        Assert.IsTrue(buf.SequenceEqual(TestStructBytes));
    }

    [TestMethod]
    public void StructToBytes_Span_Short()
    {
        var buf = new byte[TestStructBytes.Length - 1];
        Assert.ThrowsExactly<ArgumentException>(() =>
        {
            StructToBytes(TestStruct, buf);
        });
    }

    [TestMethod]
    public void StructToBytes_Success()
    {
        var buf = StructToBytes(TestStruct);
        Assert.IsTrue(buf.SequenceEqual(TestStructBytes));
    }

    [TestMethod]
    public void BytesToStruct_out_Success()
    {
        BytesToStruct(TestStructBytes, out TestStructType s);
        Assert.AreEqual(TestStruct, s);
    }

    [TestMethod]
    public void BytesToStruct_out_Short()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
        {
            BytesToStruct(TestStructBytes.AsSpan()[0..^1], out TestStructType s);
        });
    }

    [TestMethod]
    public void BytesToStruct_Success()
    {
        var s = BytesToStruct<TestStructType>(TestStructBytes);
        Assert.AreEqual(TestStruct, s);
    }

    [TestMethod]
    public void BytesToStruct_Short()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
        {
            BytesToStruct<TestStructType>(TestStructBytes.AsSpan()[0..^1]);
        });
    }

    sealed class SpeedData
    {
        static readonly Dictionary<USB_DEVICE_SPEED, UsbDeviceSpeed> _KnownGood = new()
        {
            { USB_DEVICE_SPEED.UsbLowSpeed, UsbDeviceSpeed.USB_SPEED_LOW },
            { USB_DEVICE_SPEED.UsbFullSpeed, UsbDeviceSpeed.USB_SPEED_FULL },
            { USB_DEVICE_SPEED.UsbHighSpeed, UsbDeviceSpeed.USB_SPEED_HIGH },
            { USB_DEVICE_SPEED.UsbSuperSpeed, UsbDeviceSpeed.USB_SPEED_SUPER },
            { (USB_DEVICE_SPEED)0x0badf00d, UsbDeviceSpeed.USB_SPEED_UNKNOWN },
        };

        public static IEnumerable<object[]> KnownGood => from value in _KnownGood select new object[] { value.Key, value.Value };
    }

    [TestMethod]
    [DynamicData(nameof(SpeedData.KnownGood), typeof(SpeedData))]
    public void MapWindowsSpeedToLinuxSpeed_Test(USB_DEVICE_SPEED windows, UsbDeviceSpeed linux)
    {
        Assert.AreEqual(linux, MapWindowsSpeedToLinuxSpeed(windows));
    }

    sealed class ErrorData
    {
        static readonly Dictionary<UsbSupError, Errno> _KnownGood = new()
        {
            { UsbSupError.USBSUP_XFER_OK, Errno.SUCCESS },
            { UsbSupError.USBSUP_XFER_STALL, Errno.EPIPE },
            { UsbSupError.USBSUP_XFER_DNR, Errno.ETIME },
            { UsbSupError.USBSUP_XFER_CRC, Errno.EILSEQ },
            { UsbSupError.USBSUP_XFER_NAC, Errno.EPROTO },
            { UsbSupError.USBSUP_XFER_UNDERRUN, Errno.EREMOTEIO },
            { UsbSupError.USBSUP_XFER_OVERRUN, Errno.EOVERFLOW },
            { (UsbSupError)0xbaadf00d, Errno.EPROTO },
        };

        public static IEnumerable<object[]> KnownGood => from value in _KnownGood select new object[] { value.Key, value.Value };
    }

    [TestMethod]
    [DynamicData(nameof(ErrorData.KnownGood), typeof(ErrorData))]
    public void ConvertError_Test(UsbSupError vbox, Errno linux)
    {
        Assert.AreEqual(linux, ConvertError(vbox));
    }

    [TestMethod]
    public void ThrowOnError_Win32Success()
    {
        BOOL success = true;
        success.ThrowOnWin32Error("dummy");
    }

    [TestMethod]
    public void ThrowOnError_Win32Error()
    {
        const string testMessage = "TestMessage";
        BOOL failure = false;
        var exception = Assert.ThrowsExactly<Win32Exception>(() =>
        {
            failure.ThrowOnWin32Error(testMessage);
        });
        Assert.Contains(testMessage, exception.Message);
    }

    [TestMethod]
    public void ThrowOnError_ConfigSuccess()
    {
        var success = CONFIGRET.CR_SUCCESS;
        success.ThrowOnError("dummy");
    }

    [TestMethod]
    public void ThrowOnError_ConfigError()
    {
        const string testMessage = "TestMessage";
        var failure = CONFIGRET.CR_INVALID_DATA;
        var exception = Assert.ThrowsExactly<ConfigurationManagerException>(() =>
        {
            failure.ThrowOnError(testMessage);
        });
        Assert.Contains(testMessage, exception.Message);
    }

    [TestMethod]
    public void RawEndpoint_Input()
    {
        const byte testEndpoint = 42;
        UsbIpHeaderBasic basic = new()
        {
            ep = testEndpoint,
            direction = UsbIpDir.USBIP_DIR_IN,
        };
        var rawEndpoint = basic.RawEndpoint();
        Assert.AreEqual(testEndpoint | 0x80, rawEndpoint);
    }

    [TestMethod]
    public void RawEndpoint_Output()
    {
        const byte testEndpoint = 42;
        UsbIpHeaderBasic basic = new()
        {
            ep = testEndpoint,
            direction = UsbIpDir.USBIP_DIR_OUT,
        };
        var rawEndpoint = basic.RawEndpoint();
        Assert.AreEqual(testEndpoint, rawEndpoint);
    }

    [TestMethod]
    public void EndpointType_MSG()
    {
        var basic = new UsbIpHeaderBasic() { ep = 0 };
        // Bogus number_of_packets + bogus interval, endpoint has precedence.
        var submit = new UsbIpHeaderCmdSubmit() { number_of_packets = 42, interval = 42 };
        var type = basic.EndpointType(submit);
        Assert.AreEqual(UsbSupTransferType.USBSUP_TRANSFER_TYPE_MSG, type);
    }

    [TestMethod]
    public void EndpointType_ISOC()
    {
        var basic = new UsbIpHeaderBasic() { ep = 1 };
        // Bogus interval, number_of_packets has precedence.
        var submit = new UsbIpHeaderCmdSubmit() { number_of_packets = 42 };
        var type = basic.EndpointType(submit);
        Assert.AreEqual(UsbSupTransferType.USBSUP_TRANSFER_TYPE_ISOC, type);
    }

    [TestMethod]
    public void EndpointType_INTR()
    {
        var basic = new UsbIpHeaderBasic() { ep = 1 };
        var submit = new UsbIpHeaderCmdSubmit() { interval = 42 };
        var type = basic.EndpointType(submit);
        Assert.AreEqual(UsbSupTransferType.USBSUP_TRANSFER_TYPE_INTR, type);
    }

    [TestMethod]
    public void EndpointType_BULK()
    {
        var basic = new UsbIpHeaderBasic() { ep = 1 };
        var submit = new UsbIpHeaderCmdSubmit();
        var type = basic.EndpointType(submit);
        Assert.AreEqual(UsbSupTransferType.USBSUP_TRANSFER_TYPE_BULK, type);
    }

    [TestMethod]
    [DataRow((ushort)0x0000, "0.0.0")]
    [DataRow((ushort)0x0001, "0.0.1")]
    [DataRow((ushort)0x0010, "0.1.0")]
    [DataRow((ushort)0x0100, "1.0.0")]
    [DataRow((ushort)0x1000, "16.0.0")]
    [DataRow((ushort)0x0123, "1.2.3")]
    [DataRow((ushort)0xffff, "255.15.15")]
    public void UsbIpVersionToVersion(ushort usbipVersion, string expected)
    {
        Assert.AreEqual(Version.Parse(expected), usbipVersion.UsbIpVersionToVersion());
        Assert.AreEqual(expected, usbipVersion.UsbIpVersionToVersion().ToString());
    }
}
