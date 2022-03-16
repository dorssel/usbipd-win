// SPDX-FileCopyrightText: 2020 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-2.0-only

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UsbIpServer.Interop;
using Windows.Win32;
using Windows.Win32.Devices.Usb;
using static UsbIpServer.Interop.UsbIp;
using static UsbIpServer.Interop.WinSDK;
using static UsbIpServer.Tools;

namespace UsbIpServer
{
    sealed partial record ExportedDevice(string InstanceId, BusId BusId, Linux.UsbDeviceSpeed Speed,
        ushort VendorId, ushort ProductId, ushort BcdDevice,
        byte DeviceClass, byte DeviceSubClass, byte DeviceProtocol,
        byte ConfigurationValue, byte NumConfigurations, List<(byte, byte, byte)> Interfaces);

    sealed partial record ExportedDevice
    {
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
            Serialize(stream, InstanceId, SYSFS_PATH_MAX);
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

        static async Task<List<(byte, byte, byte)>> GetInterfacesAsync(DeviceFile hub, ushort connectionIndex)
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

        public static async Task<ExportedDevice> GetExportedDevice(UsbDevice device, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!device.BusId.HasValue)
            {
                throw new ArgumentException("device is not connected", nameof(device));
            }

            // Query the parent USB hub for device details.

            var hubInterfacePath = ConfigurationManager.GetHubInterfacePath(device.StubInstanceId ?? device.InstanceId);
            using var hubFile = new DeviceFile(hubInterfacePath);
            using var cancellationTokenRegistration = cancellationToken.Register(() => hubFile.Dispose());
            try
            {
                var data = new UsbNodeConnectionInformationEx() { ConnectionIndex = device.BusId.Value.Port };
                var buf = StructToBytes(data);
                await hubFile.IoControlAsync(IoControl.IOCTL_USB_GET_NODE_CONNECTION_INFORMATION_EX, buf, buf);
                BytesToStruct(buf, out data);

                var speed = MapWindowsSpeedToLinuxSpeed((USB_DEVICE_SPEED)data.Speed);

                var data2 = new UsbNodeConnectionInformationExV2()
                {
                    ConnectionIndex = device.BusId.Value.Port,
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
                        // HACK: Linux vhci_hcd does not (yet) support USB_SPEED_SUPER_PLUS.
                        // See: https://elixir.bootlin.com/linux/v5.16.9/source/drivers/usb/usbip/vhci_sysfs.c#L288
                        // Looks like this only influences the reported rate; the USB protocol is supposed to be the same.
                        // So, we simply lie about the speed...

                        // speed = Linux.UsbDeviceSpeed.USB_SPEED_SUPER_PLUS;
                        speed = Linux.UsbDeviceSpeed.USB_SPEED_SUPER;
                    }
                    else if ((data2.Flags & UsbNodeConnectionInformationExV2Flags.DeviceIsOperatingAtSuperSpeedOrHigher) != 0)
                    {
                        speed = Linux.UsbDeviceSpeed.USB_SPEED_SUPER;
                    }
                }

                var interfaces = new List<(byte, byte, byte)>();
                try
                {
                    // This may or may not fail if the device is disabled.
                    // Failure is not fatal, it just means that the export data will not contain
                    // the interface list, which only makes the identification of the 
                    // device by the user a little more difficult.
                    interfaces = await GetInterfacesAsync(hubFile, device.BusId.Value.Port);
                }
                catch (Win32Exception) { }

                var exportedDevice = new ExportedDevice(
                    InstanceId: device.InstanceId,
                    BusId: device.BusId.Value,
                    Speed: speed,
                    VendorId: data.DeviceDescriptor.idVendor,
                    ProductId: data.DeviceDescriptor.idProduct,
                    BcdDevice: data.DeviceDescriptor.bcdDevice,
                    DeviceClass: data.DeviceDescriptor.bDeviceClass,
                    DeviceSubClass: data.DeviceDescriptor.bDeviceSubClass,
                    DeviceProtocol: data.DeviceDescriptor.bDeviceProtocol,
                    ConfigurationValue: data.CurrentConfigurationValue,
                    NumConfigurations: data.DeviceDescriptor.bNumConfigurations,
                    Interfaces: interfaces);

                return exportedDevice;
            }
            catch (ObjectDisposedException)
            {
                cancellationToken.ThrowIfCancellationRequested();
                throw;
            }
        }
    }
}
