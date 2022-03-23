// SPDX-FileCopyrightText: 2021 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-2.0-only

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Windows.Win32;
using Windows.Win32.Devices.DeviceAndDriverInstallation;
using Windows.Win32.Devices.Properties;
using Windows.Win32.UI.Shell.PropertiesSystem;
using static UsbIpServer.Interop.WinSDK;

namespace Windows.Win32.Devices.Properties
{
    partial struct DEVPROPKEY
    {
        /// <summary>
        /// *HACK*
        /// 
        /// CsWin32 is confused about PROPERTYKEY and DEVPROPKEY, which are in fact the exact same structure.
        /// This is an implicit c++-like "reinterpret_cast".
        /// </summary>
        public static implicit operator DEVPROPKEY(in PROPERTYKEY propertyKey)
        {
            return Unsafe.As<PROPERTYKEY, DEVPROPKEY>(ref Unsafe.AsRef(propertyKey));
        }
    }
}

namespace UsbIpServer
{
    static class ConfigurationManager
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
            unsafe
            {
                fixed (char* pInstanceId = instanceId)
                {
                    uint deviceNode;
                    PInvoke.CM_Locate_DevNode(&deviceNode, (ushort*)pInstanceId, present ? PInvoke.CM_LOCATE_DEVNODE_NORMAL : PInvoke.CM_LOCATE_DEVNODE_PHANTOM).ThrowOnError(nameof(PInvoke.CM_Locate_DevNode));
                    return deviceNode;
                }
            }
        }

        static string[] Get_Device_Interface_List(in Guid interfaceClassGuid, string? deviceId, uint flags)
        {
            unsafe
            {
                fixed (char* pDeviceId = deviceId)
                {
                    uint bufferLen;
                    var guid = interfaceClassGuid;
                    PInvoke.CM_Get_Device_Interface_List_Size(&bufferLen, &guid, (ushort*)pDeviceId, flags).ThrowOnError(nameof(PInvoke.CM_Get_Device_Interface_List_Size));
                    var deviceInterfaceList = new string('\0', (int)bufferLen);
                    fixed (char* buffer = deviceInterfaceList)
                    {
                        PInvoke.CM_Get_Device_Interface_List(&guid, (ushort*)pDeviceId, buffer, bufferLen, flags).ThrowOnError(nameof(PInvoke.CM_Get_Device_Interface_List_Size));
                    }
                    return deviceInterfaceList.Split('\0', StringSplitOptions.RemoveEmptyEntries);
                }
            }
        }

        static unsafe object ConvertProperty(uint propertyType, byte* pBuffer, int propertyBufferSize)
        {
            return propertyType switch
            {
                PInvoke.DEVPROP_TYPE_STRING => new string((char*)pBuffer, 0, propertyBufferSize / sizeof(char)).TrimEnd('\0'),
                PInvoke.DEVPROP_TYPE_STRING | PInvoke.DEVPROP_TYPEMOD_LIST => new string((char*)pBuffer, 0, propertyBufferSize / sizeof(char)).Split('\0', StringSplitOptions.RemoveEmptyEntries),
                _ => throw new NotImplementedException($"property type {propertyType}"),
            };
        }

        static object Get_Device_Interface_Property(string deviceInterface, in DEVPROPKEY devPropKey)
        {
            unsafe
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
                    PInvoke.CM_Get_Device_Interface_Property(deviceInterface, devPropKey, out propertyType, pBuffer, ref propertyBufferSize, 0).ThrowOnError(nameof(PInvoke.CM_Get_Device_Interface_Property));
                    return ConvertProperty(propertyType, pBuffer, (int)propertyBufferSize);
                }
            }
        }

        static object Get_DevNode_Property(uint deviceNode, in DEVPROPKEY devPropKey)
        {
            unsafe
            {
                var propertyBufferSize = 0u;
                var cr = PInvoke.CM_Get_DevNode_Property(deviceNode, devPropKey, out var propertyType, null, ref propertyBufferSize, 0);
                if (cr != CONFIGRET.CR_BUFFER_SMALL)
                {
                    ThrowOnError(cr, nameof(PInvoke.CM_Get_DevNode_Property));
                }
                var buffer = new byte[propertyBufferSize];
                fixed (byte* pBuffer = buffer)
                {
                    PInvoke.CM_Get_DevNode_Property(deviceNode, devPropKey, out propertyType, pBuffer, ref propertyBufferSize, 0).ThrowOnError(nameof(PInvoke.CM_Get_DevNode_Property));
                    return ConvertProperty(propertyType, pBuffer, (int)propertyBufferSize);
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
            var hubInterfaces = Get_Device_Interface_List(PInvoke.GUID_DEVINTERFACE_USB_HUB, null, PInvoke.CM_GET_DEVICE_INTERFACE_LIST_PRESENT);
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

        public static BusId GetBusId(uint deviceNode)
        {
            var locationInfo = (string)Get_DevNode_Property(deviceNode, PInvoke.DEVPKEY_Device_LocationInfo);
            var match = Regex.Match(locationInfo, "^Port_#([0-9]{4}).Hub_#([0-9]{4})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!match.Success)
            {
                // We want users to report this one.
                throw new NotSupportedException($"DEVPKEY_Device_LocationInfo returned '{locationInfo}', expected form 'Port_#0123.Hub_#4567'");
            }
            return new()
            {
                Bus = ushort.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture),
                Port = ushort.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
            };
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

        public static string GetDescription(uint deviceNode)
        {
            var isCompositeDevice = Get_DevNode_Property(deviceNode, PInvoke.DEVPKEY_Device_CompatibleIds) is string[] compatibleIds && compatibleIds.Contains(@"USB\COMPOSITE");
            if (!isCompositeDevice)
            {
                // NAME is FriendlyName (if it exists), or else it is Description
                return ((string)Get_DevNode_Property(deviceNode, PInvoke.DEVPKEY_NAME)).Trim();
            }

            // For USB\COMPOSITE we need to add the descriptions of the direct children.
            // We remove duplicates, often something like "HID Device", but we also want to preserve the order
            var descriptionSet = new SortedSet<string>();
            var descriptionList = new List<string>();

            try
            {
                // The FriendlyName (if it exists) is usefull for USB\COMPOSITE, but the Description is not.
                // So, if FriendlyName exists, we start the list with that.
                var friendlyName = ((string)Get_DevNode_Property(deviceNode, PInvoke.DEVPKEY_Device_FriendlyName)).Trim();
                if (!string.IsNullOrEmpty(friendlyName))
                {
                    descriptionList.Add(friendlyName);
                    descriptionSet.Add(friendlyName);
                }
            }
            catch (ConfigurationManagerException) { }

            foreach (var childNode in EnumChildren(deviceNode))
            {
                var name = ((string)Get_DevNode_Property(childNode, PInvoke.DEVPKEY_NAME)).Trim();
                if (!string.IsNullOrEmpty(name) && descriptionSet.Add(name))
                {
                    descriptionList.Add(name);
                }
            }

            if (descriptionList.Count == 0)
            {
                // This can happen if the USB\COMPOSITE device does not have a FriendlyName and the
                // bus has not yet enumerated the children; for example, right after releasing the device
                // back to Windows. Just fall back to the non-descriptive name for USB\COMPOSITE.
                return ((string)Get_DevNode_Property(deviceNode, PInvoke.DEVPKEY_NAME)).Trim();
            }

            return string.Join(", ", descriptionList);
        }

        public sealed record ConnectedUsbDevice(uint DeviceNode, string InstanceId);

        public static IEnumerable<ConnectedUsbDevice> GetConnectedUsbDevices()
        {
            var hubs = GetHubs();
            var usbDevices = new Dictionary<string, ConnectedUsbDevice>();
            foreach (var hub in hubs)
            {
                foreach (var deviceNode in EnumChildren(hub.Key))
                {
                    // This may fail due to a race condition between a device being removed and querying its details.
                    // In such cases, just skip the device as if it was never there in the first place.
                    try
                    {
                        if (hubs.ContainsKey(deviceNode))
                        {
                            // Do not include hubs.
                            continue;
                        }
                        var hardwareIds = (string[])Get_DevNode_Property(deviceNode, PInvoke.DEVPKEY_Device_HardwareIds);
                        if (hardwareIds.Any(hardwareId => hardwareId.Contains(Interop.VBoxUsb.StubHardwareId, StringComparison.InvariantCultureIgnoreCase)))
                        {
                            // Do not include stubs.
                            continue;
                        }
                        var instanceId = (string)Get_DevNode_Property(deviceNode, PInvoke.DEVPKEY_Device_InstanceId);
                        usbDevices[instanceId] = new(deviceNode, instanceId);
                    }
                    catch (ConfigurationManagerException) { }
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
            var hubInstanceId = (string)Get_DevNode_Property(hubDeviceNode, PInvoke.DEVPKEY_Device_InstanceId);
            return Get_Device_Interface_List(PInvoke.GUID_DEVINTERFACE_USB_HUB, hubInstanceId, PInvoke.CM_GET_DEVICE_INTERFACE_LIST_PRESENT).Single();
        }

        public sealed record VBoxDevice(uint DeviceNode, string InstanceId, string InterfacePath);

        public static VBoxDevice GetVBoxDevice(BusId busId)
        {
            var deviceInterfaces = Get_Device_Interface_List(Interop.VBoxUsb.GUID_CLASS_VBOXUSB, null, PInvoke.CM_GET_DEVICE_INTERFACE_LIST_PRESENT);
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

        public static bool HasVBoxDriver(string instanceId)
        {
            try
            {
                var deviceNode = Locate_DevNode(instanceId, false);
                var driverDesc = (string)Get_DevNode_Property(deviceNode, PInvoke.DEVPKEY_Device_DriverDesc);
                return driverDesc == "VirtualBox USB";
            }
            catch (ConfigurationManagerException)
            {
                // Device is gone (uninstalled) or does not have a driver description.
                // In any case, the device does not have the VBoxDriver.
                return false;
            }
        }

        public static IEnumerable<string> GetOriginalDeviceIdsWithVBoxDriver()
        {
            // This gets all the VBox driver installations ever installed, even those
            // that are not currently installed for a device and for devices that are not
            // plugged in now.
            var deviceInterfaces = Get_Device_Interface_List(Interop.VBoxUsb.GUID_CLASS_VBOXUSB, null, PInvoke.CM_GET_DEVICE_INTERFACE_LIST_ALL_DEVICES);
            foreach (var deviceInterface in deviceInterfaces)
            {
                string deviceId;
                // This may fail due to one of the properties not existing.
                // In such cases, just skip the device as it does not have the VBox driver currently anyway.
                try
                {
                    deviceId = (string)Get_Device_Interface_Property(deviceInterface, PInvoke.DEVPKEY_Device_InstanceId);
                    var deviceNode = Locate_DevNode(deviceId, false);
                    // Filter out the devices that are mocked by VboxUsbMon, since those are supposed to have the VBox driver.
                    var hardwareIds = (string[])Get_DevNode_Property(deviceNode, PInvoke.DEVPKEY_Device_HardwareIds);
                    if (hardwareIds.Any(hardwareId => hardwareId.Contains(Interop.VBoxUsb.StubHardwareId, StringComparison.InvariantCultureIgnoreCase)))
                    {
                        continue;
                    }
                    // Don't return the device if the *current* driver isn't the VBox driver.
                    if (!HasVBoxDriver(deviceId))
                    {
                        continue;
                    }
                }
                catch (ConfigurationManagerException)
                {
                    continue;
                }
                yield return deviceId;
            }
        }

        public static void SetDeviceFriendlyName(uint deviceNode)
        {
            unsafe
            {
                var friendlyName = "USBIP Shared Device";
                fixed (char* pValue = friendlyName)
                {
                    DEVPROPKEY devPropKey = PInvoke.DEVPKEY_Device_FriendlyName;
                    PInvoke.CM_Set_DevNode_Property(deviceNode, &devPropKey, PInvoke.DEVPROP_TYPE_STRING, (byte*)pValue, (uint)(friendlyName.Length + 1) * sizeof(char), 0).ThrowOnError(nameof(PInvoke.CM_Set_DevNode_Property));
                }
            }
        }

        /// <summary>
        /// See https://docs.microsoft.com/en-us/windows-hardware/drivers/install/porting-from-setupapi-to-cfgmgr32#restart-device
        /// </summary>
        public sealed class RestartingDevice
            : IDisposable
        {
            public RestartingDevice(string instanceId)
                 : this(Locate_DevNode(instanceId, true))
            {
            }

            public RestartingDevice(uint deviceNode)
            {
                DeviceNode = deviceNode;
                unsafe
                {
                    PNP_VETO_TYPE vetoType;
                    var vetoName = new string('\0', (int)PInvoke.MAX_PATH);
                    fixed (char* pVetoName = vetoName)
                    {
                        var cr = PInvoke.CM_Query_And_Remove_SubTree(DeviceNode, &vetoType, pVetoName, PInvoke.MAX_PATH, PInvoke.CM_REMOVE_NO_RESTART | PInvoke.CM_REMOVE_UI_NOT_OK);
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
                    // For extra measure, we also try to reset the USB port, which may fail silently.
                    var busId = GetBusId(DeviceNode);
                    var hubInterfacePath = GetHubInterfacePath(DeviceNode);
                    using var hubFile = new DeviceFile(hubInterfacePath);

                    var data = new UsbCyclePortParams() { ConnectionIndex = busId.Port };
                    var buf = Tools.StructToBytes(data);
                    hubFile.IoControlAsync(IoControl.IOCTL_USB_HUB_CYCLE_PORT, buf, buf).Wait();
                }
                catch (ConfigurationManagerException) { }
                catch (Win32Exception) { }

                // This is the reverse of what the constructor accomplished.
                PInvoke.CM_Setup_DevNode(DeviceNode, PInvoke.CM_SETUP_DEVNODE_READY);
            }
        }
    }
}
