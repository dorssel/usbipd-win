// SPDX-FileCopyrightText: 2025 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

using Microsoft.Win32.SafeHandles;
using Windows.Win32;

namespace UnitTests;

sealed unsafe class SafeInfHandle : SafeHandleZeroOrMinusOneIsInvalid // DevSkim: ignore DS172412
{
    public SafeInfHandle(void* inf)
        : base(true)
    {
        handle = unchecked((nint)inf);
    }

    protected override bool ReleaseHandle()
    {
        TestPInvoke.SetupCloseInfFile(handle.ToPointer());
        return true;
    }

    public static implicit operator void*(SafeInfHandle inf)
    {
        return inf.DangerousGetHandle().ToPointer();
    }
}
