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
    sealed class Parse_wsl_detach_Tests
        : ParseTestBase
    {
        static readonly BusId TestBusId = BusId.Parse("3-42");

        [TestMethod]
        public void AllSuccess()
        {
            var mock = CreateMock();
            mock.Setup(m => m.WslDetachAll(
                It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(ExitCode.Success));

            Test(ExitCode.Success, mock, "wsl", "detach", "--all");
        }

        [TestMethod]
        public void AllFailure()
        {
            var mock = CreateMock();
            mock.Setup(m => m.WslDetachAll(
                It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(ExitCode.Failure));

            Test(ExitCode.Failure, mock, "wsl", "detach", "--all");
        }

        [TestMethod]
        public void AllCanceled()
        {
            var mock = CreateMock();
            mock.Setup(m => m.WslDetachAll(
                It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Throws<OperationCanceledException>();

            Test(ExitCode.Canceled, mock, "wsl", "detach", "--all");
        }

        [TestMethod]
        public void BusIdSuccess()
        {
            var mock = CreateMock();
            mock.Setup(m => m.WslDetach(It.Is<BusId>(busId => busId == TestBusId),
                It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(ExitCode.Success));

            Test(ExitCode.Success, mock, "wsl", "detach", "--busid", TestBusId.ToString());
        }

        [TestMethod]
        public void BusIdFailure()
        {
            var mock = CreateMock();
            mock.Setup(m => m.WslDetach(It.Is<BusId>(busId => busId == TestBusId),
                It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(ExitCode.Failure));

            Test(ExitCode.Failure, mock, "wsl", "detach", "--busid", TestBusId.ToString());
        }

        [TestMethod]
        public void BusIdCanceled()
        {
            var mock = CreateMock();
            mock.Setup(m => m.WslDetach(It.Is<BusId>(busId => busId == TestBusId),
                It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Throws<OperationCanceledException>();

            Test(ExitCode.Canceled, mock, "wsl", "detach", "--busid", TestBusId.ToString());
        }

        [TestMethod]
        public void Help()
        {
            Test(ExitCode.Success, "wsl", "detach", "--help");
        }

        [TestMethod]
        public void OptionMissing()
        {
            Test(ExitCode.ParseError, "wsl", "detach");
        }

        [TestMethod]
        public void AllAndBusId()
        {
            Test(ExitCode.ParseError, "wsl", "detach", "--all", "--busid", TestBusId.ToString());
        }

        [TestMethod]
        public void AllWithArgument()
        {
            Test(ExitCode.ParseError, "wsl", "detach", "--all=argument");
        }

        [TestMethod]
        public void BusIdArgumentMissing()
        {
            Test(ExitCode.ParseError, "wsl", "detach", "--busid");
        }

        [TestMethod]
        public void BusIdArgumentInvalid()
        {
            Test(ExitCode.ParseError, "wsl", "detach", "--busid", "not-a-busid");
        }

        [TestMethod]
        public void StrayArgument()
        {
            Test(ExitCode.ParseError, "wsl", "detach", "--busid", TestBusId.ToString(), "stray-argument");
        }
    }
}
