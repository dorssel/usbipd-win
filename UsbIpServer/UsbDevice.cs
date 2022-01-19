// SPDX-FileCopyrightText: 2022 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-2.0-only

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace UsbIpServer
{
    /// <summary>
    /// All data concerning a USB device that can be handled one way or another, including:
    /// <list type="bullet">
    ///     <item>Currently connected (non-hub) devices (operational or not).</item>
    ///     <item>Disconnected (persisted) devices that are bound.</item>
    /// </list>
    /// </summary>
    sealed partial record UsbDevice(string InstanceId, string Description, bool IsForced,
        BusId? BusId = null, Guid? Guid = null, IPAddress? IPAddress = null, string? StubInstanceId = null);

    sealed partial record UsbDevice
    {
        /// <summary>
        /// Gets all devices, either bound or connected.
        /// </summary>
        public static IEnumerable<UsbDevice> GetAll()
        {
            var usbDevices = new Dictionary<string, UsbDevice>(RegistryUtils.GetBoundDevices().Select(d => KeyValuePair.Create(d.InstanceId, d)));
            foreach (var device in ConfigurationManager.GetConnectedUsbDevices())
            {
                if (usbDevices.ContainsKey(device.InstanceId))
                {
                    // This device is bound, so we already have it.
                    continue;
                }
                // This is a connected device that is not currently bound.
                // This can fail due to race conditions, in which case we just do not report the device.
                try
                {
                    usbDevices[device.InstanceId] = new(
                        InstanceId: device.InstanceId,
                        Description: ConfigurationManager.GetDescription(device.DeviceNode),
                        BusId: ConfigurationManager.GetBusId(device.DeviceNode),
                        IsForced: ConfigurationManager.HasVBoxDriver(device.InstanceId));
                }
                catch (ConfigurationManagerException) { }
            }
            return usbDevices.Values;
        }
    }
}
