// SPDX-FileCopyrightText: 2022 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

using System.Net;

namespace UnitTests;

[TestClass]
sealed class ClientContext_Tests
{
    [TestMethod]
    public void DefaultConstructor()
    {
        using var clientContext = new ClientContext();
    }

    [TestMethod]
    public void ClientAddress()
    {
        using var clientContext = new ClientContext();

        Assert.AreEqual(IPAddress.Any, clientContext.ClientAddress);
        clientContext.ClientAddress = IPAddress.Loopback;
        Assert.AreEqual(IPAddress.Loopback, clientContext.ClientAddress);
        clientContext.ClientAddress = IPAddress.Any;
        Assert.AreEqual(IPAddress.Any, clientContext.ClientAddress);
    }

    [TestMethod]
    public void AttachedBusId()
    {
        using var clientContext = new ClientContext();

        Assert.IsNull(clientContext.AttachedBusId);
        clientContext.AttachedBusId = new(1, 42);
        Assert.IsNotNull(clientContext.AttachedBusId);
        clientContext.AttachedBusId = null;
        Assert.IsNull(clientContext.AttachedBusId);
    }

    [TestMethod]
    public void Dispose()
    {
        var clientContext = new ClientContext();
        ((IDisposable)clientContext).Dispose();
        Assert.ThrowsExactly<ObjectDisposedException>(() =>
        {
            clientContext.TcpClient.Connect(IPAddress.Loopback, 1234);
        });
    }

    [TestMethod]
    public void DisposeTwice()
    {
        using var temporaryFile = new TemporaryFile(true);
        var clientContext = new ClientContext
        {
            AttachedDevice = new DeviceFile(temporaryFile.AbsolutePath)
        };
        ((IDisposable)clientContext).Dispose();
        ((IDisposable)clientContext).Dispose();
    }
}
