// SPDX-FileCopyrightText: 2021 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-2.0-only

using System.Runtime.CompilerServices;
using Windows.Win32.UI.Shell.PropertiesSystem;

namespace Windows.Win32.Devices.Properties;

partial struct DEVPROPKEY
{
    /// <summary>
    /// *HACK*
    /// 
    /// CsWin32 is confused about PROPERTYKEY and DEVPROPKEY, which are in fact the exact same structure.
    /// This is an implicit c++-like "reinterpret_cast".
    /// </summary>
    public static implicit operator DEVPROPKEY(in PROPERTYKEY propertyKey)
    {
        return Unsafe.As<PROPERTYKEY, DEVPROPKEY>(ref Unsafe.AsRef(propertyKey));
    }
}
