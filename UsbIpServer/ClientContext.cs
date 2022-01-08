// SPDX-FileCopyrightText: 2020 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-2.0-only

using System;
using System.Net;
using System.Net.Sockets;

namespace UsbIpServer
{
    sealed class ClientContext : IDisposable
    {
        public TcpClient TcpClient { get; set; } = new();
        /// <summary>
        /// Canonical remote client IP address (either IPv4 or IPv6).
        /// </summary>
        public IPAddress ClientAddress { get; set; } = IPAddress.Any;
        public DeviceFile? AttachedDevice { get; set; }

        void IDisposable.Dispose()
        {
            TcpClient.Dispose();
            AttachedDevice?.Dispose();
        }
    }
}
