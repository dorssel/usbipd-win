// SPDX-FileCopyrightText: 2020 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-2.0-only

using System;
using Microsoft.Win32.SafeHandles;
using Windows.Win32;

namespace UsbIpServer
{
    sealed class SafeDeviceInfoSetHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafeDeviceInfoSetHandle()
            : base(true)
        {
        }

        public unsafe SafeDeviceInfoSetHandle(void *handle)
            : base(true)
        {
            this.handle = (IntPtr)handle;
        }

        public unsafe void* PInvokeHandle { get => IsInvalid ? null : (void*)handle; }

        protected override bool ReleaseHandle()
        {
            unsafe
            {
                return PInvoke.SetupDiDestroyDeviceInfoList((void*)handle);
            }
        }
    }
}
