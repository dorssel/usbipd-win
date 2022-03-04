// SPDX-FileCopyrightText: 2022 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-2.0-only

using System;
using System.Net;
using System.Runtime.Serialization;

namespace Usbipd.Automation
{
    [DataContract]
    public sealed class Device
    {
        [DataMember]
        public string InstanceId { get; init; } = string.Empty;

        [DataMember]
        public string Description { get; init; } = string.Empty;

        [DataMember]
        public bool IsForced { get; init; }

        [DataMember]
        public string? BusId { get; init; }

        [DataMember]
        public Guid? PersistedGuid { get; init; }

        [DataMember]
        public string? StubInstanceId { get; init; }

        /// <summary>
        /// Serialization for <see cref="IPAddress"/>.
        /// </summary>
        [DataMember(Name = nameof(IPAddress))]
        string? _IPAddress;

        [IgnoreDataMember]
        public IPAddress? ClientIPAddress
        {
            get => IPAddress.TryParse(_IPAddress, out var ipAddress) ? ipAddress : null;
            init => _IPAddress = value?.ToString();
        }

        [DataMember]
        public string? ClientWslInstance { get; init; }

        [IgnoreDataMember]
        public bool IsBound { get => PersistedGuid is not null; }

        [IgnoreDataMember]
        public bool IsConnected { get => BusId is not null; }

        [IgnoreDataMember]
        public bool IsAttached { get => ClientIPAddress is not null; }

        [IgnoreDataMember]
        public bool IsWslAttached { get => ClientWslInstance is not null; }
    }
}
