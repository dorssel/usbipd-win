// SPDX-FileCopyrightText: 2022 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

using System.Net;
using System.Runtime.Serialization;
#if !NETSTANDARD
using System.Text.Json.Serialization;
#endif

namespace Usbipd.Automation;

[DataContract]
public sealed partial class Device
{
    public Device() { }

#if !NETSTANDARD
    [JsonConstructor]
    public Device(string instanceId, string description, bool isForced, BusId? busId, Guid? persistedGuid, string? stubInstanceId, IPAddress? clientIPAddress)
        => (InstanceId, Description, IsForced, BusId, PersistedGuid, StubInstanceId, ClientIPAddress)
        = (instanceId, description, isForced, busId, persistedGuid, stubInstanceId, clientIPAddress);
#endif

    [DataMember]
#if !NETSTANDARD
    [JsonPropertyOrder(4)]
#endif
    public string InstanceId { get; init; } = string.Empty;

#if !NETSTANDARD
    [JsonIgnore]
#endif
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
#if !NETSTANDARD
    [JsonPropertyOrder(3)]
#endif
    public string Description { get; init; } = string.Empty;

    [DataMember]
#if !NETSTANDARD
    [JsonPropertyOrder(5)]
#endif
    public bool IsForced { get; init; }

    /// <summary>
    /// Serialization for <see cref="BusId"/>.
    /// </summary>
    [DataMember(Name = nameof(BusId))]
    string? _BusId;

#if !NETSTANDARD
    [JsonPropertyOrder(1)]
#endif
    public BusId? BusId
    {
        get => Automation.BusId.TryParse(_BusId ?? string.Empty, out var busId) ? busId : null;
        init => _BusId = value?.ToString();
    }

    [DataMember]
#if !NETSTANDARD
    [JsonPropertyOrder(6)]
#endif
    public Guid? PersistedGuid { get; init; }

    [DataMember]
#if !NETSTANDARD
    [JsonPropertyOrder(7)]
#endif
    public string? StubInstanceId { get; init; }

    /// <summary>
    /// Serialization for <see cref="ClientIPAddress"/>.
    /// </summary>
    [DataMember(Name = nameof(ClientIPAddress))]
    string? _ClientIPAddress;

#if !NETSTANDARD
    [JsonPropertyOrder(2)]
    [JsonConverter(typeof(JsonConverterIPAddress))]
#endif
    public IPAddress? ClientIPAddress
    {
        get => IPAddress.TryParse(_ClientIPAddress, out var clientIPAddress) ? clientIPAddress : null;
        init => _ClientIPAddress = value?.ToString();
    }

#if !NETSTANDARD
    [JsonIgnore]
#endif
    public bool IsBound { get => PersistedGuid is not null; }

#if !NETSTANDARD
    [JsonIgnore]
#endif
    public bool IsConnected { get => BusId is not null; }

#if !NETSTANDARD
    [JsonIgnore]
#endif
    public bool IsAttached { get => ClientIPAddress is not null; }
}
