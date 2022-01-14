// SPDX-FileCopyrightText: 2022 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-2.0-only

using System;
using System.ComponentModel;
using System.IO;
using System.Net;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using UsbIpServer;
using Windows.Win32;
using Windows.Win32.System.Ioctl;

namespace UnitTests
{
    [TestClass]
    sealed class DeviceFile_Tests
    {
        static readonly string TemporaryPath = Path.GetTempFileName();

        [ClassCleanup]
        public static void ClassCleanup()
        {
            File.Delete(TemporaryPath);
        }

        [TestMethod]
        public void Constructor_Success()
        {
            using var deviceFile = new DeviceFile(TemporaryPath);
        }

        [TestMethod]
        public void Constructor_FileNotFound()
        {
            Assert.ThrowsException<Win32Exception>(() =>
            {
                using var deviceFile = new DeviceFile(TemporaryPath + "_does_not_exist");
            });
        }

        [TestMethod]
        public void Dispose()
        {
            var deviceFile = new DeviceFile(TemporaryPath);
            deviceFile.Dispose();
            Assert.ThrowsException<ObjectDisposedException>(() =>
            {
                _ = deviceFile.DangerousGetHandle();
            });
        }

        [TestMethod]
        public void Dispose_Twice()
        {
            var deviceFile = new DeviceFile(TemporaryPath);
            deviceFile.Dispose();
            deviceFile.Dispose();
        }

        [TestMethod]
        public void DangerousGetHandle_Success()
        {
            using var deviceFile = new DeviceFile(TemporaryPath);
            _ = deviceFile.DangerousGetHandle();
        }

        enum TEST_IOCTL : uint
        {
            FSCTL_QUERY_ALLOCATED_RANGES = TestPInvoke.FSCTL_QUERY_ALLOCATED_RANGES,
        }

        [TestMethod]
        public void IoControlAsync_Success()
        {
            using var deviceFile = new DeviceFile(TemporaryPath);
            var rangeBuffer = new FILE_ALLOCATED_RANGE_BUFFER();
            var outputBuffer = Array.Empty<byte>();
            var result = deviceFile.IoControlAsync(TEST_IOCTL.FSCTL_QUERY_ALLOCATED_RANGES, Tools.StructToBytes(rangeBuffer), outputBuffer).Result;
            Assert.AreEqual(0u, result);
        }

        [TestMethod]
        public void IoControlAsync_null_Output()
        {
            using var deviceFile = new DeviceFile(TemporaryPath);
            var rangeBuffer = new FILE_ALLOCATED_RANGE_BUFFER();
            var result = deviceFile.IoControlAsync(TEST_IOCTL.FSCTL_QUERY_ALLOCATED_RANGES, Tools.StructToBytes(rangeBuffer), null).Result;
            Assert.AreEqual(0u, result);
        }

        [TestMethod]
        public void IoControlAsync_ShortOutput()
        {
            using var deviceFile = new DeviceFile(TemporaryPath);
            var rangeBuffer = new FILE_ALLOCATED_RANGE_BUFFER();
            var outputBuffer = new byte[1];
            var result = deviceFile.IoControlAsync(TEST_IOCTL.FSCTL_QUERY_ALLOCATED_RANGES, Tools.StructToBytes(rangeBuffer), outputBuffer, false).Result;
            Assert.AreEqual(0u, result);
        }

        [TestMethod]
        public void IoControlAsync_Win32Exception()
        {
            using var deviceFile = new DeviceFile(TemporaryPath);
            var exception = Assert.ThrowsException<AggregateException>(() =>
            {
                deviceFile.IoControlAsync(TEST_IOCTL.FSCTL_QUERY_ALLOCATED_RANGES, null, null).Wait();
            });
            Assert.IsInstanceOfType(exception.InnerException, typeof(Win32Exception));
        }

        [TestMethod]
        public void IoControlAsync_ProtocolViolation()
        {
            using var deviceFile = new DeviceFile(TemporaryPath);
            var rangeBuffer = new FILE_ALLOCATED_RANGE_BUFFER();
            var outputBuffer = new byte[1];
            var exception = Assert.ThrowsException<AggregateException>(() =>
            {
                deviceFile.IoControlAsync(TEST_IOCTL.FSCTL_QUERY_ALLOCATED_RANGES, Tools.StructToBytes(rangeBuffer), outputBuffer).Wait();
            });
            Assert.IsInstanceOfType(exception.InnerException, typeof(ProtocolViolationException));
        }
    }
}
