// SPDX-FileCopyrightText: 2022 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-2.0-only

using System;
using System.IO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using UsbIpServer;
using static UsbIpServer.Interop.UsbIp;

namespace UnitTests
{
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
            public MockConfiguration(string? path)
            {
                Mock.Setup(m => m["usbipd:PcapNg:Path"]).Returns(path!);
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

        [TestMethod]
        public void DumpPacket_Submit_Disabled()
        {
            using var mockConfiguration = new MockConfiguration(null);
            using var pcapNg = new PcapNg(mockConfiguration.Object, MockLogger);
            pcapNg.DumpPacket(new UsbIpHeaderBasic(), new UsbIpHeaderCmdSubmit(), new byte[10]);
        }

        [TestMethod]
        public void DumpPacket_Submit_NoData_Disabled()
        {
            using var mockConfiguration = new MockConfiguration(null);
            using var pcapNg = new PcapNg(mockConfiguration.Object, MockLogger);
            pcapNg.DumpPacket(new UsbIpHeaderBasic(), new UsbIpHeaderCmdSubmit(), null);
        }

        [TestMethod]
        public void DumpPacket_Reply_Disabled()
        {
            using var mockConfiguration = new MockConfiguration(null);
            using var pcapNg = new PcapNg(mockConfiguration.Object, MockLogger);
            pcapNg.DumpPacket(new UsbIpHeaderBasic(), new UsbIpHeaderCmdSubmit(), new UsbIpHeaderRetSubmit(), new byte[10]);
        }

        [TestMethod]
        public void DumpPacket_Reply_NoData_Disabled()
        {
            using var mockConfiguration = new MockConfiguration(null);
            using var pcapNg = new PcapNg(mockConfiguration.Object, MockLogger);
            pcapNg.DumpPacket(new UsbIpHeaderBasic(), new UsbIpHeaderCmdSubmit(), new UsbIpHeaderRetSubmit(), null);
        }

        [TestMethod]
        public void DumpPacket_Submit()
        {
            using var mockConfiguration = new MockConfiguration(TemporaryPath);
            using var pcapNg = new PcapNg(mockConfiguration.Object, MockLogger);
            pcapNg.DumpPacket(new UsbIpHeaderBasic(), new UsbIpHeaderCmdSubmit(), new byte[10]);
        }

        [TestMethod]
        public void DumpPacket_Submit_NoData()
        {
            using var mockConfiguration = new MockConfiguration(TemporaryPath);
            using var pcapNg = new PcapNg(mockConfiguration.Object, MockLogger);
            pcapNg.DumpPacket(new UsbIpHeaderBasic(), new UsbIpHeaderCmdSubmit(), null);
        }

        [TestMethod]
        public void DumpPacket_Reply()
        {
            using var mockConfiguration = new MockConfiguration(TemporaryPath);
            using var pcapNg = new PcapNg(mockConfiguration.Object, MockLogger);
            pcapNg.DumpPacket(new UsbIpHeaderBasic(), new UsbIpHeaderCmdSubmit(), new UsbIpHeaderRetSubmit(), new byte[10]);
        }

        [TestMethod]
        public void DumpPacket_Reply_NoData()
        {
            using var mockConfiguration = new MockConfiguration(TemporaryPath);
            using var pcapNg = new PcapNg(mockConfiguration.Object, MockLogger);
            pcapNg.DumpPacket(new UsbIpHeaderBasic(), new UsbIpHeaderCmdSubmit(), new UsbIpHeaderRetSubmit(), null);
        }
    }
}
