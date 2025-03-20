// SPDX-FileCopyrightText: 2021 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

#if !NETSTANDARD
using System.Text.Json.Serialization;
#endif
using System.Text.RegularExpressions;

namespace Usbipd.Automation;

#if NETSTANDARD
public
#else
[JsonConverter(typeof(JsonConverterBusId))]
#endif
readonly record struct BusId
    : IComparable<BusId>
{
#if !NETSTANDARD
    [JsonConstructor]
#endif
    public BusId(ushort bus, ushort port)
    {
        // Do not allow the explicit creation of the special IncompatibleHub value.
        // Instead, use the static IncompatibleHub field (preferable) or "default".
        // USB supports up to 127 devices, but that would require multiple hubs; the "per hub" port will never be >99.
        // And if you have more than 99 hubs on one system, then you win a prize! (but we're not going to support it...)
        if (bus is 0 or > 99)
        {
            throw new ArgumentOutOfRangeException(nameof(bus));
        }
        if (port is 0 or > 99)
        {
            throw new ArgumentOutOfRangeException(nameof(port));
        }
        (Bus, Port) = (bus, port);
    }

    public ushort Bus
    {
        get;
#if !NETSTANDARD
        // Required for MSTest DynamicData (de)serialization.
        init;
#endif
    }

    public ushort Port
    {
        get;
#if !NETSTANDARD
        // Required for MSTest DynamicData (de)serialization.
        init;
#endif
    }

    /// <summary>
    /// The special value 0-0 for hubs that do not expose a compatible busid.
    /// NOTE: This is also the "default" value for this type.
    /// </summary>
    public static readonly BusId IncompatibleHub;

#if !NETSTANDARD
    [JsonIgnore]
#endif
    public bool IsIncompatibleHub => (Bus == 0) || (Port == 0);

    public override readonly string ToString()
    {
        return IsIncompatibleHub ? nameof(IncompatibleHub) : $"{Bus}-{Port}";
    }

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
            && ushort.TryParse(match.Groups[1].Value, out var bus) && bus <= 99
            && ushort.TryParse(match.Groups[2].Value, out var port) && port <= 99)
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
        return TryParse(input, out var busId) ? busId : throw new FormatException();
    }

    #region IComparable<BusId>

    public readonly int CompareTo(BusId other)
    {
        return (((uint)Bus << 16) | Port).CompareTo(((uint)other.Bus << 16) | other.Port);
    }

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
