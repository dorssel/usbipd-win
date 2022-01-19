// SPDX-FileCopyrightText: 2022 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-2.0-only

using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using UsbIpServer;
using static UsbIpServer.Interop.Linux;

namespace UnitTests
{
    [TestClass]
    sealed class ExportedDevice_Tests
    {
        static readonly ExportedDevice TestDevice = new(
            InstanceId: @"SOME\Device\Path\01234567",
            BusId: BusId.Parse("3-42"),
            Speed: UsbDeviceSpeed.USB_SPEED_SUPER_PLUS,
            VendorId: 0x1234,
            ProductId: 0x9876,
            BcdDevice: 0x0405,
            DeviceClass: 0x23,
            DeviceSubClass: 0x45,
            DeviceProtocol: 0x67,
            ConfigurationValue: 3,
            NumConfigurations: 4,
            Interfaces: new() { (1, 2, 3), (4, 5, 6) }
        );

        /// <summary>
        /// See <seealso href="https://www.kernel.org/doc/html/latest/usb/usbip_protocol.html"/>.
        /// </summary>
        static readonly byte[] TestDeviceBytes = new byte[] {
            // path (256 bytes, text)
            (byte)'S', (byte)'O', (byte)'M', (byte)'E', (byte)'\\', (byte)'D', (byte)'e', (byte)'v',
            (byte)'i', (byte)'c', (byte)'e', (byte)'\\', (byte)'P', (byte)'a', (byte)'t', (byte)'h',
            (byte)'\\', (byte)'0', (byte)'1', (byte)'2', (byte)'3', (byte)'4', (byte)'5', (byte)'6',
            (byte)'7', 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            // busid (32 bytes, text)
            (byte)'3', (byte)'-', (byte)'4', (byte)'2', 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            // busnum (4 bytes, big endian)
            0, 0, 0, 3,
            // devnum (4 bytes, big endian)
            0, 0, 0, 42,
            // speed (4 bytes, enum, big endian)
            0, 0, 0, (byte)UsbDeviceSpeed.USB_SPEED_SUPER_PLUS,
            // idVendor (2 bytes, big endian)
            0x12, 0x34,
            // idProduct (2 bytes, big endian)
            0x98, 0x76,
            // bcdDevice (2 bytes, big endian)
            0x04, 0x05,
            // bDeviceClass (1 byte)
            0x23,
            // bDeviceSubClass (1 byte)
            0x45,
            // bDeviceProtocol (1 byte)
            0x67,
            // bConfigurationValue (1 byte)
            3,
            // bNumConfigurations (1 byte)
            4,
            // bNumInterfaces (1 byte)
            2,
        };

        /// <summary>
        /// See <seealso href="https://www.kernel.org/doc/html/latest/usb/usbip_protocol.html"/>.
        /// </summary>
        static readonly byte[] TestDeviceInterfacesBytes = new byte[] {
            // interface: 3 bytes + 1 padding byte
            1, 2, 3, 0,
            // interface: 3 bytes + 1 padding byte
            4, 5, 6, 0,
        };

        [TestMethod]
        public void Serialize_NoInterfaces()
        {
            using var stream = new MemoryStream();
            TestDevice.Serialize(stream, false);
            Assert.IsTrue(stream.ToArray().SequenceEqual(TestDeviceBytes));
        }

        [TestMethod]
        public void Serialize_WithInterfaces()
        {
            using var stream = new MemoryStream();
            TestDevice.Serialize(stream, true);
            Assert.IsTrue(stream.ToArray().SequenceEqual(TestDeviceBytes.Concat(TestDeviceInterfacesBytes)));
        }
    }
}
