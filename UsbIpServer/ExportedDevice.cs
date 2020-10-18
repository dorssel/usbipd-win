/*
    usbipd-win: a server for hosting USB devices across networks
    Copyright (C) 2020  Frans van Dorsselaer

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program.  If not, see <https://www.gnu.org/licenses/>.
*/

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UsbIpServer.Interop;
using static UsbIpServer.Interop.Usb;
using static UsbIpServer.Interop.UsbIp;
using static UsbIpServer.Interop.WinSDK;
using static UsbIpServer.Tools;

namespace UsbIpServer
{
    sealed class ExportedDevice
    {
        public string Path { get; set; } = string.Empty;
        public string BusId { get => $"{BusNum}-{DevNum}"; }
        public uint BusNum { get; set; }
        public uint DevNum { get; set; }
        public Linux.UsbDeviceSpeed Speed { get; set; }
        public ushort VendorId { get; set; }
        public ushort ProductId { get; set; }
        public ushort BcdDevice { get; set; }
        public byte DeviceClass { get; set; }
        public byte DeviceSubClass { get; set; }
        public byte DeviceProtocol { get; set; }
        public byte ConfigurationValue { get; set; }
        public byte NumConfigurations { get; set; }

        public UsbConfigurationDescriptors ConfigurationDescriptors { get; private set; } = new UsbConfigurationDescriptors();

        public string Manufacturer { get; set; } = string.Empty;
        public string Product { get; set; } = string.Empty;
        public string SerialNumber { get; set; } = string.Empty;

        static void Serialize(Stream stream, string value, uint size)
        {
            var buf = new byte[size];
            Encoding.UTF8.GetBytes(value, 0, value.Length, buf, 0);
            stream.Write(buf);
        }

        static void Serialize(Stream stream, uint value)
        {
            var buf = new byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(buf, value);
            stream.Write(buf);
        }

        static void Serialize(Stream stream, ushort value)
        {
            var buf = new byte[2];
            BinaryPrimitives.WriteUInt16BigEndian(buf, value);
            stream.Write(buf);
        }

        static void Serialize(Stream stream, byte value)
        {
            stream.WriteByte(value);
        }

        public void Serialize(Stream stream, bool includeInterfaces)
        {
            Serialize(stream, Path, SYSFS_PATH_MAX);
            Serialize(stream, BusId, SYSFS_BUS_ID_SIZE);
            Serialize(stream, BusNum);
            Serialize(stream, DevNum);
            Serialize(stream, (uint)Speed);
            Serialize(stream, VendorId);
            Serialize(stream, ProductId);
            Serialize(stream, BcdDevice);
            Serialize(stream, DeviceClass);
            Serialize(stream, DeviceSubClass);
            Serialize(stream, DeviceProtocol);
            Serialize(stream, ConfigurationValue);
            Serialize(stream, NumConfigurations);

            var interfaces = ConfigurationDescriptors.GetUniqueInterfaces();
            Serialize(stream, (byte)interfaces.Length);
            if (includeInterfaces)
            {
                foreach (var (Class, SubClass, Protocol) in interfaces)
                {
                    Serialize(stream, Class);
                    Serialize(stream, SubClass);
                    Serialize(stream, Protocol);
                    stream.WriteByte(0); // padding
                }
            }
        }

        static async Task<UsbConfigurationDescriptors> GetConfigurationDescriptor(DeviceFile hub, ushort connectionIndex, byte numConfigurations)
        {
            var result = new UsbConfigurationDescriptors();

            for (byte configIndex = 0; configIndex < numConfigurations; ++configIndex)
            {
                var buf = new byte[Marshal.SizeOf<UsbDescriptorRequest>() + Marshal.SizeOf<UsbConfigurationDescriptor>()];
                var request = new UsbDescriptorRequest()
                {
                    ConnectionIndex = connectionIndex,
                    SetupPacket = {
                        wLength = (ushort)Marshal.SizeOf<UsbConfigurationDescriptor>(),
                        wValue = (ushort)(((byte)UsbDescriptorType.USB_CONFIGURATION_DESCRIPTOR_TYPE << 8) | configIndex)
                    }
                };
                StructToBytes(request, buf, 0);
                await hub.IoControlAsync(IoControl.IOCTL_USB_GET_DESCRIPTOR_FROM_NODE_CONNECTION, buf, buf);
                BytesToStruct(buf, Marshal.SizeOf<UsbDescriptorRequest>(), out UsbConfigurationDescriptor configuration);
                buf = new byte[Marshal.SizeOf<UsbDescriptorRequest>() + configuration.wTotalLength];
                request = new UsbDescriptorRequest()
                {
                    ConnectionIndex = connectionIndex,
                    SetupPacket = {
                        wLength = configuration.wTotalLength,
                        wValue = (ushort)(((byte)UsbDescriptorType.USB_CONFIGURATION_DESCRIPTOR_TYPE << 8) | configIndex)
                    }
                };
                StructToBytes(request, buf, 0);
                await hub.IoControlAsync(IoControl.IOCTL_USB_GET_DESCRIPTOR_FROM_NODE_CONNECTION, buf, buf);

                result.AddDescriptor(buf.AsSpan(Marshal.SizeOf<UsbDescriptorRequest>()));
            }

            return result;
        }

        public static async Task<ExportedDevice[]> GetAll(CancellationToken cancellationToken)
        {
            var exportedDevices = new SortedDictionary<string, ExportedDevice>();

            using var deviceInfoSet = NativeMethods.SetupDiGetClassDevs(IntPtr.Zero, "USB", IntPtr.Zero, DiGetClassFlags.DIGCF_ALLCLASSES | DiGetClassFlags.DIGCF_PRESENT);
            foreach (var devInfoData in EnumDeviceInfo(deviceInfoSet))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var instanceId = GetDevicePropertyString(deviceInfoSet, devInfoData, DEVPKEY_Device_InstanceId);
                if (IsUsbHub(instanceId))
                {
                    // device is itself a USB hub, which is not supported
                    continue;
                }
                var parentId = GetDevicePropertyString(deviceInfoSet, devInfoData, DEVPKEY_Device_Parent);
                if (!IsUsbHub(parentId))
                {
                    // parent is not a USB hub (which it must be for this device to be supported)
                    continue;
                }

                // OK, so the device is directly connected to a hub, but is not a hub itself ... this looks promising

                GetBusId(deviceInfoSet, devInfoData, out var hubNum, out var connectionIndex);

                var address = GetDevicePropertyUInt32(deviceInfoSet, devInfoData, DEVPKEY_Device_Address);
                if (connectionIndex != address)
                {
                    throw new NotSupportedException($"DEVPKEY_Device_Address ({address}) does not match DEVPKEY_Device_LocationInfo ({connectionIndex})");
                }

                // now query the parent USB hub for device details

                {
                    cancellationToken.ThrowIfCancellationRequested();

                    using var hubs = NativeMethods.SetupDiGetClassDevs(GUID_DEVINTERFACE_USB_HUB, parentId, IntPtr.Zero, DiGetClassFlags.DIGCF_DEVICEINTERFACE | DiGetClassFlags.DIGCF_PRESENT);
                    var (_, interfaceData) = EnumDeviceInterfaces(hubs, GUID_DEVINTERFACE_USB_HUB).Single();
                    var hubPath = GetDeviceInterfaceDetail(hubs, interfaceData);

                    cancellationToken.ThrowIfCancellationRequested();
                    using var hubFile = new DeviceFile(hubPath);
                    using var cancellationTokenRegistration = cancellationToken.Register(() => hubFile.Dispose());

                    var data = new UsbNodeConnectionInformationEx() { ConnectionIndex = connectionIndex };
                    var buf = StructToBytes(data);
                    await hubFile.IoControlAsync(IoControl.IOCTL_USB_GET_NODE_CONNECTION_INFORMATION_EX, buf, buf);
                    BytesToStruct(buf, 0, out data);

                   var exportedDevice = new ExportedDevice()
                    {
                        Path = instanceId,
                        BusNum = hubNum,
                        DevNum = connectionIndex,
                        Speed = MapWindowsSpeedToLinuxSpeed(data.Speed),
                        VendorId = data.DeviceDescriptor.idVendor,
                        ProductId = data.DeviceDescriptor.idProduct,
                        BcdDevice = data.DeviceDescriptor.bcdDevice,
                        DeviceClass = data.DeviceDescriptor.bDeviceClass,
                        DeviceSubClass = data.DeviceDescriptor.bDeviceSubClass,
                        DeviceProtocol = data.DeviceDescriptor.bDeviceProtocol,
                        ConfigurationValue = data.CurrentConfigurationValue,
                        NumConfigurations = data.DeviceDescriptor.bNumConfigurations,
                        ConfigurationDescriptors = await GetConfigurationDescriptor(hubFile, connectionIndex, data.DeviceDescriptor.bNumConfigurations),
                    };

                    exportedDevices.Add(exportedDevice.BusId, exportedDevice);
                }
            }

            return exportedDevices.Values.ToArray();
        }
    }
}
