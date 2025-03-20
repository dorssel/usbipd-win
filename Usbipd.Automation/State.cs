// SPDX-FileCopyrightText: 2022 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

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
sealed class State
{
    internal State() { }

#if !NETSTANDARD
    [JsonConstructor]
    public State(IReadOnlyCollection<Device> devices)
    {
        Devices = devices;
    }
#endif

    /// <summary>
    /// Serialization for <see cref="Devices" />.
    /// </summary>
#if NETSTANDARD
    [DataMember(Name = nameof(Devices))]
#endif
    List<Device> _Devices = [];

    public IReadOnlyCollection<Device> Devices
    {
        get => _Devices.AsReadOnly();
        init => _Devices = [.. value];
    }
}
