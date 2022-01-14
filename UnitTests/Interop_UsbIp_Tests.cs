// SPDX-FileCopyrightText: 2022 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-2.0-only

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using UsbIpServer.Interop;
using static UsbIpServer.Interop.UsbIp;

namespace UnitTests
{
    [TestClass]
    sealed class Interop_UsbIp_Tests
    {
        static readonly byte[] TestUsbIpHeaderBytes =
        {
            0x01, 0x02, 0x03, 0x04, // basic.command
            0x11, 0x12, 0x13, 0x14, // basic.seqnum
            0x21, 0x22, 0x23, 0x24, // basic.devid
            0x31, 0x32, 0x33, 0x34, // basic.direction
            0x41, 0x42, 0x43, 0x44, // basic.ep

            0x51, 0x52, 0x53, 0x54, // cmd_submit.transfer_flags
            0x61, 0x62, 0x63, 0x64, // cmd_submit.transfer_buffer_length
            0x71, 0x72, 0x73, 0x74, // cmd_submit.start_frame
            0x81, 0x82, 0x83, 0x84, // cmd_submit.number_of_packets
            0x91, 0x92, 0x93, 0x94, // cmd_submit.interval

            0xa1, // cmd_submit.setup.bmRequestType
            0xa2, // cmd_submit.setup.bRequest
            0xa3, 0xa4, // cmd_submit.setup.wValue
            0xa5, 0xa6, // cmd_submit.setup.wIndex
            0xa7, 0xa8, // cmd_submit.setup.wLength
        };

        static readonly UsbIpHeader TestUsbIpHeader = new()
        {
            basic =
            {
                command = (UsbIpCmd)0x01020304,
                seqnum = 0x11121314,
                devid = 0x21222324,
                direction = (UsbIpDir)0x31323334,
                ep = 0x41424344,
            },
            cmd_submit =
            {
                transfer_flags = 0x51525354,
                transfer_buffer_length = 0x61626364,
                start_frame = 0x71727374,
                number_of_packets = unchecked((int)0x81828384),
                interval = unchecked((int)0x91929394),
                setup =
                {
                    bmRequestType = { B = 0xa1 },
                    bRequest = 0xa2,
                    // NOTE: the following are *not* automatically put in little endian order
                    wValue = { W = 0xa4a3 },
                    wIndex = { W = 0xa6a5 },
                    wLength = 0xa8a7,
                }
            },
        };

        [TestMethod]
        public async Task ReadUsbIpHeaderAsync_Success()
        {
            using var memoryStream = new MemoryStream(TestUsbIpHeaderBytes);
            var usbIpHeader = await memoryStream.ReadUsbIpHeaderAsync(CancellationToken.None);
            Assert.AreEqual(TestUsbIpHeader.basic.command, usbIpHeader.basic.command);
            Assert.AreEqual(TestUsbIpHeader.basic.seqnum, usbIpHeader.basic.seqnum);
            Assert.AreEqual(TestUsbIpHeader.basic.devid, usbIpHeader.basic.devid);
            Assert.AreEqual(TestUsbIpHeader.basic.direction, usbIpHeader.basic.direction);
            Assert.AreEqual(TestUsbIpHeader.basic.ep, usbIpHeader.basic.ep);
            Assert.AreEqual(TestUsbIpHeader.cmd_submit.transfer_flags, usbIpHeader.cmd_submit.transfer_flags);
            Assert.AreEqual(TestUsbIpHeader.cmd_submit.transfer_buffer_length, usbIpHeader.cmd_submit.transfer_buffer_length);
            Assert.AreEqual(TestUsbIpHeader.cmd_submit.start_frame, usbIpHeader.cmd_submit.start_frame);
            Assert.AreEqual(TestUsbIpHeader.cmd_submit.number_of_packets, usbIpHeader.cmd_submit.number_of_packets);
            Assert.AreEqual(TestUsbIpHeader.cmd_submit.interval, usbIpHeader.cmd_submit.interval);
            Assert.AreEqual(TestUsbIpHeader.cmd_submit.setup.bmRequestType, usbIpHeader.cmd_submit.setup.bmRequestType);
            Assert.AreEqual(TestUsbIpHeader.cmd_submit.setup.bRequest, usbIpHeader.cmd_submit.setup.bRequest);
            Assert.AreEqual(TestUsbIpHeader.cmd_submit.setup.wValue, usbIpHeader.cmd_submit.setup.wValue);
            Assert.AreEqual(TestUsbIpHeader.cmd_submit.setup.wIndex, usbIpHeader.cmd_submit.setup.wIndex);
            Assert.AreEqual(TestUsbIpHeader.cmd_submit.setup.wLength, usbIpHeader.cmd_submit.setup.wLength);
        }

        [TestMethod]
        public void ReadUsbIpHeaderAsync_Short()
        {
            using var memoryStream = new MemoryStream(TestUsbIpHeaderBytes[0..^1]);
            var exception = Assert.ThrowsException<AggregateException>(() =>
            {
                memoryStream.ReadUsbIpHeaderAsync(CancellationToken.None).Wait();
            });
            Assert.IsInstanceOfType(exception.InnerException, typeof(EndOfStreamException));
        }

        [TestMethod]
        public void UsbIpHeader_ToBytes_Success()
        {
            var bytes = TestUsbIpHeader.ToBytes();
            Assert.IsTrue(bytes.SequenceEqual(TestUsbIpHeaderBytes));
        }

        static readonly byte[] TestUsbIpIsoPacketDescriptorBytes =
        {
            0x01, 0x02, 0x03, 0x04, // offset
            0x11, 0x12, 0x13, 0x14, // length
            0x21, 0x22, 0x23, 0x24, // actual_length
            0x31, 0x32, 0x33, 0x34, // status
        };

        static readonly UsbIpIsoPacketDescriptor TestUsbIpIsoPacketDescriptor = new()
        {
            offset = 0x01020304,
            length = 0x11121314,
            actual_length = 0x21222324,
            status = 0x31323334,
        };

        [TestMethod]
        public async Task ReadUsbIpIsoPacketDescriptorsAsync_Success()
        {
            using var memoryStream = new MemoryStream(TestUsbIpIsoPacketDescriptorBytes);
            var usbIpIsoPacketDescriptors = await memoryStream.ReadUsbIpIsoPacketDescriptorsAsync(1, CancellationToken.None);
            Assert.AreEqual(1, usbIpIsoPacketDescriptors.Length);
            Assert.AreEqual(TestUsbIpIsoPacketDescriptor.offset, usbIpIsoPacketDescriptors[0].offset);
            Assert.AreEqual(TestUsbIpIsoPacketDescriptor.length, usbIpIsoPacketDescriptors[0].length);
            Assert.AreEqual(TestUsbIpIsoPacketDescriptor.actual_length, usbIpIsoPacketDescriptors[0].actual_length);
            Assert.AreEqual(TestUsbIpIsoPacketDescriptor.status, usbIpIsoPacketDescriptors[0].status);
        }

        [TestMethod]
        public void ReadUsbIpIsoPacketDescriptorsAsync_Short()
        {
            using var memoryStream = new MemoryStream(TestUsbIpIsoPacketDescriptorBytes[0..^1]);
            var exception = Assert.ThrowsException<AggregateException>(() =>
            {
                memoryStream.ReadUsbIpIsoPacketDescriptorsAsync(1, CancellationToken.None).Wait();
            });
            Assert.IsInstanceOfType(exception.InnerException, typeof(EndOfStreamException));
        }

        [TestMethod]
        public async Task ReadUsbIpIsoPacketDescriptorsAsync_Success_Multiple()
        {
            using var memoryStream = new MemoryStream(
                new byte[TestUsbIpIsoPacketDescriptorBytes.Length].Concat(TestUsbIpIsoPacketDescriptorBytes).ToArray());
            var usbIpIsoPacketDescriptors = await memoryStream.ReadUsbIpIsoPacketDescriptorsAsync(2, CancellationToken.None);
            Assert.AreEqual(2, usbIpIsoPacketDescriptors.Length);
            Assert.AreEqual(0u, usbIpIsoPacketDescriptors[0].offset);
            Assert.AreEqual(0u, usbIpIsoPacketDescriptors[0].length);
            Assert.AreEqual(0u, usbIpIsoPacketDescriptors[0].actual_length);
            Assert.AreEqual(0u, usbIpIsoPacketDescriptors[0].status);
            Assert.AreEqual(TestUsbIpIsoPacketDescriptor.offset, usbIpIsoPacketDescriptors[1].offset);
            Assert.AreEqual(TestUsbIpIsoPacketDescriptor.length, usbIpIsoPacketDescriptors[1].length);
            Assert.AreEqual(TestUsbIpIsoPacketDescriptor.actual_length, usbIpIsoPacketDescriptors[1].actual_length);
            Assert.AreEqual(TestUsbIpIsoPacketDescriptor.status, usbIpIsoPacketDescriptors[1].status);
        }

        [TestMethod]
        public void ReadUsbIpIsoPacketDescriptorsAsync_Short_Multiple()
        {
            using var memoryStream = new MemoryStream(
                new byte[TestUsbIpIsoPacketDescriptorBytes.Length].Concat(TestUsbIpIsoPacketDescriptorBytes[0..^1]).ToArray());
            var exception = Assert.ThrowsException<AggregateException>(() =>
            {
                memoryStream.ReadUsbIpIsoPacketDescriptorsAsync(2, CancellationToken.None).Wait();
            });
            Assert.IsInstanceOfType(exception.InnerException, typeof(EndOfStreamException));
        }

        [TestMethod]
        public void UsbIpIsoPacketDescriptors_ToBytes_Success()
        {
            var usbIpIsoPacketDescriptors = new UsbIpIsoPacketDescriptor[]
            {
                new UsbIpIsoPacketDescriptor(),
                TestUsbIpIsoPacketDescriptor,
                new UsbIpIsoPacketDescriptor(),
            };
            var bytes = usbIpIsoPacketDescriptors.ToBytes();
            Assert.IsTrue(bytes.SequenceEqual(new byte[TestUsbIpIsoPacketDescriptorBytes.Length]
                .Concat(TestUsbIpIsoPacketDescriptorBytes).Concat(new byte[TestUsbIpIsoPacketDescriptorBytes.Length]).ToArray()));
        }

    }
}
