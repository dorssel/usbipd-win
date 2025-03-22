// SPDX-FileCopyrightText: 2023 Frans van Dorsselaer
// SPDX-FileCopyrightText: .NET Foundation
//
// SPDX-License-Identifier: GPL-3.0-only

using System.Text;

namespace Usbipd.Automation;

static class UsbIds
{
#pragma warning disable CS0649 // Field is never assigned to. Used only by UnitTests.
    public static string? TestDataPath;
    public static bool TestEmptyBytePointers;
#pragma warning restore CS0649

    static byte[] ReadData(string path)
    {
        try
        {
            return File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>
    /// We read (and cache) the file at most once per instance. And not at all if it isn't even used.
    /// </summary>
    static readonly Lazy<byte[]> ProductionData = new(() =>
    {
#if NETSTANDARD
        // For PowerShell automation, the usb.ids file is in the assembly directory itself.
        var dataDirectory = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
#else
        // For usbipd, the usb.ids file is in the application base directory.
        var dataDirectory = AppContext.BaseDirectory;
#endif
        return ReadData(Path.Combine(dataDirectory, "usb.ids"));
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

    static unsafe bool StartsWith(byte* utf8, int utf8Length, byte* prefix, int prefixLength) // DevSkim: ignore DS172412
    {
        if (prefixLength == 0)
        {
            return true;
        }

        if (utf8Length < prefixLength)
        {
            return false;
        }

        while (true)
        {
            if (*utf8 != *prefix)
            {
                return false;
            }
            --prefixLength;
            if (prefixLength == 0)
            {
                return true;
            }
            ++utf8;
            ++prefix;
        }
    }

    static bool IsHexDigit(byte c)
    {
        // see https://github.com/dotnet/runtime/blob/main/src/libraries/Common/src/System/HexConverter.cs#L390

        unchecked
        {
            var i = (ulong)((uint)c - '0');
            var shift = 0b1111111111000000011111100000000000000000000000000111111000000000UL << (int)i;
            var mask = i - 64;

            return (long)(shift & mask) < 0;
        }
    }

    /// <summary>
    /// Byte-searching through the original UTF8 is much faster than string pattern matching.
    /// </summary>
    /// <returns><see langword="null"/> if not found</returns>
    public static (string? Vendor, string? Product) GetVendorProduct(this VidPid vidPid, bool includeProduct)
    {
        // Example:
        //
        // 046d  Logitech, Inc.
        var vendorPrefix = Encoding.UTF8.GetBytes($"{vidPid.Vid:x4}  ");

        // Example:
        //
        // <tab>0870  QuickCam Express
        var productPrefix = Encoding.UTF8.GetBytes($"\t{vidPid.Pid:x4}  ");

        var data = TestDataPath is null ? ProductionData.Value : ReadData(TestDataPath);
        if (TestEmptyBytePointers)
        {
            data = [];
            vendorPrefix = [];
            productPrefix = [];
        }

        unsafe // DevSkim: ignore DS172412
        {
            fixed (byte* dataPtr = data)
            fixed (byte* vendorPrefixPtr = vendorPrefix)
            fixed (byte* productPrefixPtr = productPrefix)
            {
                var utf8 = dataPtr;
                var utf8Length = data.Length;

                // strip off the start of the file to the start of the vendor name
                while (!StartsWith(utf8, utf8Length, vendorPrefixPtr, vendorPrefix.Length))
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
                utf8 += vendorPrefix.Length;
                utf8Length -= vendorPrefix.Length;

                var vendorLineEnd = FindNewline(utf8, utf8Length);
                if (vendorLineEnd == -1)
                {
                    // EOF, any last line without \n is ignored, even if it looks like a vendor line.
                    return default;
                }
                // Trimming should not be necessary, but it can't hurt either.
                var vendor = Encoding.UTF8.GetString(utf8, vendorLineEnd).Trim();
                if (vendor.Length == 0)
                {
                    // Should never happen, unless usb.ids is corrupt.
                    return default;
                }

                if (!includeProduct)
                {
                    return (vendor, null);
                }

                // strip off the vendor line itself
                utf8 += vendorLineEnd + 1;
                utf8Length -= vendorLineEnd + 1;
                while (true)
                {
                    var lineEnd = FindNewline(utf8, utf8Length);
                    if (lineEnd == -1)
                    {
                        // EOF, any last line without \n is ignored, even if it looks like a product line.
                        return (vendor, null);
                    }
                    if (StartsWith(utf8, utf8Length, productPrefixPtr, productPrefix.Length))
                    {
                        // Trimming should not be necessary, but it can't hurt.
                        var product = Encoding.UTF8.GetString(utf8 + productPrefix.Length, lineEnd - productPrefix.Length).Trim();
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
                    utf8Length -= lineEnd + 1;
                }
            }
        }
    }
}
