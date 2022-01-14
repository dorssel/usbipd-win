// SPDX-FileCopyrightText: 2022 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-2.0-only

using System;
using System.IO;
using System.Net;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using UsbIpServer;

namespace UnitTests
{
    [TestClass]
    sealed class ClientContext_Tests
    {
        static readonly string TemporaryPath = Path.GetTempFileName();

        [ClassCleanup]
        public static void ClassCleanup()
        {
            File.Delete(TemporaryPath);
        }

        [TestMethod]
        public void DefaultConstructor()
        {
            using var clientContext = new ClientContext();
        }

        [TestMethod]
        public void Dispose()
        {
            var clientContext = new ClientContext();
            ((IDisposable)clientContext).Dispose();
            Assert.ThrowsException<ObjectDisposedException>(() =>
            {
                clientContext.TcpClient.Connect(IPAddress.Loopback, 1234);
            });
        }

        [TestMethod]
        public void DisposeTwice()
        {
            var clientContext = new ClientContext
            {
                AttachedDevice = new DeviceFile(TemporaryPath)
            };
            ((IDisposable)clientContext).Dispose();
            ((IDisposable)clientContext).Dispose();
        }
    }
}
