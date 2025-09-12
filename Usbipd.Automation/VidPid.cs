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
            vidPid = new(vid, pid);
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

    /// <summary>
    /// Parses a HardwareId or InstanceId string to extract the VID and PID.
    /// </summary>
    /// <param name="input">A HardwareId or InstanceId.</param>
    /// <param name="vidPid">If parsing was successful, the parsed VidPid. Otherwise default.</param>
    /// <returns>true if parsing succeeded.</returns>
    internal static bool TryParseId(string input, out VidPid vidPid)
    {
        // Examples:
        //   VID_80EE&PID_CAFE
        //   Vid_80EE&Pid_CAFE
        //   USB\\VID_1BCF&PID_28A6\\6&17A81E1D&0&8
        //
        // NOTE: everyone seems to use capitals for VID and PID *except* VBoxUSB.
        var match = Regex.Match(input, "VID_([0-9a-fA-F]{4})&PID_([0-9a-fA-F]{4})([^0-9a-fA-F]|$)", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            vidPid = new(
                ushort.Parse(match.Groups[1].Value, NumberStyles.AllowHexSpecifier, null),
                ushort.Parse(match.Groups[2].Value, NumberStyles.AllowHexSpecifier, null));
            return true;
        }
        else
        {
            vidPid = default;
            return false;
        }
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
