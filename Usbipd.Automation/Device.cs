// SPDX-FileCopyrightText: 2022 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

using System;
using System.Net;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace Usbipd.Automation;

[DataContract]
public sealed partial class Device
{
    public Device() { }

    [JsonConstructor]
    public Device(string instanceId, string description, bool isForced, BusId? busId, Guid? persistedGuid, string? stubInstanceId,
        IPAddress? clientIPAddress, string? clientWslInstance)
        => (InstanceId, Description, IsForced, BusId, PersistedGuid, StubInstanceId, ClientIPAddress, ClientWslInstance)
        = (instanceId, description, isForced, busId, persistedGuid, stubInstanceId, clientIPAddress, clientWslInstance);

    [DataMember]
    [JsonPropertyOrder(5)]
    public string InstanceId { get; init; } = string.Empty;

    [JsonIgnore]
    public VidPid HardwareId
    {
        get
        {
            try
            {
                return VidPid.FromHardwareOrInstanceId(InstanceId);
            }
            catch (FormatException)
            {
                return new VidPid();
            }
        }
    }

    [DataMember]
    [JsonPropertyOrder(4)]
    public string Description { get; init; } = string.Empty;

    [DataMember]
    [JsonPropertyOrder(6)]
    public bool IsForced { get; init; }

    /// <summary>
    /// Serialization for <see cref="BusId"/>.
    /// </summary>
    [DataMember(Name = nameof(BusId))]
    string? _BusId;

    [JsonPropertyOrder(1)]
    [JsonConverter(typeof(NullableBusIdJsonConverter))]
    public BusId? BusId
    {
        get => Automation.BusId.TryParse(_BusId ?? string.Empty, out var busId) ? busId : null;
        init => _BusId = value?.ToString();
    }

    [DataMember]
    [JsonPropertyOrder(7)]
    public Guid? PersistedGuid { get; init; }

    [DataMember]
    [JsonPropertyOrder(8)]
    public string? StubInstanceId { get; init; }

    /// <summary>
    /// Serialization for <see cref="ClientIPAddress"/>.
    /// </summary>
    [DataMember(Name = nameof(ClientIPAddress))]
    string? _ClientIPAddress;

    [JsonPropertyOrder(2)]
    [JsonConverter(typeof(NullableIPAddressJsonConverter))]
    public IPAddress? ClientIPAddress
    {
        get => IPAddress.TryParse(_ClientIPAddress, out var clientIPAddress) ? clientIPAddress : null;
        init => _ClientIPAddress = value?.ToString();
    }

    [DataMember]
    [JsonPropertyOrder(3)]
    public string? ClientWslInstance { get; init; }

    [JsonIgnore]
    public bool IsBound { get => PersistedGuid is not null; }

    [JsonIgnore]
    public bool IsConnected { get => BusId is not null; }

    [JsonIgnore]
    public bool IsAttached { get => ClientIPAddress is not null; }

    [JsonIgnore]
    public bool IsWslAttached { get => ClientWslInstance is not null; }
}
