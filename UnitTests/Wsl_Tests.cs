// SPDX-FileCopyrightText: 2022 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.Net;

namespace UnitTests;

[TestClass]
sealed class Wsl_Tests
{
    public static IEnumerable<(string Host, string Client)> SameNetworks = [
        ("0.0.0.0/0", "255.255.255.255"),
        ("1.2.3.4/32", "1.2.3.4"),
        ("1.2.3.4/31", "1.2.3.5"),
    ];

    public static IEnumerable<(string Host, string Client)> DifferentNetworks = [
        ("0.0.0.0/1", "128.0.0.0"),
        ("1.2.3.4/32", "1.2.3.5"),
        ("1.2.3.4/24", "0::1.2.3.4"),
        ("0::1.2.3.4/24", "1.2.3.4"),
        ("0::1.2.3.4/24", "0::1.2.3.4"),
    ];

    public static IEnumerable<(string, string, bool)> ExpectedSameNetworks =
        [.. SameNetworks.Select(pair => (pair.Host, pair.Client, true)), .. DifferentNetworks.Select(pair => (pair.Host, pair.Client, false))];

    static (IPAddress address, IPAddress mask) FromCIDR(string cidr)
    {
        var cidrParts = cidr.Split('/');
        return (IPAddress.Parse(cidrParts[0]), new IPAddress(BinaryPrimitives.ReverseEndianness(unchecked((uint)(-1L << (32 - int.Parse(cidrParts[1])))))));
    }

    [TestMethod]
    [DynamicData(nameof(ExpectedSameNetworks))]
    public void IsOnSameIPv4Network(string host, string client, bool expected)
    {
        var (hostAddress, hostMask) = FromCIDR(host);
        var clientAddress = IPAddress.Parse(client);

        var result = Wsl.IsOnSameIPv4Network(hostAddress, hostMask, clientAddress);
        Assert.AreEqual(expected, result);
    }
}
