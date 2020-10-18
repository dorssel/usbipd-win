namespace UsbIpServer.Interop
{
    static class Linux
    {
        /// <summary>linux: ch9.h: usb_device_speed
        /// <para><seealso cref="WinSDK.UsbDeviceSpeed"/></para></summary>
        public enum UsbDeviceSpeed : uint
        {
            USB_SPEED_UNKNOWN = 0,
            USB_SPEED_LOW, USB_SPEED_FULL, // usb 1.1
            USB_SPEED_HIGH,                // usb 2.0
            USB_SPEED_WIRELESS,            // wireless (usb 2.5)
            USB_SPEED_SUPER,               // usb 3.0
            USB_SPEED_SUPER_PLUS,          // usb 3.1
        }
    }
}
