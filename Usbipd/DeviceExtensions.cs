// SPDX-FileCopyrightText: 2022 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

using Usbipd.Automation;
using Windows.Win32;

namespace Usbipd;

static class DeviceExtensions
{
    // NOTE: a true internal extension on the Device class (as intended) leads to a spurious CA1515 warning.
    //    extension(Device)
    //    {
    /// <summary>
    /// Gets all USB devices, either bound or connected.
    /// </summary>
    public static IEnumerable<Device> GetAll()
    {
        var devices = new Dictionary<string, Device>(UsbipdRegistry.Instance.GetBoundDevices().Select(d => KeyValuePair.Create(d.InstanceId, d)));
        // Add all connected devices that are not hubs or stubs, and not already in the list (i.e. all USB devices that are available for USBIP sharing).
        foreach (var device in WindowsDevice.GetAll(PInvoke.GUID_DEVINTERFACE_USB_HUB).SelectMany(di => di.Children)
            .Where(d => !d.IsStub && !d.IsHub))
        {
            if (devices.ContainsKey(device.InstanceId))
            {
                // This device is bound, so we already have it.
                continue;
            }
            // This is a connected device that is not currently bound.
            // This can fail due to race conditions, in which case we just do not report the device.
            try
            {
                devices[device.InstanceId] = new(
                    instanceId: device.InstanceId,
                    description: device.Description,
                    isForced: device.HasVBoxDriver,
                    busId: device.BusId,
                    persistedGuid: null,
                    stubInstanceId: null,
                    clientIPAddress: null
                    );
            }
            catch (ConfigurationManagerException) { }
        }
        return devices.Values;
    }
}
