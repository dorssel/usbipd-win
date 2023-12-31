// SPDX-FileCopyrightText: 2021 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

#if !NETSTANDARD
using System.Text.Json.Serialization;
#endif
using System.Text.RegularExpressions;

namespace Usbipd.Automation;

public readonly record struct BusId
    : IComparable<BusId>
{
#if !NETSTANDARD
    [JsonConstructor]
#endif
    public BusId(ushort bus, ushort port)
    {
        // Do not allow the explicit creation of the special IncompatibleHub value.
        // Instead, use the static IncompatibleHub field (preferrable) or "default".
        if (bus == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bus));
        }
        if (port == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(port));
        }
        Bus = bus;
        Port = port;
    }

    public ushort Bus { get; }
    public ushort Port { get; }

    /// <summary>
    /// The special value 0-0 for hubs that do not expose a compatible busid.
    /// NOTE: This is also the "default" value for this type.
    /// </summary>
    public static readonly BusId IncompatibleHub;

#if !NETSTANDARD
    [JsonIgnore]
#endif
    public bool IsIncompatibleHub => (Bus == 0) || (Port == 0);

    public override readonly string ToString() => IsIncompatibleHub ? nameof(IncompatibleHub) : $"{Bus}-{Port}";

    /// <summary>
    /// NOTE: Valid inputs are x-y, where either x and y are between 1 and 65535, or both are 0.
    /// NOTE: We do not allow leading zeros on non-zero values.
    /// </summary>
    public static bool TryParse(string input, out BusId busId)
    {
        if (input == nameof(IncompatibleHub))
        {
            busId = IncompatibleHub;
            return true;
        }
        var match = Regex.Match(input, "^([1-9][0-9]*)-([1-9][0-9]*)$");
        if (match.Success
            && ushort.TryParse(match.Groups[1].Value, out var bus)
            && ushort.TryParse(match.Groups[2].Value, out var port))
        {
            busId = new(bus, port);
            return true;
        }
        else
        {
            busId = IncompatibleHub;
            return false;
        }
    }

    public static BusId Parse(string input)
    {
        if (!TryParse(input, out var busId))
        {
            throw new FormatException();
        }
        return busId;
    }

    #region IComparable<BusId>

    public readonly int CompareTo(BusId other) => ((uint)Bus << 16 | Port).CompareTo((uint)other.Bus << 16 | other.Port);

    public static bool operator <(BusId left, BusId right)
    {
        return left.CompareTo(right) < 0;
    }

    public static bool operator <=(BusId left, BusId right)
    {
        return left.CompareTo(right) <= 0;
    }

    public static bool operator >(BusId left, BusId right)
    {
        return left.CompareTo(right) > 0;
    }

    public static bool operator >=(BusId left, BusId right)
    {
        return left.CompareTo(right) >= 0;
    }

    #endregion
}
