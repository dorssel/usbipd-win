/*
    usbipd-win
    Copyright (C) 2020  Frans van Dorsselaer

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program.  If not, see <https://www.gnu.org/licenses/>.
*/

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
