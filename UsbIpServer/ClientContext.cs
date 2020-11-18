// SPDX-FileCopyrightText: 2020 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-2.0-only

using System;
using System.Diagnostics.CodeAnalysis;
using System.Net.Sockets;

namespace UsbIpServer
{
    [SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Instantiated by DI")]
    sealed class ClientContext : IDisposable
    {
        public TcpClient TcpClient { get; set; } = new TcpClient();
        public DeviceFile? AttachedDevice { get; set; }
        public UsbConfigurationDescriptors? ConfigurationDescriptors { get; set; }

        void IDisposable.Dispose()
        {
            TcpClient.Dispose();
            AttachedDevice?.Dispose();
        }
    }
}
