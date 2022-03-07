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
        [DataMember(Name = nameof(ClientIPAddress))]
        string? _ClientIPAddress;

        public IPAddress? ClientIPAddress
        {
            get => IPAddress.TryParse(_ClientIPAddress, out var clientIPAddress) ? clientIPAddress : null;
            init => _ClientIPAddress = value?.ToString();
        }

        [DataMember]
        public string? ClientWslInstance { get; init; }

        public bool IsBound { get => PersistedGuid is not null; }

        public bool IsConnected { get => BusId is not null; }

        public bool IsAttached { get => ClientIPAddress is not null; }

        public bool IsWslAttached { get => ClientWslInstance is not null; }
    }
}
