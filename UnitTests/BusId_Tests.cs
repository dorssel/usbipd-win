// SPDX-FileCopyrightText: 2022 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-2.0-only

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using UsbIpServer;

namespace UnitTests
{
    [TestClass]
    sealed class BusId_Tests
    {
        [TestMethod]
        public void DefaultConstructor()
        {
            var busId = new BusId();

            Assert.AreEqual(0, busId.Bus);
            Assert.AreEqual(0, busId.Port);
        }

        sealed class BusIdData
        {
            static readonly string[] _Invalid = new[]
            {
                "",
                "1",
                "1-",
                "-1",
                " 1-1",
                "1 -1",
                "1- 1",
                "1-1 ",
                "a-1",
                "1-a",
                "0-1",
                "1-0",
                "1-65536",
                "65536-1",
            };

            public static IEnumerable<string[]> Invalid
            {
                get => from value in _Invalid select new string[] { value };
            }

            static readonly string[] _Valid = new[]
            {
                "1-1",
                "1-2",
                "1-65534",
                "1-65535",
                "2-1",
                "2-2",
                "2-65534",
                "2-65535",
                "65534-1",
                "65534-2",
                "65534-65534",
                "65534-65535",
                "65535-1",
                "65535-2",
                "65535-65534",
                "65535-65535",
            };

            public static IEnumerable<string[]> Valid
            {
                get => from value in _Valid select new string[] { value };
            }

            static int ExpectedCompare(string left, string right)
            {
                var leftBus = ushort.Parse(left.Split('-')[0]);
                var leftPort = ushort.Parse(left.Split('-')[1]);
                var rightBus = ushort.Parse(right.Split('-')[0]);
                var rightPort = ushort.Parse(right.Split('-')[1]);

                return
                    leftBus < rightBus ? -1 :
                    leftBus > rightBus ? 1 :
                    leftPort < rightPort ? -1 :
                    leftPort > rightPort ? 1 : 0;
            }

            public static IEnumerable<object[]> Compare
            {
                get => from left in _Valid from right in _Valid select new object[] { left, right, ExpectedCompare(left, right) };
            }
        }

        [DataTestMethod]
        [DynamicData(nameof(BusIdData.Invalid), typeof(BusIdData))]
        public void TryParseInvalid(string text)
        {
            var result = BusId.TryParse(text, out var busId);
            Assert.IsFalse(result);
            Assert.AreEqual(0, busId.Bus);
            Assert.AreEqual(0, busId.Port);
        }

        [DataTestMethod]
        [DynamicData(nameof(BusIdData.Valid), typeof(BusIdData))]
        public void TryParseValid(string text)
        {
            var result = BusId.TryParse(text, out var busId);
            Assert.IsTrue(result);

            var expectedBus = ushort.Parse(text.Split('-')[0]);
            var expectedPort = ushort.Parse(text.Split('-')[1]);
            Assert.AreEqual(expectedBus, busId.Bus);
            Assert.AreEqual(expectedPort, busId.Port);
        }

        [DataTestMethod]
        [DynamicData(nameof(BusIdData.Invalid), typeof(BusIdData))]
        public void ParseInvalid(string text)
        {
            Assert.ThrowsException<FormatException>(() =>
            {
                var busId = BusId.Parse(text);
            });
        }

        [DataTestMethod]
        [DynamicData(nameof(BusIdData.Valid), typeof(BusIdData))]
        public void ParseValid(string text)
        {
            var busId = BusId.Parse(text);
            var expectedBus = ushort.Parse(text.Split('-')[0]);
            var expectedPort = ushort.Parse(text.Split('-')[1]);
            Assert.AreEqual(expectedBus, busId.Bus);
            Assert.AreEqual(expectedPort, busId.Port);
        }

        [DataTestMethod]
        [DynamicData(nameof(BusIdData.Compare), typeof(BusIdData))]
        public void Compare(string left, string right, int expected)
        {
            var result = BusId.Parse(left).CompareTo(BusId.Parse(right));
            Assert.AreEqual(expected, result);
        }
    }
}
