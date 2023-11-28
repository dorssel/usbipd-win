// SPDX-FileCopyrightText: 2023 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

namespace Usbipd.Automation;

static partial class UsbIds
{
    static string? FindString(ulong[] array, ulong value)
    {
        var index = Array.BinarySearch(array, value);
        if (index < 0)
        {
            // Negative means the bitwise complement of the first item greater.
            index = ~index;
            if (index >= array.Length)
            {
                return null;
            }
            if ((array[index] & 0xffffffff00000000) != value)
            {
                return null;
            }
        }
        var start = (int)(array[index] & 0xffffffff);
        var end = Strings.IndexOf('\0', start);
        return Strings.Substring(start, end - start);
    }

    /// <returns><see langword="null"/> if not found</returns>
    public static string? GetVendor(this VidPid vidPid)
    {
        return FindString(VendorLookup, (ulong)vidPid.Vid << 48);
    }

    /// <returns><see langword="null"/> if not found</returns>
    public static string? GetProduct(this VidPid vidPid)
    {
        return FindString(ProductLookup, ((ulong)vidPid.Vid << 48) | ((ulong)vidPid.Pid << 32));
    }
}
