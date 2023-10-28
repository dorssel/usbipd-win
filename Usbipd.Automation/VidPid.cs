// SPDX-FileCopyrightText: 2022 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Usbipd.Automation;

public readonly record struct VidPid
    : IComparable<VidPid>
{
    [JsonConstructor]
    public VidPid(ushort vid, ushort pid) => (Vid, Pid) = (vid, pid);

    public ushort Vid { get; init; }
    public ushort Pid { get; init; }

    public override readonly string ToString() => $"{Vid:x4}:{Pid:x4}";

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
        if (!TryParse(input, out var pidVid))
        {
            throw new FormatException();
        }
        return pidVid;
    }

    public static VidPid FromHardwareOrInstanceId(string input)
    {
        // Examples:
        //   VID_80EE&PID_CAFE
        //   USB\\VID_1BCF&PID_28A6\\6&17A81E1D&0&8
        var match = Regex.Match(input, "VID_([0-9a-fA-F]{4})&PID_([0-9a-fA-F]{4})([^0-9a-fA-F]|$)");
        if (!match.Success
            || !ushort.TryParse(match.Groups[1].Value, NumberStyles.AllowHexSpecifier, null, out var vid)
            || !ushort.TryParse(match.Groups[2].Value, NumberStyles.AllowHexSpecifier, null, out var pid))
        {
            throw new FormatException();
        }
        return new()
        {
            Vid = vid,
            Pid = pid,
        };
    }

    public string? Vendor => UsbIds.GetNames(this).Vendor;

    public string? Product => UsbIds.GetNames(this).Product;

    #region IComparable<VidPid>

    public readonly int CompareTo(VidPid other) => ((uint)Vid << 16 | Pid).CompareTo((uint)other.Vid << 16 | other.Pid);

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
