// SPDX-FileCopyrightText: 2022 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-2.0-only

using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

[assembly: CLSCompliant(false)]

namespace Usbipd.Automation
{
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
}
