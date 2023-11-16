// SPDX-FileCopyrightText: 2022 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace Usbipd.Automation;

[DataContract]
public sealed partial class State
{
    public State() : this([]) { }

    [JsonConstructor]
    public State(IReadOnlyCollection<Device> devices) => Devices = devices;

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
