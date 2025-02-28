// SPDX-FileCopyrightText: 2022 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using static Usbipd.Interop.UsbIp;
using static Usbipd.Interop.VBoxUsb;

namespace UnitTests;

[TestClass]
sealed partial class PcapNg_Tests
{
    sealed partial class MockConfiguration : IDisposable
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
        using var temporaryFile = new TemporaryFile();
        using var mockConfiguration = new MockConfiguration(temporaryFile.AbsolutePath);
        {
            using var _ = new PcapNg(mockConfiguration.Object, MockLogger);
        }
        Assert.AreNotEqual(0, new FileInfo(temporaryFile.AbsolutePath).Length);
    }

    [TestMethod]
    public void Constructor_Overwrite()
    {
        using var temporaryFile = new TemporaryFile();
        {
            using var file = File.Create(temporaryFile.AbsolutePath);
            file.Write(new byte[1000]);
        }
        Assert.AreEqual(1000, new FileInfo(temporaryFile.AbsolutePath).Length);
        using var mockConfiguration = new MockConfiguration(temporaryFile.AbsolutePath);
        {
            using var _ = new PcapNg(mockConfiguration.Object, MockLogger);
        }
        Assert.AreNotEqual(0, new FileInfo(temporaryFile.AbsolutePath).Length);
        Assert.IsTrue(new FileInfo(temporaryFile.AbsolutePath).Length < 1000);
    }

    [TestMethod]
    public void Dispose()
    {
        using var temporaryFile = new TemporaryFile();
        using var mockConfiguration = new MockConfiguration(temporaryFile.AbsolutePath);
        var pcapNg = new PcapNg(mockConfiguration.Object, MockLogger);
        pcapNg.Dispose();
    }

    [TestMethod]
    public void Dispose_Twice()
    {
        using var temporaryFile = new TemporaryFile();
        using var mockConfiguration = new MockConfiguration(temporaryFile.AbsolutePath);
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

        static readonly List<UsbSupTransferType> _Invalid =
        [
            UsbSupTransferType.USBSUP_TRANSFER_TYPE_CTRL,
            (UsbSupTransferType)0xbaadf00d,
        ];

        public static IEnumerable<object[]> KnownGood => from value in _KnownGood select new object[] { value.Key, value.Value };

        public static IEnumerable<object[]> Invalid => from value in _Invalid select new object[] { value };
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
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => PcapNg.ConvertType(from));
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
        using var temporaryFile = new TemporaryFile();
        using var mockConfiguration = new MockConfiguration(temporaryFile.AbsolutePath);
        using var pcapNg = new PcapNg(mockConfiguration.Object, MockLogger);
        pcapNg.DumpPacketNonIsoRequest(new(), new(), new byte[10]);
    }

    [TestMethod]
    public void DumpPacketNonIsoRequest_NoData()
    {
        using var temporaryFile = new TemporaryFile();
        using var mockConfiguration = new MockConfiguration(temporaryFile.AbsolutePath);
        using var pcapNg = new PcapNg(mockConfiguration.Object, MockLogger);
        pcapNg.DumpPacketNonIsoRequest(new(), new(), null);
    }

    [TestMethod]
    public void DumpPacketNonIsoRequest_NoSetup()
    {
        using var temporaryFile = new TemporaryFile();
        using var mockConfiguration = new MockConfiguration(temporaryFile.AbsolutePath);
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
        using var temporaryFile = new TemporaryFile();
        using var mockConfiguration = new MockConfiguration(temporaryFile.AbsolutePath);
        using var pcapNg = new PcapNg(mockConfiguration.Object, MockLogger);
        pcapNg.DumpPacketNonIsoReply(new(), new(), new(), new byte[10]);
    }

    [TestMethod]
    public void DumpPacketNonIsoReply_NoData()
    {
        using var temporaryFile = new TemporaryFile();
        using var mockConfiguration = new MockConfiguration(temporaryFile.AbsolutePath);
        using var pcapNg = new PcapNg(mockConfiguration.Object, MockLogger);
        pcapNg.DumpPacketNonIsoReply(new(), new(), new(), null);
    }

    [TestMethod]
    public void DumpPacketIsoRequest_Disabled()
    {
        using var mockConfiguration = new MockConfiguration(null);
        using var pcapNg = new PcapNg(mockConfiguration.Object, MockLogger);
        pcapNg.DumpPacketIsoRequest(new(), new(), [], null);
    }

    [TestMethod]
    public void DumpPacketIsoRequest_WithData()
    {
        using var temporaryFile = new TemporaryFile();
        using var mockConfiguration = new MockConfiguration(temporaryFile.AbsolutePath);
        using var pcapNg = new PcapNg(mockConfiguration.Object, MockLogger);
        pcapNg.DumpPacketIsoRequest(new(), new(), new UsbIpIsoPacketDescriptor[2], new byte[10]);
    }

    [TestMethod]
    public void DumpPacketIsoRequest_NoData()
    {
        using var temporaryFile = new TemporaryFile();
        using var mockConfiguration = new MockConfiguration(temporaryFile.AbsolutePath);
        using var pcapNg = new PcapNg(mockConfiguration.Object, MockLogger);
        pcapNg.DumpPacketIsoRequest(new(), new(), [], null);
    }

    [TestMethod]
    public void DumpPacketIsoReply_Disabled()
    {
        using var mockConfiguration = new MockConfiguration(null);
        using var pcapNg = new PcapNg(mockConfiguration.Object, MockLogger);
        pcapNg.DumpPacketIsoReply(new(), new(), new(), [], null);
    }

    [TestMethod]
    public void DumpPacketIsoReply_WithData()
    {
        using var temporaryFile = new TemporaryFile();
        using var mockConfiguration = new MockConfiguration(temporaryFile.AbsolutePath);
        using var pcapNg = new PcapNg(mockConfiguration.Object, MockLogger);
        pcapNg.DumpPacketIsoReply(new(), new(), new(), new UsbIpIsoPacketDescriptor[2], new byte[10]);
    }

    [TestMethod]
    public void DumpPacketIsoReply_NoData()
    {
        using var temporaryFile = new TemporaryFile();
        using var mockConfiguration = new MockConfiguration(temporaryFile.AbsolutePath);
        using var pcapNg = new PcapNg(mockConfiguration.Object, MockLogger);
        pcapNg.DumpPacketIsoReply(new(), new(), new(), [], null);
    }

    [TestMethod]
    public void DumpPacketUnlink_Disabled()
    {
        using var mockConfiguration = new MockConfiguration(null);
        using var pcapNg = new PcapNg(mockConfiguration.Object, MockLogger);
        pcapNg.DumpPacketUnlink(new(), false, new());
    }

    [TestMethod]
    public void DumpPacketUnlink_Reply_Disabled()
    {
        using var mockConfiguration = new MockConfiguration(null);
        using var pcapNg = new PcapNg(mockConfiguration.Object, MockLogger);
        pcapNg.DumpPacketUnlink(new(), true, new());
    }

    [TestMethod]
    public void DumpPacketUnlink()
    {
        using var temporaryFile = new TemporaryFile();
        using var mockConfiguration = new MockConfiguration(temporaryFile.AbsolutePath);
        using var pcapNg = new PcapNg(mockConfiguration.Object, MockLogger);
        pcapNg.DumpPacketUnlink(new(), false, new());
    }

    [TestMethod]
    public void DumpPacketUnlink_Reply()
    {
        using var temporaryFile = new TemporaryFile();
        using var mockConfiguration = new MockConfiguration(temporaryFile.AbsolutePath);
        using var pcapNg = new PcapNg(mockConfiguration.Object, MockLogger);
        pcapNg.DumpPacketUnlink(new(), true, new());
    }

    [TestMethod]
    public void SnapLength_Normal()
    {
        using var temporaryFile = new TemporaryFile();
        using var mockConfiguration = new MockConfiguration(temporaryFile.AbsolutePath, 1024);
        using var pcapNg = new PcapNg(mockConfiguration.Object, MockLogger);
    }

    [TestMethod]
    public void SnapLength_ClipMinimum()
    {
        using var temporaryFile = new TemporaryFile();
        using var mockConfiguration = new MockConfiguration(temporaryFile.AbsolutePath, 1);
        using var pcapNg = new PcapNg(mockConfiguration.Object, MockLogger);
    }

    [TestMethod]
    public void SnapLength_ClipMaximum()
    {
        using var temporaryFile = new TemporaryFile();
        using var mockConfiguration = new MockConfiguration(temporaryFile.AbsolutePath, uint.MaxValue);
        using var pcapNg = new PcapNg(mockConfiguration.Object, MockLogger);
    }

    [TestMethod]
    public void PacketWriterAsync_Flush()
    {
        using var temporaryFile = new TemporaryFile();
        using var mockConfiguration = new MockConfiguration(temporaryFile.AbsolutePath);
        using var pcapNg = new PcapNg(mockConfiguration.Object, MockLogger);
        pcapNg.DumpPacketNonIsoRequest(new(), new(), null);
        Thread.Sleep(TimeSpan.FromSeconds(6));
    }
}
