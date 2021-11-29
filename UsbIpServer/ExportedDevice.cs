// SPDX-FileCopyrightText: 2020 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-2.0-only

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UsbIpServer.Interop;
using Windows.Win32;
using Windows.Win32.Devices.DeviceAndDriverInstallation;
using Windows.Win32.Devices.Usb;
using static UsbIpServer.Interop.UsbIp;
using static UsbIpServer.Interop.WinSDK;
using static UsbIpServer.Tools;

namespace UsbIpServer
{
    sealed class ExportedDevice
    {
        private ExportedDevice()
        {
        }

        public string Path { get; private set; } = string.Empty;
        public string HubPath { get; private init; } = string.Empty;
        public BusId BusId { get; private init; }
        public Linux.UsbDeviceSpeed Speed { get; private init; }
        public ushort VendorId { get; private init; }
        public ushort ProductId { get; private init; }
        public ushort BcdDevice { get; private init; }
        public byte DeviceClass { get; private init; }
        public byte DeviceSubClass { get; private init; }
        public byte DeviceProtocol { get; private init; }
        public byte ConfigurationValue { get; private init; }
        public byte NumConfigurations { get; private init; }
        public List<(byte, byte, byte)> Interfaces { get; private set; } = new();

        public string Description { get; private set; } = string.Empty;

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
            Serialize(stream, BusId.ToString(), SYSFS_BUS_ID_SIZE);
            Serialize(stream, (uint)BusId.Bus);
            Serialize(stream, (uint)BusId.Port);
            Serialize(stream, (uint)Speed);
            Serialize(stream, VendorId);
            Serialize(stream, ProductId);
            Serialize(stream, BcdDevice);
            Serialize(stream, DeviceClass);
            Serialize(stream, DeviceSubClass);
            Serialize(stream, DeviceProtocol);
            Serialize(stream, ConfigurationValue);
            Serialize(stream, NumConfigurations);

            Serialize(stream, (byte)Interfaces.Count);
            if (includeInterfaces)
            {
                foreach (var (Class, SubClass, Protocol) in Interfaces)
                {
                    Serialize(stream, Class);
                    Serialize(stream, SubClass);
                    Serialize(stream, Protocol);
                    stream.WriteByte(0); // padding
                }
            }
        }

        static async Task<List<(byte,byte,byte)>> GetInterfacesAsync(DeviceFile hub, ushort connectionIndex)
        {
            var result = new List<(byte, byte, byte)>();

            // IOCTL_USB_GET_DESCRIPTOR_FROM_NODE_CONNECTION will always get the current configuration, any index is not used.
            // This is not a problem, the result is only used informatively.
            var buf = new byte[Marshal.SizeOf<UsbDescriptorRequest>() + Marshal.SizeOf<USB_CONFIGURATION_DESCRIPTOR>()];
            var request = new UsbDescriptorRequest()
            {
                ConnectionIndex = connectionIndex,
                SetupPacket = {
                    wLength = (ushort)Marshal.SizeOf<USB_CONFIGURATION_DESCRIPTOR>(),
                    wValue = (ushort)(PInvoke.USB_CONFIGURATION_DESCRIPTOR_TYPE << 8),
                }
            };
            StructToBytes(request, buf);
            await hub.IoControlAsync(IoControl.IOCTL_USB_GET_DESCRIPTOR_FROM_NODE_CONNECTION, buf, buf);
            BytesToStruct(buf.AsSpan(Marshal.SizeOf<UsbDescriptorRequest>()), out USB_CONFIGURATION_DESCRIPTOR configuration);
            buf = new byte[Marshal.SizeOf<UsbDescriptorRequest>() + configuration.wTotalLength];
            request.SetupPacket.wLength = configuration.wTotalLength;
            StructToBytes(request, buf);
            await hub.IoControlAsync(IoControl.IOCTL_USB_GET_DESCRIPTOR_FROM_NODE_CONNECTION, buf, buf);

            var offset = Marshal.SizeOf<UsbDescriptorRequest>();
            while (offset < buf.Length)
            {
                BytesToStruct(buf.AsSpan(offset), out USB_COMMON_DESCRIPTOR common);
                if (common.bDescriptorType == PInvoke.USB_INTERFACE_DESCRIPTOR_TYPE)
                {
                    BytesToStruct(buf.AsSpan(offset), out USB_INTERFACE_DESCRIPTOR iface);
                    if (iface.bAlternateSetting == 0)
                    {
                        result.Add(new(iface.bInterfaceClass, iface.bInterfaceSubClass, iface.bInterfaceProtocol));
                    }
                }
                offset += common.bLength;
            }
            return result;
        }

        static async Task<ExportedDevice?> GetExportedDevice(ConfigurationManager.UsbDevice device, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // now query the parent USB hub for device details

            using var hubFile = new DeviceFile(device.HubInterface);
            using var cancellationTokenRegistration = cancellationToken.Register(() => hubFile.Dispose());

            var data = new UsbNodeConnectionInformationEx() { ConnectionIndex = device.BusId.Port };
            var buf = StructToBytes(data);
            await hubFile.IoControlAsync(IoControl.IOCTL_USB_GET_NODE_CONNECTION_INFORMATION_EX, buf, buf);
            BytesToStruct(buf, out data);

            var speed = MapWindowsSpeedToLinuxSpeed((USB_DEVICE_SPEED)data.Speed);

            var data2 = new UsbNodeConnectionInformationExV2()
            {
                ConnectionIndex = device.BusId.Port,
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
                Path = device.DeviceId,
                HubPath = device.HubInterface,
                BusId = device.BusId,
                Speed = speed,
                VendorId = data.DeviceDescriptor.idVendor,
                ProductId = data.DeviceDescriptor.idProduct,
                BcdDevice = data.DeviceDescriptor.bcdDevice,
                DeviceClass = data.DeviceDescriptor.bDeviceClass,
                DeviceSubClass = data.DeviceDescriptor.bDeviceSubClass,
                DeviceProtocol = data.DeviceDescriptor.bDeviceProtocol,
                ConfigurationValue = data.CurrentConfigurationValue,
                NumConfigurations = data.DeviceDescriptor.bNumConfigurations,
                Interfaces = await GetInterfacesAsync(hubFile, device.BusId.Port),
                Description = device.Description,
            };

            if (RegistryUtils.GetOriginalInstanceId(exportedDevice) is string originalInstanceId)
            {
                // If the device is currently attached, then VBoxMon will have overridden the path.
                exportedDevice.Path = originalInstanceId;
            }

            if (RegistryUtils.GetDeviceDescription(exportedDevice) is string cachedDescription)
            {
                // If the device is currently attached, then we will have overridden the description.
                exportedDevice.Description = cachedDescription;
            }

            return exportedDevice;
        }

        public static async Task<ExportedDevice[]> GetAll(CancellationToken cancellationToken)
        {
            var exportedDevices = new SortedDictionary<BusId, ExportedDevice>();
            foreach (var device in ConfigurationManager.GetUsbDevices(true))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (await GetExportedDevice(device, cancellationToken) is ExportedDevice exportedDevice)
                    {
                        exportedDevices.Add(exportedDevice.BusId, exportedDevice);
                    }
                }
                catch (Win32Exception)
                {
                    // This can happen after standby/hibernation, or when racing against unplugging the device.
                    // Simply do not report the device as present, which will force a surprise removal if the device was attached.
                    // Common errors: ERROR_NO_SUCH_DEVICE, ERROR_GEN_FAILURE, and possibly many more
                    continue;
                }
            }
            return exportedDevices.Values.ToArray();
        }
    }
}
