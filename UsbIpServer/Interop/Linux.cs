﻿// SPDX-FileCopyrightText: 2020 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-2.0-only

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

        public enum Errno : int
        {
            SUCCESS = 0,
            /// <summary>linux: errno-base.h</summary>
            EPIPE = 32,
            /// <summary>linux: errno.h</summary>
            ETIME = 62,
            /// <summary>linux: errno.h</summary>
            EPROTO = 71,
            /// <summary>linux: errno.h</summary>
            EOVERFLOW = 75,
            /// <summary>linux: errno.h</summary>
            EILSEQ = 84,
            /// <summary>linux: errno.h</summary>
            EREMOTEIO = 121,
        }
    }
}
