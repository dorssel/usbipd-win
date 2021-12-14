// SPDX-FileCopyrightText: 2022 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-2.0-only

using System;
using System.CommandLine;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using UsbIpServer;

namespace UnitTests
{
    using ExitCode = Program.ExitCode;

    [TestClass]
    sealed class Parse_bind_Tests
        : ParseTestBase
    {
        static readonly BusId TestBusId = BusId.Parse("3-42");

        [TestMethod]
        public void Success()
        {
            var mock = CreateMock();
            mock.Setup(m => m.Bind(It.Is<BusId>(busId => busId == TestBusId), false,
                It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(ExitCode.Success));

            Test(ExitCode.Success, mock, "bind", "--busid", TestBusId.ToString());
        }

        [TestMethod]
        public void ForceSuccess()
        {
            var mock = CreateMock();
            mock.Setup(m => m.Bind(It.Is<BusId>(busId => busId == TestBusId), true,
                It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(ExitCode.Success));

            Test(ExitCode.Success, mock, "bind", "--busid", TestBusId.ToString(), "--force");
        }

        [TestMethod]
        public void Failure()
        {
            var mock = CreateMock();
            mock.Setup(m => m.Bind(It.Is<BusId>(busId => busId == TestBusId), false,
                It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(ExitCode.Failure));

            Test(ExitCode.Failure, mock, "bind", "--busid", TestBusId.ToString());
        }

        [TestMethod]
        public void Canceled()
        {
            var mock = CreateMock();
            mock.Setup(m => m.Bind(It.Is<BusId>(busId => busId == TestBusId), false,
                It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Throws<OperationCanceledException>();

            Test(ExitCode.Canceled, mock, "bind", "--busid", TestBusId.ToString());
        }

        [TestMethod]
        public void Help()
        {
            Test(ExitCode.Success, "bind", "--help");
        }

        [TestMethod]
        public void BusIdOptionMissing()
        {
            Test(ExitCode.ParseError, "bind");
        }

        [TestMethod]
        public void BusIdArgumentMissing()
        {
            Test(ExitCode.ParseError, "bind", "--busid");
        }

        [TestMethod]
        public void BusIdArgumentInvalid()
        {
            Test(ExitCode.ParseError, "bind", "--busid", "not-a-busid");
        }

        [TestMethod]
        public void StrayArgument()
        {
            Test(ExitCode.ParseError, "bind", "--busid", TestBusId.ToString(), "stray-argument");
        }
    }
}
