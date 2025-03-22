// SPDX-FileCopyrightText: 2022 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

using System.Net;
#if NETSTANDARD
using System.Runtime.Serialization;
#else
using System.Text.Json.Serialization;
#endif

namespace Usbipd.Automation;

#if NETSTANDARD
[DataContract]
public
#endif
sealed class Device
{
#if !NETSTANDARD
    public
#endif
    Device()
    { }

#if !NETSTANDARD
    [JsonConstructor]
    public Device(string instanceId, string description, bool isForced, BusId? busId, Guid? persistedGuid, string? stubInstanceId, IPAddress? clientIPAddress)
    {
        (InstanceId, Description, IsForced, BusId, PersistedGuid, StubInstanceId, ClientIPAddress)
            = (instanceId, description, isForced, busId, persistedGuid, stubInstanceId, clientIPAddress);
    }
#endif

#if NETSTANDARD
    [DataMember]
#else
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

#if NETSTANDARD
    [DataMember]
#else
    [JsonPropertyOrder(3)]
#endif
    public string Description { get; init; } = string.Empty;

#if NETSTANDARD
    [DataMember]
#else
    [JsonPropertyOrder(5)]
#endif
    public bool IsForced { get; init; }

    /// <summary>
    /// Serialization for <see cref="BusId"/>.
    /// </summary>
#if NETSTANDARD
    [DataMember(Name = nameof(BusId))]
#endif
    string? _BusId;

#if !NETSTANDARD
    [JsonPropertyOrder(1)]
#endif
    public BusId? BusId
    {
        get => Automation.BusId.TryParse(_BusId ?? string.Empty, out var busId) ? busId : null;
        init => _BusId = value?.ToString();
    }

#if NETSTANDARD
    [DataMember]
#else
    [JsonPropertyOrder(6)]
#endif
    public Guid? PersistedGuid { get; init; }

#if NETSTANDARD
    [DataMember]
#else
    [JsonPropertyOrder(7)]
#endif
    public string? StubInstanceId { get; init; }

    /// <summary>
    /// Serialization for <see cref="ClientIPAddress"/>.
    /// </summary>
#if NETSTANDARD
    [DataMember(Name = nameof(ClientIPAddress))]
#endif
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
    public bool IsBound => PersistedGuid is not null;

#if !NETSTANDARD
    [JsonIgnore]
#endif
    public bool IsConnected => BusId is not null;

#if !NETSTANDARD
    [JsonIgnore]
#endif
    public bool IsAttached => ClientIPAddress is not null;
}
