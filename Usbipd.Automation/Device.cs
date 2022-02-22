// SPDX-FileCopyrightText: 2022 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-2.0-only

using System.Net;
using System.Runtime.Serialization;

namespace Usbipd.Automation
{
    [DataContract]
    public sealed class Device
    {
        [DataMember]
        public string Name { get; init; } = string.Empty;

        /// <summary>
        /// Serialization for <see cref="IPAddress"/>.
        /// </summary>
        [DataMember(Name = nameof(IPAddress))]
        string? _IPAddress;

        [IgnoreDataMember]
        public IPAddress? IPAddress
        {
            get => IPAddress.TryParse(_IPAddress, out var ipAddress) ? ipAddress : null;
            init => _IPAddress = value?.ToString();
        }
    }
}
