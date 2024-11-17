// SPDX-FileCopyrightText: 2020 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

using System.Net;
using System.Net.Sockets;
using Usbipd.Automation;

namespace Usbipd;

sealed partial class ClientContext : IDisposable
{
    public TcpClient TcpClient { get; set; } = new();
    /// <summary>
    /// Canonical remote client IP address (either IPv4 or IPv6).
    /// </summary>
    public IPAddress ClientAddress { get; set; } = IPAddress.Any;
    public BusId? AttachedBusId { get; set; }
    public DeviceFile? AttachedDevice { get; set; }

    void IDisposable.Dispose()
    {
        TcpClient.Dispose();
        AttachedDevice?.Dispose();
    }
}
