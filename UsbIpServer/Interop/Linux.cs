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
