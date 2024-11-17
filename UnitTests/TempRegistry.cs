// SPDX-FileCopyrightText: 2024 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

using Microsoft.Win32;

namespace UnitTests;

sealed partial class TempRegistry : IDisposable
{
    public string Name { get; } = Guid.NewGuid().ToString("B");
    public RegistryKey Key { get; }
    public RegistryKey Parent { get; }

    public TempRegistry(RegistryKey parent)
    {
        Parent = parent;
        Key = Parent.CreateSubKey(Name, true, RegistryOptions.Volatile);
    }

    public void Dispose()
    {
        Key.Close();
        Parent.DeleteSubKeyTree(Name, false);
    }
}
