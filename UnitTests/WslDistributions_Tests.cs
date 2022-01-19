// SPDX-FileCopyrightText: 2022 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-2.0-only

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using UsbIpServer;

namespace UnitTests
{
    [TestClass]
    sealed class WslDistributions_Tests
    {
        [TestMethod]
        public void Constructor()
        {
            var distros = new WslDistributions(Array.Empty<WslDistributions.Distribution>(), null);
            Assert.AreEqual(0, distros.Distributions.Count());
            Assert.IsNull(distros.HostAddress);
        }

        static readonly WslDistributions.Distribution TestOldDistribution = new("Old", false, 1, true, null);
        static readonly WslDistributions.Distribution TestNewDistribution1 = new("New1", true, 2, false, null);
        static readonly WslDistributions.Distribution TestNewDistribution2 = new("New2", false, 2, true, IPAddress.Parse("1.2.3.4"));

        static readonly WslDistributions TestDistributions = new(new[]
        {
            TestOldDistribution, TestNewDistribution1, TestNewDistribution2,
        }, IPAddress.Parse("1.2.3.1"));

        [TestMethod]
        public void WslPath()
        {
            var isFullyQualified = Path.IsPathFullyQualified(WslDistributions.WslPath);
            Assert.IsTrue(isFullyQualified);
        }

        [TestMethod]
        public void DefaultDistribution()
        {
            Assert.AreEqual(TestNewDistribution1, TestDistributions.DefaultDistribution);
        }

        [TestMethod]
        public void LookupByName_Success()
        {
            var distro = TestDistributions.LookupByName("New2");
            Assert.AreEqual(TestNewDistribution2, distro);
        }

        [TestMethod]
        public void LookupByName_NotFound()
        {
            var distro = TestDistributions.LookupByName("New3");
            Assert.IsNull(distro);
        }

        [TestMethod]
        public void LookupByIPAddress_Success()
        {
            var distro = TestDistributions.LookupByIPAddress(IPAddress.Parse("1.2.3.4"));
            Assert.AreEqual(TestNewDistribution2, distro);
        }

        [TestMethod]
        public void LookupByIPAddress_NotFound()
        {
            var distro = TestDistributions.LookupByIPAddress(IPAddress.Parse("1.2.3.5"));
            Assert.IsNull(distro);
        }

        class NetworkData
        {
            static readonly (string host, string client)[] SameNetworkData = new[]
            {
                ("0.0.0.0/0", "255.255.255.255"),
                ("1.2.3.4/32", "1.2.3.4"),
                ("1.2.3.4/31", "1.2.3.5"),
            };

            static readonly (string host, string client)[] DifferentNetworkData = new[]
            {
                ("0.0.0.0/1", "128.0.0.0"),
                ("1.2.3.4/32", "1.2.3.5"),
                ("1.2.3.4/24", "0::1.2.3.4"),
                ("0::1.2.3.4/24", "1.2.3.4"),
                ("0::1.2.3.4/24", "0::1.2.3.4"),
            };

            static (IPAddress address, IPAddress mask) FromCIDR(string cidr)
            {
                var cidrParts = cidr.Split('/');
                return (IPAddress.Parse(cidrParts[0]), new IPAddress(BinaryPrimitives.ReverseEndianness((uint)(-1L << (32 - int.Parse(cidrParts[1]))))));
            }

            public static IEnumerable<object[]> TestData
            {
                get
                {
                    foreach (var (host, client) in SameNetworkData)
                    {
                        var (hostAddress, hostMask) = FromCIDR(host);
                        yield return new object[] { hostAddress, hostMask, IPAddress.Parse(client), true };
                    }
                    foreach (var (host, client) in DifferentNetworkData)
                    {
                        var (hostAddress, hostMask) = FromCIDR(host);
                        yield return new object[] { hostAddress, hostMask, IPAddress.Parse(client), false };
                    }
                    yield break;
                }
            }
        }

        [TestMethod]
        [DynamicData(nameof(NetworkData.TestData), typeof(NetworkData))]
        public void IsOnSameIPv4Network(IPAddress hostAddress, IPAddress hostMask, IPAddress clientAddress, bool expected)
        {
            var result = WslDistributions.IsOnSameIPv4Network(hostAddress, hostMask, clientAddress);
            Assert.AreEqual(expected, result);
        }
    }
}
