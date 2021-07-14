// SPDX-FileCopyrightText: 2020 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-2.0-only

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
        public string BusId => $"{BusNum}-{DevNum}";
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
                StructToBytes(request, buf);
                await hub.IoControlAsync(IoControl.IOCTL_USB_GET_DESCRIPTOR_FROM_NODE_CONNECTION, buf, buf);
                BytesToStruct(buf.AsSpan(Marshal.SizeOf<UsbDescriptorRequest>()), out UsbConfigurationDescriptor configuration);
                buf = new byte[Marshal.SizeOf<UsbDescriptorRequest>() + configuration.wTotalLength];
                request = new UsbDescriptorRequest()
                {
                    ConnectionIndex = connectionIndex,
                    SetupPacket = {
                        wLength = configuration.wTotalLength,
                        wValue = (ushort)(((byte)UsbDescriptorType.USB_CONFIGURATION_DESCRIPTOR_TYPE << 8) | configIndex)
                    }
                };
                StructToBytes(request, buf);
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
                    BytesToStruct(buf, out data);

                    var speed = MapWindowsSpeedToLinuxSpeed(data.Speed);

                    var data2 = new UsbNodeConnectionInformationExV2()
                    {
                        ConnectionIndex = connectionIndex,
                        Length = (uint)Marshal.SizeOf<UsbNodeConnectionInformationExV2>(),
                        SupportedUsbProtocols = UsbProtocols.Usb110 | UsbProtocols.Usb200 | UsbProtocols.Usb300,
                    };
                    var buf2 = StructToBytes(data2);
                    await hubFile.IoControlAsync(IoControl.IOCTL_USB_GET_NODE_CONNECTION_INFORMATION_EX_V2, buf2, buf2);
                    BytesToStruct(buf2, out data2);

                    if ((data2.SupportedUsbProtocols & UsbProtocols.Usb300) != 0)
                    {
                        if ((data2.Flags & UsbNodeConnectionInformationExV2Flags.DeviceIsOperatingAtSuperSpeedPlusOrHigher) != 0)
                        {
                            speed = Linux.UsbDeviceSpeed.USB_SPEED_SUPER_PLUS;
                        }
                        else if ((data2.Flags & UsbNodeConnectionInformationExV2Flags.DeviceIsOperatingAtSuperSpeedOrHigher) != 0)
                        {
                            speed = Linux.UsbDeviceSpeed.USB_SPEED_SUPER;
                        }
                    }

                    var exportedDevice = new ExportedDevice()
                    {
                        Path = instanceId,
                        BusNum = hubNum,
                        DevNum = connectionIndex,
                        Speed = speed,
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
