// SPDX-FileCopyrightText: 2022 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

using System.Runtime.Serialization;
#if !NETSTANDARD
using System.Text.Json.Serialization;
#endif

namespace Usbipd.Automation;

[DataContract]
public sealed partial class State
{
    public State() { }

#if !NETSTANDARD
    [JsonConstructor]
    public State(IReadOnlyCollection<Device> devices) => Devices = devices;
#endif

    /// <summary>
    /// Serialization for <see cref="Devices" />.
    /// </summary>
    [DataMember(Name = nameof(Devices))]
    List<Device> _Devices = [];

    public IReadOnlyCollection<Device> Devices
    {
        get => _Devices.AsReadOnly();
        init => _Devices = new(value);
    }
}
