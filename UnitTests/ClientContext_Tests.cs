// SPDX-FileCopyrightText: 2022 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

using System.Net;

namespace UnitTests;

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
