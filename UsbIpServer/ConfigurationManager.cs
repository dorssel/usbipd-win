// SPDX-FileCopyrightText: 2021 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-2.0-only

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Windows.Win32;
using Windows.Win32.Devices.DeviceAndDriverInstallation;
using Windows.Win32.Devices.Properties;
using Windows.Win32.UI.Shell.PropertiesSystem;

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

        static uint Locate_DevNode(string instanceId)
        {
            unsafe
            {
                fixed (char* pInstanceId = instanceId)
                {
                    uint deviceNode;
                    PInvoke.CM_Locate_DevNode(&deviceNode, (ushort*)pInstanceId, PInvoke.CM_LOCATE_DEVNODE_NORMAL).ThrowOnError(nameof(PInvoke.CM_Locate_DevNode));
                    return deviceNode;
                }
            }
        }

        static string[] Get_Device_Interface_List(in Guid interfaceClassGuid, uint flags)
        {
            unsafe
            {
                PInvoke.CM_Get_Device_Interface_List_Size(out var bufferLen, interfaceClassGuid, null, flags).ThrowOnError(nameof(PInvoke.CM_Get_Device_Interface_List_Size));
                var deviceInterfaceList = new string('\0', (int)bufferLen);
                fixed (char* buffer = deviceInterfaceList)
                {
                    PInvoke.CM_Get_Device_Interface_List(interfaceClassGuid, null, buffer, bufferLen, flags).ThrowOnError(nameof(PInvoke.CM_Get_Device_Interface_List_Size));
                }
                return deviceInterfaceList.Split('\0', StringSplitOptions.RemoveEmptyEntries);
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
            var hubInterfaces = Get_Device_Interface_List(PInvoke.GUID_DEVINTERFACE_USB_HUB, PInvoke.CM_GET_DEVICE_INTERFACE_LIST_PRESENT);
            foreach (var hubInterface in hubInterfaces)
            {
                try
                {
                    // This may fail due to a race condition between a hub being removed and querying its details.
                    // In such cases, just skip the hub as if it was never there in the first place.
                    var hubId = (string)Get_Device_Interface_Property(hubInterface, PInvoke.DEVPKEY_Device_InstanceId);
                    var hubNode = Locate_DevNode(hubId);
                    hubs.Add(hubNode, hubInterface);
                }
                catch (ConfigurationManagerException) { }
            }
            return hubs;
        }

        static BusId GetBusId(uint deviceNode)
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

        static string GetDescription(uint deviceNode)
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

        public sealed record UsbDevice
            : IComparable<UsbDevice>
        {
            public BusId BusId { get; init; }
            public string DeviceId { get; init; } = string.Empty;
            public string HubInterface { get; init; } = string.Empty;
            public string Description { get; init; } = string.Empty;

            public int CompareTo(UsbDevice? other) => (other is null) ? 1 : BusId.CompareTo(other.BusId);
        }

        public static SortedSet<UsbDevice> GetUsbDevices(bool includeDescriptions)
        {
            var hubs = GetHubs();
            var usbDevices = new SortedSet<UsbDevice>();
            foreach (var hub in hubs)
            {
                foreach (var deviceNode in EnumChildren(hub.Key))
                {
                    // This may fail due to a race condition between a device being removed and querying its details.
                    // In such cases, just skip the device as if it was never there in the first place.
                    try
                    {
                        if (!hubs.ContainsKey(deviceNode))
                        {
                            // Device is not a hub.
                            usbDevices.Add(new()
                            {
                                BusId = GetBusId(deviceNode),
                                DeviceId = (string)Get_DevNode_Property(deviceNode, PInvoke.DEVPKEY_Device_InstanceId),
                                HubInterface = hub.Value,
                                Description = includeDescriptions ? GetDescription(deviceNode) : string.Empty,
                            });
                        }
                    }
                    catch (ConfigurationManagerException) { }
                }
            }
            return usbDevices;
        }

        public sealed record VBoxDevice
        {
            public uint DeviceNode { get; init; }
            public string InterfacePath { get; init; } = string.Empty;
        }


        public static VBoxDevice GetVBoxDevice(BusId busId)
        {
            var deviceInterfaces = Get_Device_Interface_List(Interop.VBoxUsb.GUID_CLASS_VBOXUSB, PInvoke.CM_GET_DEVICE_INTERFACE_LIST_PRESENT);
            foreach (var deviceInterface in deviceInterfaces)
            {
                // This may fail due to a race condition between a device being removed and querying its details.
                // In such cases, just skip the device as if it was never there in the first place.
                try
                {
                    var deviceId = (string)Get_Device_Interface_Property(deviceInterface, PInvoke.DEVPKEY_Device_InstanceId);
                    var deviceNode = Locate_DevNode(deviceId);
                    if (GetBusId(deviceNode) == busId)
                    {
                        return new()
                        {
                            DeviceNode = deviceNode,
                            InterfacePath = deviceInterface,
                        };
                    }
                }
                catch (ConfigurationManagerException) { }
            }
            throw new FileNotFoundException();
        }

        public static void SetDeviceProperty(VBoxDevice vboxDevice, in DEVPROPKEY devPropKey, string value)
        {
            unsafe
            {
                fixed (DEVPROPKEY* pDevPropKey = &devPropKey)
                {
                    fixed (char* pValue = value)
                    {
                        PInvoke.CM_Set_DevNode_Property(vboxDevice.DeviceNode, pDevPropKey, PInvoke.DEVPROP_TYPE_STRING, (byte*)pValue, (uint)(value.Length + 1) * sizeof(char), 0);
                    }
                }
            }
        }

        public static void RestartDevice(string instanceId)
        {
            var deviceNode = Locate_DevNode(instanceId);
            PInvoke.CM_Disable_DevNode(deviceNode, PInvoke.CM_DISABLE_UI_NOT_OK).ThrowOnError(nameof(PInvoke.CM_Disable_DevNode));
            PInvoke.CM_Enable_DevNode(deviceNode, 0).ThrowOnError(nameof(PInvoke.CM_Enable_DevNode));
        }
    }
}
