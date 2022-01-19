// SPDX-FileCopyrightText: 2022 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-2.0-only

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using UsbIpServer;
using Windows.Win32.Devices.Usb;
using static UsbIpServer.Interop.Linux;
using static UsbIpServer.Interop.VBoxUsb;
using static UsbIpServer.Tools;

namespace UnitTests
{
    [TestClass]
    sealed class Tools_Tests
    {
        static readonly byte[] TestStreamBytes =
        {
            0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08,
        };

        [TestMethod]
        public void ReadExactlyAsync_Success()
        {
            using var memoryStream = new MemoryStream(TestStreamBytes);
            var buf = new byte[TestStreamBytes.Length - 1];
            memoryStream.ReadExactlyAsync(buf, CancellationToken.None).Wait();
            Assert.AreEqual(TestStreamBytes.Length - 1, memoryStream.Position);
            Assert.IsTrue(buf.SequenceEqual(TestStreamBytes[0..^1]));
        }

        [TestMethod]
        public void ReadExactlyAsync_EndOfStream()
        {
            using var memoryStream = new MemoryStream(TestStreamBytes);
            var buf = new byte[TestStreamBytes.Length + 1];
            var exception = Assert.ThrowsException<AggregateException>(() =>
            {
                memoryStream.ReadExactlyAsync(buf, CancellationToken.None).Wait();
            });
            Assert.IsInstanceOfType(exception.InnerException, typeof(EndOfStreamException));
        }

        [TestMethod]
        public void ReadExactlyAsync_Parts()
        {
            var pipe = new Pipe();
            using var readStream = pipe.Reader.AsStream();
            using var writeStream = pipe.Writer.AsStream();

            var buf = new byte[TestStreamBytes.Length - 1];
            writeStream.Write(TestStreamBytes.AsSpan(0, 1));
            var task = readStream.ReadExactlyAsync(buf, CancellationToken.None);
            task.Wait(100);
            Assert.AreEqual(TaskStatus.WaitingForActivation, task.Status);
            writeStream.Write(TestStreamBytes.AsSpan(1));
            task.Wait();
            Assert.IsTrue(buf.SequenceEqual(TestStreamBytes[0..^1]));
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

        static readonly byte[] TestStructBytes = new byte[]
        {
            0x04, 0x03, 0x02, 0x01, // i
            0x01, 0x00, 0x00, 0x00, // b
            0x18, 0x17, 0x16, 0x015, 0x14, 0x13, 0x12, 0x11, // l
        };

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
            Assert.ThrowsException<ArgumentException>(() =>
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
            Assert.ThrowsException<ArgumentException>(() =>
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
            Assert.ThrowsException<ArgumentException>(() =>
            {
                _ = BytesToStruct<TestStructType>(TestStructBytes.AsSpan()[0..^1]);
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

            public static IEnumerable<object[]> KnownGood
            {
                get => from value in _KnownGood select new object[] { value.Key, value.Value };
            }
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

            public static IEnumerable<object[]> KnownGood
            {
                get => from value in _KnownGood select new object[] { value.Key, value.Value };
            }
        }

        [TestMethod]
        [DynamicData(nameof(ErrorData.KnownGood), typeof(ErrorData))]
        public void ConvertError_Test(UsbSupError vbox, Errno linux)
        {
            Assert.AreEqual(linux, ConvertError(vbox));
        }
    }
}
