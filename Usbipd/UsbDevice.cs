// SPDX-FileCopyrightText: 2022 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

using System.Net;
using Usbipd.Automation;
using Windows.Win32;

namespace Usbipd;

/// <summary>
/// All data concerning a USB device that can be handled one way or another, including:
/// <list type="bullet">
///     <item>Currently connected (non-hub) devices (operational or not).</item>
///     <item>Disconnected (persisted) devices that are bound.</item>
/// </list>
/// </summary>
sealed record UsbDevice(string InstanceId, string Description, bool IsForced,
    BusId? BusId = null, Guid? Guid = null, IPAddress? IPAddress = null, string? StubInstanceId = null)
{
    public VidPid HardwareId => VidPid.TryParseId(InstanceId, out var vidPid) ? vidPid : default;

    /// <summary>
    /// Gets all devices, either bound or connected.
    /// </summary>
    public static IEnumerable<UsbDevice> GetAll()
    {
        var usbDevices = new Dictionary<string, UsbDevice>(UsbipdRegistry.Instance.GetBoundDevices().Select(d => KeyValuePair.Create(d.InstanceId, d)));
        // Add all connected devices that are not hubs or stubs, and not already in the list (i.e. all USB devices that are available for USBIP sharing).
        foreach (var device in WindowsDevice.GetAll(PInvoke.GUID_DEVINTERFACE_USB_HUB).SelectMany(di => di.Children)
            .Where(d => !d.IsStub && !d.IsHub))
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
                    Description: device.Description,
                    BusId: device.BusId,
                    IsForced: device.HasVBoxDriver);
            }
            catch (ConfigurationManagerException) { }
        }
        return usbDevices.Values;
    }
}
