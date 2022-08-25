// SPDX-FileCopyrightText: 2022 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-2.0-only

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using static Usbipd.Interop.UsbIp;
using static Usbipd.Interop.VBoxUsb;

namespace UnitTests;

[TestClass]
sealed class PcapNg_Tests
{
    static readonly string TemporaryPath = Path.GetTempFileName();

    [ClassCleanup]
    public static void ClassCleanup()
    {
        File.Delete(TemporaryPath);
    }

    sealed class MockConfiguration : IDisposable
    {
        public MockConfiguration(string? path, uint? snapLength = null)
        {
            Mock.Setup(m => m["usbipd:PcapNg:Path"]).Returns(path!);
            if (path is not null)
            {
                Mock.Setup(m => m["usbipd:PcapNg:SnapLength"]).Returns(snapLength?.ToString()!);
            }
        }

        readonly Mock<IConfiguration> Mock = new(MockBehavior.Strict);
        public IConfiguration Object => Mock.Object;

        bool IsDisposed;
        public void Dispose()
        {
            if (!IsDisposed)
            {
                Mock.VerifyAll();
                IsDisposed = true;
            }
        }
    }

    static readonly ILogger<PcapNg> MockLogger = new Mock<ILogger<PcapNg>>(MockBehavior.Loose).Object;

    [TestMethod]
    public void Constructor_Disabled()
    {
        using var mockConfiguration = new MockConfiguration(null);
        {
            using var _ = new PcapNg(mockConfiguration.Object, MockLogger);
        }
    }

    [TestMethod]
    public void Constructor_InvalidPath()
    {
        using var mockConfiguration = new MockConfiguration(@"<>?*\InvalidPath");
        {
            using var _ = new PcapNg(mockConfiguration.Object, MockLogger);
        }
    }

    [TestMethod]
    public void Constructor_Create()
    {
        File.Delete(TemporaryPath);
        Assert.IsFalse(File.Exists(TemporaryPath));
        using var mockConfiguration = new MockConfiguration(TemporaryPath);
        {
            using var _ = new PcapNg(mockConfiguration.Object, MockLogger);
        }
        Assert.AreNotEqual(0, new FileInfo(TemporaryPath).Length);
    }

    [TestMethod]
    public void Constructor_Overwrite()
    {
        {
            using var file = File.Create(TemporaryPath);
            file.Write(new byte[1000]);
        }
        Assert.AreEqual(1000, new FileInfo(TemporaryPath).Length);
        using var mockConfiguration = new MockConfiguration(TemporaryPath);
        {
            using var _ = new PcapNg(mockConfiguration.Object, MockLogger);
        }
        Assert.AreNotEqual(0, new FileInfo(TemporaryPath).Length);
        Assert.IsTrue(new FileInfo(TemporaryPath).Length < 1000);
    }

    [TestMethod]
    public void Dispose()
    {
        using var mockConfiguration = new MockConfiguration(TemporaryPath);
        var pcapNg = new PcapNg(mockConfiguration.Object, MockLogger);
        pcapNg.Dispose();
    }

    [TestMethod]
    public void Dispose_Twice()
    {
        using var mockConfiguration = new MockConfiguration(TemporaryPath);
        var pcapNg = new PcapNg(mockConfiguration.Object, MockLogger);
        pcapNg.Dispose();
        pcapNg.Dispose();
    }

    sealed class TypeData
    {
        static readonly Dictionary<UsbSupTransferType, byte> _KnownGood = new()
        {
            { UsbSupTransferType.USBSUP_TRANSFER_TYPE_ISOC, 0 },
            { UsbSupTransferType.USBSUP_TRANSFER_TYPE_INTR, 1 },
            { UsbSupTransferType.USBSUP_TRANSFER_TYPE_MSG, 2 },
            { UsbSupTransferType.USBSUP_TRANSFER_TYPE_BULK, 3 },
        };

        static readonly List<UsbSupTransferType> _Invalid = new()
        {
            UsbSupTransferType.USBSUP_TRANSFER_TYPE_CTRL,
            (UsbSupTransferType)0xbaadf00d,
        };

        public static IEnumerable<object[]> KnownGood
        {
            get => from value in _KnownGood select new object[] { value.Key, value.Value };
        }

        public static IEnumerable<object[]> Invalid
        {
            get => from value in _Invalid select new object[] { value };
        }
    }


    [TestMethod]
    [DynamicData(nameof(TypeData.KnownGood), typeof(TypeData))]
    public void ConvertType_Success(UsbSupTransferType from, byte to)
    {
        Assert.AreEqual(to, PcapNg.ConvertType(from));
    }

    [TestMethod]
    [DynamicData(nameof(TypeData.Invalid), typeof(TypeData))]
    public void ConvertType_Invalid(UsbSupTransferType from)
    {
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => PcapNg.ConvertType(from));
    }

    [TestMethod]
    public void DumpPacketNonIsoRequest_Disabled()
    {
        using var mockConfiguration = new MockConfiguration(null);
        using var pcapNg = new PcapNg(mockConfiguration.Object, MockLogger);
        pcapNg.DumpPacketNonIsoRequest(new(), new(), null);
    }

    [TestMethod]
    public void DumpPacketNonIsoRequest_WithData()
    {
        using var mockConfiguration = new MockConfiguration(TemporaryPath);
        using var pcapNg = new PcapNg(mockConfiguration.Object, MockLogger);
        pcapNg.DumpPacketNonIsoRequest(new(), new(), new byte[10]);
    }

    [TestMethod]
    public void DumpPacketNonIsoRequest_NoData()
    {
        using var mockConfiguration = new MockConfiguration(TemporaryPath);
        using var pcapNg = new PcapNg(mockConfiguration.Object, MockLogger);
        pcapNg.DumpPacketNonIsoRequest(new(), new(), null);
    }

    [TestMethod]
    public void DumpPacketNonIsoRequest_NoSetup()
    {
        using var mockConfiguration = new MockConfiguration(TemporaryPath);
        using var pcapNg = new PcapNg(mockConfiguration.Object, MockLogger);
        pcapNg.DumpPacketNonIsoRequest(new() { ep = 1 }, new(), null);
    }

    [TestMethod]
    public void DumpPacketNonIsoReply_Disabled()
    {
        using var mockConfiguration = new MockConfiguration(null);
        using var pcapNg = new PcapNg(mockConfiguration.Object, MockLogger);
        pcapNg.DumpPacketNonIsoReply(new(), new(), new(), null);
    }

    [TestMethod]
    public void DumpPacketNonIsoReply_WithData()
    {
        using var mockConfiguration = new MockConfiguration(TemporaryPath);
        using var pcapNg = new PcapNg(mockConfiguration.Object, MockLogger);
        pcapNg.DumpPacketNonIsoReply(new(), new(), new(), new byte[10]);
    }

    [TestMethod]
    public void DumpPacketNonIsoReply_NoData()
    {
        using var mockConfiguration = new MockConfiguration(TemporaryPath);
        using var pcapNg = new PcapNg(mockConfiguration.Object, MockLogger);
        pcapNg.DumpPacketNonIsoReply(new(), new(), new(), null);
    }

    [TestMethod]
    public void DumpPacketIsoRequest_Disabled()
    {
        using var mockConfiguration = new MockConfiguration(null);
        using var pcapNg = new PcapNg(mockConfiguration.Object, MockLogger);
        pcapNg.DumpPacketIsoRequest(new(), new(), Array.Empty<UsbIpIsoPacketDescriptor>(), null);
    }

    [TestMethod]
    public void DumpPacketIsoRequest_WithData()
    {
        using var mockConfiguration = new MockConfiguration(TemporaryPath);
        using var pcapNg = new PcapNg(mockConfiguration.Object, MockLogger);
        pcapNg.DumpPacketIsoRequest(new(), new(), new UsbIpIsoPacketDescriptor[2], new byte[10]);
    }

    [TestMethod]
    public void DumpPacketIsoRequest_NoData()
    {
        using var mockConfiguration = new MockConfiguration(TemporaryPath);
        using var pcapNg = new PcapNg(mockConfiguration.Object, MockLogger);
        pcapNg.DumpPacketIsoRequest(new(), new(), Array.Empty<UsbIpIsoPacketDescriptor>(), null);
    }

    [TestMethod]
    public void DumpPacketIsoReply_Disabled()
    {
        using var mockConfiguration = new MockConfiguration(null);
        using var pcapNg = new PcapNg(mockConfiguration.Object, MockLogger);
        pcapNg.DumpPacketIsoReply(new(), new(), new(), Array.Empty<UsbIpIsoPacketDescriptor>(), null);
    }

    [TestMethod]
    public void DumpPacketIsoReply_WithData()
    {
        using var mockConfiguration = new MockConfiguration(TemporaryPath);
        using var pcapNg = new PcapNg(mockConfiguration.Object, MockLogger);
        pcapNg.DumpPacketIsoReply(new(), new(), new(), new UsbIpIsoPacketDescriptor[2], new byte[10]);
    }

    [TestMethod]
    public void DumpPacketIsoReply_NoData()
    {
        using var mockConfiguration = new MockConfiguration(TemporaryPath);
        using var pcapNg = new PcapNg(mockConfiguration.Object, MockLogger);
        pcapNg.DumpPacketIsoReply(new(), new(), new(), Array.Empty<UsbIpIsoPacketDescriptor>(), null);
    }

    [TestMethod]
    public void SnapLength_Normal()
    {
        using var mockConfiguration = new MockConfiguration(TemporaryPath, 1024);
        using var pcapNg = new PcapNg(mockConfiguration.Object, MockLogger);
    }

    [TestMethod]
    public void SnapLength_ClipMinimum()
    {
        using var mockConfiguration = new MockConfiguration(TemporaryPath, 1);
        using var pcapNg = new PcapNg(mockConfiguration.Object, MockLogger);
    }

    [TestMethod]
    public void SnapLength_ClipMaximum()
    {
        using var mockConfiguration = new MockConfiguration(TemporaryPath, uint.MaxValue);
        using var pcapNg = new PcapNg(mockConfiguration.Object, MockLogger);
    }

    [TestMethod]
    public void PacketWriterAsync_Flush()
    {
        using var mockConfiguration = new MockConfiguration(TemporaryPath);
        using var pcapNg = new PcapNg(mockConfiguration.Object, MockLogger);
        pcapNg.DumpPacketNonIsoRequest(new(), new(), null);
        Thread.Sleep(TimeSpan.FromSeconds(6));
    }
}
