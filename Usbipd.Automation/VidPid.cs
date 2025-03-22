// SPDX-FileCopyrightText: 2022 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;
#if !NETSTANDARD
using System.Text.Json.Serialization;
#endif
using System.Text.RegularExpressions;

namespace Usbipd.Automation;

#if NETSTANDARD
public
#endif
readonly record struct VidPid
    : IComparable<VidPid>
{
#if !NETSTANDARD
    [JsonConstructor]
#endif
    public VidPid(ushort vid, ushort pid)
    {
        (Vid, Pid) = (vid, pid);
    }

    public ushort Vid { get; init; }
    public ushort Pid { get; init; }

    public override readonly string ToString()
    {
        return $"{Vid:x4}:{Pid:x4}";
    }

    public static bool TryParse(string input, out VidPid vidPid)
    {
        // Must be 'VID:PID', where VID and PID are exactly 4 digit hexadecimal.
        var match = Regex.Match(input, "^([0-9a-fA-F]{4}):([0-9a-fA-F]{4})$");
        if (match.Success
            && ushort.TryParse(match.Groups[1].Value, NumberStyles.AllowHexSpecifier, null, out var vid)
            && ushort.TryParse(match.Groups[2].Value, NumberStyles.AllowHexSpecifier, null, out var pid))
        {
            vidPid = new()
            {
                Vid = vid,
                Pid = pid,
            };
            return true;
        }
        else
        {
            vidPid = default;
            return false;
        }
    }

    public static VidPid Parse(string input)
    {
        return TryParse(input, out var pidVid) ? pidVid : throw new FormatException();
    }

    internal static VidPid FromHardwareOrInstanceId(string input)
    {
        // Examples:
        //   VID_80EE&PID_CAFE
        //   USB\\VID_1BCF&PID_28A6\\6&17A81E1D&0&8
        var match = Regex.Match(input, "VID_([0-9a-fA-F]{4})&PID_([0-9a-fA-F]{4})([^0-9a-fA-F]|$)");
        return match.Success
            && ushort.TryParse(match.Groups[1].Value, NumberStyles.AllowHexSpecifier, null, out var vid)
            && ushort.TryParse(match.Groups[2].Value, NumberStyles.AllowHexSpecifier, null, out var pid)
            ? new()
            {
                Vid = vid,
                Pid = pid,
            }
            : throw new FormatException();
    }

    public string? Vendor => Descriptions.Vendor;

    public string? Product => Descriptions.Product;

#if !NETSTANDARD
    public
#endif
    (string? Vendor, string? Product) Descriptions => this.GetVendorProduct(true);

    #region IComparable<VidPid>

    public readonly int CompareTo(VidPid other)
    {
        return (((uint)Vid << 16) | Pid).CompareTo(((uint)other.Vid << 16) | other.Pid);
    }

    public static bool operator <(VidPid left, VidPid right)
    {
        return left.CompareTo(right) < 0;
    }

    public static bool operator <=(VidPid left, VidPid right)
    {
        return left.CompareTo(right) <= 0;
    }

    public static bool operator >(VidPid left, VidPid right)
    {
        return left.CompareTo(right) > 0;
    }

    public static bool operator >=(VidPid left, VidPid right)
    {
        return left.CompareTo(right) >= 0;
    }

    #endregion
}
