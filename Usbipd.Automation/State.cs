// SPDX-FileCopyrightText: 2022 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Usbipd.Automation;

[DataContract]
public sealed class State
{
    /// <summary>
    /// Serialization for <see cref="Devices" />.
    /// </summary>
    [DataMember(Name = nameof(Devices))]
    List<Device> _Devices = new();

    public IReadOnlyCollection<Device> Devices
    {
        get => _Devices.AsReadOnly();
        init => _Devices = new(value);
    }
}
