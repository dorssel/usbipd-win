// SPDX-FileCopyrightText: 2021 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Usbipd.Automation;
using Windows.Win32;
using Windows.Win32.Devices.DeviceAndDriverInstallation;
using Windows.Win32.Devices.Properties;
using Windows.Win32.Devices.Usb;

namespace Usbipd;

static partial class ConfigurationManager
{
    public static void ThrowOnError(this CONFIGRET configRet, string function)
    {
        if (configRet != CONFIGRET.CR_SUCCESS)
        {
            throw new ConfigurationManagerException(configRet, $"{function} returned {configRet}");
        }
    }

    public static uint Locate_DevNode(string instanceId, bool present)
    {
        unsafe // DevSkim: ignore DS172412
        {
            fixed (char* pInstanceId = instanceId)
            {
                PInvoke.CM_Locate_DevNode(out var deviceNode, pInstanceId,
                    present ? CM_LOCATE_DEVNODE_FLAGS.CM_LOCATE_DEVNODE_NORMAL : CM_LOCATE_DEVNODE_FLAGS.CM_LOCATE_DEVNODE_PHANTOM)
                    .ThrowOnError(nameof(PInvoke.CM_Locate_DevNode));
                return deviceNode;
            }
        }
    }

    static string[] Get_Device_Interface_List(in Guid interfaceClassGuid, string? deviceId, CM_GET_DEVICE_INTERFACE_LIST_FLAGS flags)
    {
        unsafe // DevSkim: ignore DS172412
        {
            fixed (char* pDeviceId = deviceId)
            {
                PInvoke.CM_Get_Device_Interface_List_Size(out var bufferLen, interfaceClassGuid, pDeviceId, flags)
                    .ThrowOnError(nameof(PInvoke.CM_Get_Device_Interface_List_Size));
                var deviceInterfaceList = new string('\0', (int)bufferLen);
                fixed (char* buffer = deviceInterfaceList)
                {
                    PInvoke.CM_Get_Device_Interface_List(interfaceClassGuid, pDeviceId, buffer, bufferLen, flags)
                        .ThrowOnError(nameof(PInvoke.CM_Get_Device_Interface_List_Size));
                }
                return deviceInterfaceList.Split('\0', StringSplitOptions.RemoveEmptyEntries);
            }
        }
    }

    static unsafe object ConvertProperty(DEVPROPTYPE propertyType, byte* pBuffer, int propertyBufferSize) // DevSkim: ignore DS172412
    {
        return propertyType == DEVPROPTYPE.DEVPROP_TYPE_STRING
            ? new string((char*)pBuffer, 0, propertyBufferSize / sizeof(char)).TrimEnd('\0')
            : propertyType == DEVPROPTYPE.DEVPROP_TYPE_STRING_LIST
                ? (object)new string((char*)pBuffer, 0, propertyBufferSize / sizeof(char)).Split('\0', StringSplitOptions.RemoveEmptyEntries)
                : throw new NotImplementedException($"property type {propertyType}");
    }

    static object Get_Device_Interface_Property(string deviceInterface, in DEVPROPKEY devPropKey)
    {
        unsafe // DevSkim: ignore DS172412
        {
            var propertyBufferSize = 0u;
            var cr = PInvoke.CM_Get_Device_Interface_Property(deviceInterface, devPropKey, out var propertyType, null, ref propertyBufferSize, 0);
            if (cr != CONFIGRET.CR_BUFFER_SMALL)
            {
                ThrowOnError(cr, nameof(PInvoke.CM_Get_Device_Interface_Property));
            }
            var buffer = new byte[propertyBufferSize];
            fixed (byte* pBuffer = buffer)
            {
                PInvoke.CM_Get_Device_Interface_Property(deviceInterface, devPropKey, out propertyType, pBuffer, ref propertyBufferSize, 0)
                    .ThrowOnError(nameof(PInvoke.CM_Get_Device_Interface_Property));
                return ConvertProperty(propertyType, pBuffer, (int)propertyBufferSize);
            }
        }
    }

    static bool TryGetProperty(uint deviceNode, in DEVPROPKEY devPropKey, out byte[] value, out DEVPROPTYPE propertyType)
    {
        unsafe // DevSkim: ignore DS172412
        {
            var propertyBufferSize = 0u;
            var cr = PInvoke.CM_Get_DevNode_Property(deviceNode, devPropKey, out propertyType, null, ref propertyBufferSize, 0);
            if (cr != CONFIGRET.CR_BUFFER_SMALL)
            {
                value = default!;
                propertyType = DEVPROPTYPE.DEVPROP_TYPE_EMPTY;
                return false;
            }
            var buffer = new byte[propertyBufferSize];
            fixed (byte* pBuffer = buffer)
            {
                if (PInvoke.CM_Get_DevNode_Property(deviceNode, devPropKey, out propertyType, pBuffer, ref propertyBufferSize, 0) != CONFIGRET.CR_SUCCESS)
                {
                    value = default!;
                    propertyType = DEVPROPTYPE.DEVPROP_TYPE_EMPTY;
                    return false;
                }
            }
            value = buffer;
            return true;
        }
    }

    internal static bool TryGetProperty(uint deviceNode, in DEVPROPKEY devPropKey, out string value)
    {
        if (!TryGetProperty(deviceNode, devPropKey, out var buffer, out var propertyType))
        {
            value = default!;
            return false;
        }

        if (propertyType != DEVPROPTYPE.DEVPROP_TYPE_STRING)
        {
            value = default!;
            return false;
        }

        unsafe // DevSkim: ignore DS172412
        {
            fixed (byte* pBuffer = buffer)
            {
                // The buffer includes the terminating NUL character.
                value = new string((char*)pBuffer, 0, (buffer.Length / sizeof(char)) - 1);
                return true;
            }
        }
    }

    internal static bool TryGetProperty(uint deviceNode, in DEVPROPKEY devPropKey, out string[] value)
    {
        if (!TryGetProperty(deviceNode, devPropKey, out var buffer, out var propertyType))
        {
            value = default!;
            return false;
        }

        if (propertyType != DEVPROPTYPE.DEVPROP_TYPE_STRING_LIST)
        {
            value = default!;
            return false;
        }

        unsafe // DevSkim: ignore DS172412
        {
            fixed (byte* pBuffer = buffer)
            {
                // The buffer is double-NUL terminated.
                value = new string((char*)pBuffer, 0, (buffer.Length / sizeof(char)) - 2).Split('\0');
                return true;
            }
        }
    }

    static IEnumerable<uint> EnumChildren(uint parentNode)
    {
        // Failure here means: the hub has no (more) children, or a race between
        // removing the hub and enumerating its children. In any case, we are done enumerating.
        var cr = PInvoke.CM_Get_Child(out var childNode, parentNode, 0);
        if (cr != CONFIGRET.CR_SUCCESS)
        {
            yield break;
        }
        yield return childNode;

        while (true)
        {
            cr = PInvoke.CM_Get_Sibling(out childNode, childNode, 0);
            if (cr != CONFIGRET.CR_SUCCESS)
            {
                yield break;
            }
            yield return childNode;
        }
    }

    static Dictionary<uint, string> GetHubs()
    {
        var hubs = new Dictionary<uint, string>();
        var hubInterfaces = Get_Device_Interface_List(PInvoke.GUID_DEVINTERFACE_USB_HUB, null,
            CM_GET_DEVICE_INTERFACE_LIST_FLAGS.CM_GET_DEVICE_INTERFACE_LIST_PRESENT);
        foreach (var hubInterface in hubInterfaces)
        {
            try
            {
                // This may fail due to a race condition between a hub being removed and querying its details.
                // In such cases, just skip the hub as if it was never there in the first place.
                var hubId = (string)Get_Device_Interface_Property(hubInterface, PInvoke.DEVPKEY_Device_InstanceId);
                var hubNode = Locate_DevNode(hubId, true);
                hubs.Add(hubNode, hubInterface);
            }
            catch (ConfigurationManagerException) { }
        }
        return hubs;
    }

    [GeneratedRegex(@"^Port_#([0-9]{4}).Hub_#([0-9]{4})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LocationInfoRegex();

    public static BusId GetBusId(uint deviceNode)
    {
        if (!TryGetProperty(deviceNode, PInvoke.DEVPKEY_Device_LocationInfo, out string locationInfo))
        {
            return BusId.IncompatibleHub;
        }
        var match = LocationInfoRegex().Match(locationInfo);
        if (!match.Success)
        {
            // This is probably a device on an unsupported hub-type.
            // See for example https://github.com/dorssel/usbipd-win/issues/809.
            return BusId.IncompatibleHub;
        }
        var bus = ushort.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        var port = ushort.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        return bus == 0 || port == 0 ? BusId.IncompatibleHub : new(bus, port);
    }

    public static BusId? GetBusId(string instanceId)
    {
        try
        {
            return GetBusId(Locate_DevNode(instanceId, true));
        }
        catch (ConfigurationManagerException)
        {
            return null;
        }
    }

    static string? GetDeviceName(uint deviceNode)
    {
        if (!TryGetProperty(deviceNode, PInvoke.DEVPKEY_NAME, out string name))
        {
            // This can happen for devices without a driver *and* without a USB string table.
            return null;
        }
        name = name.Trim();
        return string.IsNullOrEmpty(name) ? null : name;
    }

    public const string UnknownDevice = "Unknown device";

    public static string GetDescription(uint deviceNode)
    {
        var isCompositeDevice = TryGetProperty(deviceNode, PInvoke.DEVPKEY_Device_CompatibleIds, out string[] compatibleIds)
            && compatibleIds.Contains(@"USB\COMPOSITE");
        if (!isCompositeDevice)
        {
            // NAME is FriendlyName (if it exists), or else it is Description (if it exists), or else UnknownDevice
            return GetDeviceName(deviceNode) ?? UnknownDevice;
        }

        // For USB\COMPOSITE we need to add the descriptions of the direct children.
        // We remove duplicates, often something like "HID Device", but we also want to preserve the order
        var descriptionSet = new SortedSet<string>();
        var descriptionList = new List<string>();

        void AddDescription(string? desc)
        {
            if (desc != null)
            {
                desc = desc.Trim();
                if (!string.IsNullOrEmpty(desc) && descriptionSet.Add(desc))
                {
                    descriptionList.Add(desc);
                }
            }
        }

        // The FriendlyName (if it exists) is useful for USB\COMPOSITE.
        // Description definitely is not (for composite devices).
        if (TryGetProperty(deviceNode, PInvoke.DEVPKEY_Device_FriendlyName, out string friendlyName))
        {
            AddDescription(friendlyName);
        }

        foreach (var childNode in EnumChildren(deviceNode))
        {
            AddDescription(GetDeviceName(childNode));
        }

        if (descriptionList.Count == 0)
        {
            // This can happen if the USB\COMPOSITE device does not have a FriendlyName and the
            // bus has not yet enumerated the children; for example, right after releasing the device
            // back to Windows. Just fall back to the non-descriptive name for USB\COMPOSITE.
            return GetDeviceName(deviceNode) ?? UnknownDevice;
        }

        return string.Join(", ", descriptionList);
    }

    internal sealed record ConnectedUsbDevice(uint DeviceNode, string InstanceId);

    public static IEnumerable<ConnectedUsbDevice> GetConnectedUsbDevices()
    {
        var hubs = GetHubs();
        var usbDevices = new Dictionary<string, ConnectedUsbDevice>();
        foreach (var hub in hubs)
        {
            foreach (var deviceNode in EnumChildren(hub.Key))
            {
                if (hubs.ContainsKey(deviceNode))
                {
                    // Do not include hubs.
                    continue;
                }
                // Getting properties may fail due to a race condition between a device being removed and querying its details.
                // In such cases, just skip the device as if it was never there in the first place.
                if (!TryGetProperty(deviceNode, PInvoke.DEVPKEY_Device_HardwareIds, out string[] hardwareIds))
                {
                    continue;
                }
                if (hardwareIds.Any(hardwareId => VidPid.TryParseId(hardwareId, out var vidPid) && vidPid == DriverDetails.Instance.VidPid))
                {
                    // Do not include stubs.
                    continue;
                }
                if (!TryGetProperty(deviceNode, PInvoke.DEVPKEY_Device_InstanceId, out string instanceId))
                {
                    continue;
                }
                usbDevices[instanceId] = new(deviceNode, instanceId);
            }
        }
        return usbDevices.Values;
    }

    public static string GetHubInterfacePath(string instanceId)
    {
        return GetHubInterfacePath(Locate_DevNode(instanceId, true));
    }

    static string GetHubInterfacePath(uint deviceNode)
    {
        PInvoke.CM_Get_Parent(out var hubDeviceNode, deviceNode, 0).ThrowOnError(nameof(PInvoke.CM_Get_Parent));
        return !TryGetProperty(hubDeviceNode, PInvoke.DEVPKEY_Device_InstanceId, out string hubInstanceId)
            ? throw new UnexpectedResultException("Device node without instance ID")
            : Get_Device_Interface_List(PInvoke.GUID_DEVINTERFACE_USB_HUB, hubInstanceId,
                CM_GET_DEVICE_INTERFACE_LIST_FLAGS.CM_GET_DEVICE_INTERFACE_LIST_PRESENT).Single();
    }

    internal sealed record VBoxDevice(uint DeviceNode, string InstanceId, string InterfacePath);

    public static VBoxDevice GetVBoxDevice(BusId busId)
    {
        var deviceInterfaces = Get_Device_Interface_List(Interop.VBoxUsb.GUID_CLASS_VBOXUSB, null,
            CM_GET_DEVICE_INTERFACE_LIST_FLAGS.CM_GET_DEVICE_INTERFACE_LIST_PRESENT);
        foreach (var deviceInterface in deviceInterfaces)
        {
            // This may fail due to a race condition between a device being removed and querying its details.
            // In such cases, just skip the device as if it was never there in the first place.
            try
            {
                var deviceId = (string)Get_Device_Interface_Property(deviceInterface, PInvoke.DEVPKEY_Device_InstanceId);
                var deviceNode = Locate_DevNode(deviceId, true);
                if (GetBusId(deviceNode) == busId)
                {
                    return new(deviceNode, deviceId, deviceInterface);
                }
            }
            catch (ConfigurationManagerException) { }
        }
        throw new FileNotFoundException();
    }

    public static bool HasVBoxDriver(uint deviceNode)
    {
        if (!TryGetProperty(deviceNode, PInvoke.DEVPKEY_Device_MatchingDeviceId, out string matchingDeviceId))
        {
            // Device does not have a MatchingDeviceId, so definitely not the VBoxUSB driver.
            return false;
        }
        return VidPid.TryParseId(matchingDeviceId, out var vidPid) && vidPid == DriverDetails.Instance.VidPid;
    }

    public static bool HasVBoxDriver(string instanceId)
    {
        try
        {
            var deviceNode = Locate_DevNode(instanceId, false);
            return HasVBoxDriver(deviceNode);
        }
        catch (ConfigurationManagerException)
        {
            // Device is gone (uninstalled), so definitely not the VBoxUSB driver.
            return false;
        }
    }

    const string FriendlyName = "USBIP Shared Device";

    public static void SetDeviceFriendlyName(uint deviceNode)
    {
        // NOTE: Must include terminating NUL character.
        PInvoke.CM_Set_DevNode_Property(deviceNode, PInvoke.DEVPKEY_Device_FriendlyName, DEVPROPTYPE.DEVPROP_TYPE_STRING,
            Encoding.Unicode.GetBytes($"{FriendlyName}\0"), 0).ThrowOnError(nameof(PInvoke.CM_Set_DevNode_Property));
    }

    /// <summary>
    /// See https://docs.microsoft.com/en-us/windows-hardware/drivers/install/porting-from-setupapi-to-cfgmgr32#restart-device
    /// </summary>
    internal sealed partial class RestartingDevice
        : IDisposable
    {
        public RestartingDevice(string instanceId)
            : this(Locate_DevNode(instanceId, true))
        {
        }

        public RestartingDevice(uint deviceNode)
        {
            DeviceNode = deviceNode;
            unsafe // DevSkim: ignore DS172412
            {
                PNP_VETO_TYPE vetoType;
                var vetoName = new string('\0', (int)PInvoke.MAX_PATH);
                fixed (char* pVetoName = vetoName)
                {
                    var cr = PInvoke.CM_Query_And_Remove_SubTree(DeviceNode, &vetoType, pVetoName, PInvoke.MAX_PATH,
                        PInvoke.CM_REMOVE_NO_RESTART | PInvoke.CM_REMOVE_UI_NOT_OK);
                    if (cr == CONFIGRET.CR_REMOVE_VETOED)
                    {
                        vetoName = vetoName.TrimEnd('\0');
                        throw new ConfigurationManagerException(cr, $"{nameof(PInvoke.CM_Query_And_Remove_SubTree)} returned {cr}: {vetoType}, {vetoName}");
                    }
                    ThrowOnError(cr, nameof(PInvoke.CM_Query_And_Remove_SubTree));
                }
            }
        }

        readonly uint DeviceNode;

        public void Dispose()
        {
            // We ignore errors for multiple reasons:
            // a) Dispose is not supposed to throw.
            // b) Race condition with physical device removal.
            // c) Race condition with the device node being marked ready by something else and
            //    device enumeration already replaced the DevNode with its (non-)VBox counterpart.

            try
            {
                // Some drivers fail to initialize if the device is left in a non-default state.
                // They expect to be loaded after the device is just plugged in. Hence, we cycle the port,
                // which acts as an unplug/replug. Therefore, the driver (host or client) will see a nice clean device.

                // NOTE: Some devices (SanDisk flash drives, mostly) don't like this right after driver load.
                // Since this code is run right after switching drivers, give the device some time to settle.
                // Experiments show 20ms almost does the job (very few failures), and 30ms no longer
                // reproduced errors after 100 attach/detach cycles. So, we use 100ms for good measure.

                Thread.Sleep(TimeSpan.FromMilliseconds(100));

                var busId = GetBusId(DeviceNode);
                var hubInterfacePath = GetHubInterfacePath(DeviceNode);
                using var hubFile = new DeviceFile(hubInterfacePath);

                var data = new USB_CYCLE_PORT_PARAMS() { ConnectionIndex = busId.Port };
                var buf = Tools.StructToBytes(data);
                hubFile.IoControlAsync(PInvoke.IOCTL_USB_HUB_CYCLE_PORT, buf, buf).Wait();
            }
            catch (ConfigurationManagerException) { }
            catch (Win32Exception) { }
            catch (AggregateException ex) when (ex.InnerException is Win32Exception) { }

            // This is the reverse of what the constructor accomplished.
            _ = PInvoke.CM_Setup_DevNode(DeviceNode, PInvoke.CM_SETUP_DEVNODE_READY);
        }
    }
}
