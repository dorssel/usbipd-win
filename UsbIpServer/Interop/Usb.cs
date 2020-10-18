using System.Runtime.InteropServices;

namespace UsbIpServer.Interop
{
    static class Usb
    {
        public enum UsbDescriptorType : byte
        {
            /// <summary>WinSDK: usbspec.h</summary>
            USB_DEVICE_DESCRIPTOR_TYPE = 1,

            /// <summary>WinSDK: usbspec.h</summary>
            USB_CONFIGURATION_DESCRIPTOR_TYPE,

            /// <summary>WinSDK: usbspec.h</summary>
            USB_STRING_DESCRIPTOR_TYPE,

            /// <summary>WinSDK: usbspec.h</summary>
            USB_INTERFACE_DESCRIPTOR_TYPE,

            /// <summary>WinSDK: usbspec.h</summary>
            USB_ENDPOINT_DESCRIPTOR_TYPE,
        }

        /// <summary>WinSDK: usbspec.h: USB_COMMON_DESCRIPTOR</summary>
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct UsbCommonDescriptor
        {
            public byte bLength;
            public UsbDescriptorType bDescriptorType;
        }

        /// <summary>WinSDK: usbspec.h: USB_DEVICE_DESCRIPTOR</summary>
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct UsbDeviceDescriptor
        {
            public UsbCommonDescriptor common;
            public ushort bcdUSB;
            public byte bDeviceClass;
            public byte bDeviceSubClass;
            public byte bDeviceProtocol;
            public byte bMaxPacketSize0;
            public ushort idVendor;
            public ushort idProduct;
            public ushort bcdDevice;
            public byte iManufacturer;
            public byte iProduct;
            public byte iSerialNumber;
            public byte bNumConfigurations;
        }

        /// <summary>WinSDK: usbspec.h: USB_CONFIGURATION_DESCRIPTOR</summary>
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct UsbConfigurationDescriptor
        {
            public UsbCommonDescriptor common;
            public ushort wTotalLength;
            public byte bNumInterfaces;
            public byte bConfigurationValue;
            public byte iConfiguration;
            public byte bmAttributes;
            public byte MaxPower;
        }

        /// <summary>WinSDK: usbspec.h: USB_INTERFACE_DESCRIPTOR</summary>
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct UsbInterfaceDescriptor
        {
            public UsbCommonDescriptor common;
            public byte bInterfaceNumber;
            public byte bAlternateSetting;
            public byte bNumEndpoints;
            public byte bInterfaceClass;
            public byte bInterfaceSubClass;
            public byte bInterfaceProtocol;
            public byte iInterface;
        }

        /// <summary>WinSDK: usbspec.h: USB_ENDPOINT_DESCRIPTOR</summary>
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct UsbEndpointDescriptor
        {
            public UsbCommonDescriptor common;
            public byte bEndpointAddress;
            public byte bmAttributes;
            public ushort wMaxPacketSize;
            public byte bInterval;
        }

        public enum UsbRequest : byte
        {
            /// <summary>WinSDK: usbspec.h: USB_REQUEST_SET_CONFIGURATION</summary>
            SET_CONFIGURATION = 0x09,
            /// <summary>WinSDK: usbspec.h: USB_REQUEST_SET_INTERFACE</summary>
            SET_INTERFACE = 0x0b,
        }

        /// <summary>WinSDK: usbspec.h: USB_DEFAULT_PIPE_SETUP_PACKET</summary>
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct UsbDefaultPipeSetupPacket
        {
            public byte bmRequestType;
            public UsbRequest bRequest;
            public ushort wValue;
            public ushort wIndex;
            public ushort wLength;
        }

        public enum UsbEndpointType : byte
        {
            /// <summary>WinSDK: usbspec.h</summary>
            USB_ENDPOINT_TYPE_CONTROL = 0,

            /// <summary>WinSDK: usbspec.h</summary>
            USB_ENDPOINT_TYPE_ISOCHRONOUS,

            /// <summary>WinSDK: usbspec.h</summary>
            USB_ENDPOINT_TYPE_BULK,

            /// <summary>WinSDK: usbspec.h</summary>
            USB_ENDPOINT_TYPE_INTERRUPT,
        }
    }
}
