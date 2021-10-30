// SPDX-FileCopyrightText: 2021 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-2.0-only

using System;
using System.Text.RegularExpressions;

namespace UsbIpServer
{
    struct BusId
        : IEquatable<BusId>
        , IComparable<BusId>
    {
        public ushort Bus { get; init; }
        public ushort Port { get; init; }

        public override string ToString() => $"{Bus}-{Port}";

        public static bool TryParse(string input, out BusId busId)
        {
            // Must be 'x-y', where x and y are positive integers without leading zeros.
            var match = Regex.Match(input, "^([1-9][0-9]*)-([1-9][0-9]*)$");
            if (match.Success
                && ushort.TryParse(match.Groups[1].Value, out var bus) && bus != 0
                && ushort.TryParse(match.Groups[2].Value, out var port) && port != 0)
            {
                busId = new()
                {
                    Bus = bus,
                    Port = port,
                };
                return true;
            }
            else
            {
                busId = default;
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

        #region IEquatable<BusId>

        public override int GetHashCode() =>
            (Bus << 16) | Port;

        public override bool Equals(object? obj) =>
            obj is BusId other && Equals(other);

        public bool Equals(BusId other) =>
            Bus == other.Bus && Port == other.Port;

        public static bool operator ==(BusId a, BusId b) =>
            a.Equals(b);

        public static bool operator !=(BusId a, BusId b) =>
            !a.Equals(b);

        #endregion

        #region IComparable<BusId>

        public int CompareTo(BusId other) =>
            (Bus != other.Bus) ? (Bus - other.Bus) : (Port != other.Port) ? (Port - other.Port) : 0;

        #endregion
    }
}
