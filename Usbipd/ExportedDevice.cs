// SPDX-FileCopyrightText: 2020 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Usbipd.Automation;
using Usbipd.Interop;
using Windows.Win32;
using Windows.Win32.Devices.Usb;
using static Usbipd.Interop.UsbIp;
using static Usbipd.Interop.WinSDK;
using static Usbipd.Tools;

namespace Usbipd;

sealed record ExportedDevice(string InstanceId, BusId BusId, Linux.UsbDeviceSpeed Speed,
    ushort VendorId, ushort ProductId, ushort BcdDevice,
    byte DeviceClass, byte DeviceSubClass, byte DeviceProtocol,
    byte ConfigurationValue, byte NumConfigurations, List<(byte, byte, byte)> Interfaces)
{
    static void Serialize(Stream stream, string value, uint size)
    {
        var buf = new byte[size];
        _ = Encoding.UTF8.GetBytes(value, 0, value.Length, buf, 0);
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

        ushort totalConfigurationLength;
        {
            // IOCTL_USB_GET_DESCRIPTOR_FROM_NODE_CONNECTION will always get the current configuration, any index is not used.
            // This is not a problem, the result is only used informatively.

            var buffer = new byte[Unsafe.SizeOf<USB_DESCRIPTOR_REQUEST>() + Unsafe.SizeOf<USB_CONFIGURATION_DESCRIPTOR>()];
            ref var request = ref MemoryMarshal.AsRef<USB_DESCRIPTOR_REQUEST>(buffer.AsSpan(0, Unsafe.SizeOf<USB_DESCRIPTOR_REQUEST>()));
            // All other fields are ignored on input (they are implied by the IOCTL code).
            request.ConnectionIndex = connectionIndex;
            request.SetupPacket.wValue = (ushort)(PInvoke.USB_CONFIGURATION_DESCRIPTOR_TYPE << 8);
            _ = await hub.IoControlAsync(PInvoke.IOCTL_USB_GET_DESCRIPTOR_FROM_NODE_CONNECTION, buffer, buffer);
            ref var configurationDescriptor = ref MemoryMarshal.AsRef<USB_CONFIGURATION_DESCRIPTOR>(buffer.AsSpan(Unsafe.SizeOf<USB_DESCRIPTOR_REQUEST>()));
            totalConfigurationLength = configurationDescriptor.wTotalLength;
        }

        {
            var buffer = new byte[Unsafe.SizeOf<USB_DESCRIPTOR_REQUEST>() + totalConfigurationLength];
            ref var request = ref MemoryMarshal.AsRef<USB_DESCRIPTOR_REQUEST>(buffer.AsSpan(0, Unsafe.SizeOf<USB_DESCRIPTOR_REQUEST>()));
            // All other fields are ignored on input (they are implied by the IOCTL code).
            request.ConnectionIndex = connectionIndex;
            request.SetupPacket.wValue = (ushort)(PInvoke.USB_CONFIGURATION_DESCRIPTOR_TYPE << 8);
            _ = await hub.IoControlAsync(PInvoke.IOCTL_USB_GET_DESCRIPTOR_FROM_NODE_CONNECTION, buffer, buffer);

            var offset = Unsafe.SizeOf<USB_DESCRIPTOR_REQUEST>();
            while (offset < buffer.Length)
            {
                if ((buffer.Length - offset) < Unsafe.SizeOf<USB_COMMON_DESCRIPTOR>())
                {
                    // Broken configuration.
                    break;
                }
                ref var common = ref MemoryMarshal.AsRef<USB_COMMON_DESCRIPTOR>(buffer.AsSpan(offset));
                if (common.bLength < Unsafe.SizeOf<USB_COMMON_DESCRIPTOR>())
                {
                    // Broken configuration.
                    break;
                }
                if (common.bDescriptorType == PInvoke.USB_INTERFACE_DESCRIPTOR_TYPE)
                {
                    if (common.bLength < Unsafe.SizeOf<USB_INTERFACE_DESCRIPTOR>())
                    {
                        // Broken configuration.
                        break;
                    }
                    ref var interfaceDescriptor = ref MemoryMarshal.AsRef<USB_INTERFACE_DESCRIPTOR>(buffer.AsSpan(offset));
                    if (interfaceDescriptor.bAlternateSetting == 0)
                    {
                        result.Add(new(interfaceDescriptor.bInterfaceClass, interfaceDescriptor.bInterfaceSubClass, interfaceDescriptor.bInterfaceProtocol));
                    }
                }
                offset += common.bLength;
            }
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

        if (!WindowsDevice.TryCreate(device.StubInstanceId ?? device.InstanceId, out var windowsDevice))
        {
            throw new FileNotFoundException();
        }
        using var hubFile = windowsDevice.OpenHubInterface();
        using var cancellationTokenRegistration = cancellationToken.Register(hubFile.Dispose);
        try
        {
            var data = new USB_NODE_CONNECTION_INFORMATION_EX() { ConnectionIndex = device.BusId.Value.Port };
            var buf = StructToBytes(data);
            _ = await hubFile.IoControlAsync(PInvoke.IOCTL_USB_GET_NODE_CONNECTION_INFORMATION_EX, buf, buf);
            BytesToStruct(buf, out data);

            var speed = MapWindowsSpeedToLinuxSpeed((USB_DEVICE_SPEED)data.Speed);

            var data2 = new USB_NODE_CONNECTION_INFORMATION_EX_V2()
            {
                ConnectionIndex = device.BusId.Value.Port,
                Length = (uint)Unsafe.SizeOf<USB_NODE_CONNECTION_INFORMATION_EX_V2>(),
            };
            data2.SupportedUsbProtocols.Anonymous.Usb110 = true;
            data2.SupportedUsbProtocols.Anonymous.Usb200 = true;
            data2.SupportedUsbProtocols.Anonymous.Usb300 = true;
            var buf2 = StructToBytes(data2);
            _ = await hubFile.IoControlAsync(PInvoke.IOCTL_USB_GET_NODE_CONNECTION_INFORMATION_EX_V2, buf2, buf2);
            BytesToStruct(buf2, out data2);

            if (data2.SupportedUsbProtocols.Anonymous.Usb300)
            {
                if (data2.Flags.Anonymous.DeviceIsOperatingAtSuperSpeedPlusOrHigher)
                {
                    // NOTE: Linux vhci_hcd supports USB_SPEED_SUPER_PLUS since kernel version 6.12.
                    // See: https://elixir.bootlin.com/linux/v6.12/source/drivers/usb/usbip/vhci_sysfs.c#L286
                    // There is no way to figure out the remote kernel version.
                    // Looks like this only influences the reported rate; the USB protocol is supposed to be the same.
                    // So, we simply lie about the speed...

                    // speed = Linux.UsbDeviceSpeed.USB_SPEED_SUPER_PLUS;
                    speed = Linux.UsbDeviceSpeed.USB_SPEED_SUPER;
                }
                else if (data2.Flags.Anonymous.DeviceIsOperatingAtSuperSpeedOrHigher)
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
