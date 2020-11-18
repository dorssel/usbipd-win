// SPDX-FileCopyrightText: 2020 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-2.0-only

using Microsoft.Win32.SafeHandles;
using static UsbIpServer.Interop.WinSDK;

namespace UsbIpServer
{
    sealed class SafeDeviceInfoSetHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafeDeviceInfoSetHandle()
            : base(true)
        {
        }

        protected override bool ReleaseHandle()
        {
            return NativeMethods.SetupDiDestroyDeviceInfoList(handle);
        }
    }
}
