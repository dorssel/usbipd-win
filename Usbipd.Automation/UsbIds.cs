// SPDX-FileCopyrightText: 2023 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

using System.Text;

namespace Usbipd.Automation;

static class UsbIds
{
    /// <summary>
    /// We read (and cache) the file at most once per instance. And not at all if it isn't even used.
    /// </summary>
    static readonly Lazy<byte[]> Data = new(() =>
    {
#if NETSTANDARD
        // For PowerShell automation, the usb.ids file is in the assembly directory itself.
        var dataDirectory = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
#else
        // For usbipd, the usb.ids file is in the PowerShell subdirectory.
        var dataDirectory = Path.Combine(AppContext.BaseDirectory, "PowerShell");
#endif
        try
        {
            return File.ReadAllBytes(Path.Combine(dataDirectory, "usb.ids"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }, true);

    static unsafe int FindNewline(byte* utf8, int utf8Length) // DevSkim: ignore DS172412
    {
        for (var index = 0; utf8Length > 0; --utf8Length, ++index, ++utf8)
        {
            if (*utf8 == (byte)'\n')
            {
                return index;
            }
        }
        return -1;
    }

    static unsafe bool StartsWith(byte* utf8, int utf8Length, byte[] prefixBytes) // DevSkim: ignore DS172412
    {
        if (utf8Length < prefixBytes.Length)
        {
            return false;
        }

        var prefixRemainingLength = prefixBytes.Length;
        fixed (byte* prefixBytesPtr = prefixBytes)
        {
            var prefixCurrent = prefixBytesPtr;
            while (true)
            {
                if (*utf8 != *prefixCurrent)
                {
                    return false;
                }
                --prefixRemainingLength;
                if (prefixRemainingLength == 0)
                {
                    return true;
                }
                ++utf8;
                ++prefixCurrent;
            }
        }
    }

    static bool IsHexDigit(byte c)
    {
        return c is (>= (byte)'0' and <= (byte)'9') or (>= (byte)'a' and <= (byte)'f') or (>= (byte)'A' and <= (byte)'F');
    }

    /// <summary>
    /// Byte-searching through the original UTF8 is much faster than string pattern matching.
    /// </summary>
    /// <returns><see langword="null"/> if not found</returns>
    public static (string? Vendor, string? Product) GetVendorProduct(this VidPid vidPid, bool includeProduct)
    {
        unsafe // DevSkim: ignore DS172412
        {
            fixed (byte* data = Data.Value)
            {
                var utf8 = data;
                var utf8Length = Data.Value.Length;

                // Example:
                //
                // 046d  Logitech, Inc.

                var vendorStartBytes = Encoding.UTF8.GetBytes($"{vidPid.Vid:x4}  ");

                // strip off the start of the file to the start of the vendor name
                while (!StartsWith(utf8, utf8Length, vendorStartBytes))
                {
                    var nextNewline = FindNewline(utf8, utf8Length);
                    if (nextNewline == -1)
                    {
                        // Vendor not found.
                        return default;
                    }
                    utf8 += nextNewline + 1;
                    utf8Length -= nextNewline + 1;
                }
                utf8 += vendorStartBytes.Length;
                utf8Length -= vendorStartBytes.Length;

                var vendorLineEnd = FindNewline(utf8, utf8Length);
                if (vendorLineEnd == -1)
                {
                    vendorLineEnd = utf8Length;
                }
                // Trimming should not be necessary, but it can't hurt either.
                var vendor = Encoding.UTF8.GetString(utf8, vendorLineEnd).Trim();
                vendor = vendor.Trim();
                if (vendor.Length == 0)
                {
                    // Should never happen, unless usb.ids is corrupt.
                    return default;
                }

                if (!includeProduct)
                {
                    return (vendor, null);
                }

                // Example:
                //
                // <tab>0870  QuickCam Express
                //
                // We can stop if we find another vendor instead.

                var productStartBytes = Encoding.UTF8.GetBytes($"\t{vidPid.Pid:x4}  ");

                // strip off the vendor line itself
                utf8 += vendorLineEnd + 1;
                utf8Length -= vendorLineEnd + 1;
                do
                {
                    var lineEnd = FindNewline(utf8, utf8Length);
                    if (lineEnd == -1)
                    {
                        lineEnd = utf8Length;
                    }
                    if (StartsWith(utf8, utf8Length, productStartBytes))
                    {
                        // Trimming should not be necessary, but it can't hurt.
                        var product = Encoding.UTF8.GetString(utf8 + 7, lineEnd - 7).Trim();
                        if (product.Length == 0)
                        {
                            // Should never happen, unless usb.ids is corrupt.
                            return (vendor, null);
                        }
                        return (vendor, product);
                    }
                    if (lineEnd >= 4 && IsHexDigit(utf8[0]) && IsHexDigit(utf8[1]) && IsHexDigit(utf8[2]) && IsHexDigit(utf8[3]))
                    {
                        // Start of new vendor; i.e., we didn't find the product.
                        return (vendor, null);
                    }
                    // skip this line
                    utf8 += lineEnd + 1;
                    utf8Length += lineEnd + 1;
                }
                while (utf8Length > 0);

                // No more data; i.e., we didn't find the product.
                return (vendor, null);
            }
        }
    }
}
