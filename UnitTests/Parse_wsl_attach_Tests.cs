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
    sealed class Parse_wsl_attach_Tests
        : ParseTestBase
    {
        static readonly BusId TestBusId = BusId.Parse("3-42");
        const string TestDistribution = "Test Distribution";
        const string TestUsbipPath = "/Test/Path/To/usbip";

        [TestMethod]
        public void Success()
        {
            var mock = CreateMock();
            mock.Setup(m => m.WslAttach(It.Is<BusId>(busId => busId == TestBusId), null, null,
                It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(ExitCode.Success));

            Test(ExitCode.Success, mock, "wsl", "attach", "--busid", TestBusId.ToString());
        }

        [TestMethod]
        public void SuccessWithDistribution()
        {
            var mock = CreateMock();
            mock.Setup(m => m.WslAttach(It.Is<BusId>(busId => busId == TestBusId), It.Is<string>(distribution => distribution == TestDistribution), null,
                It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(ExitCode.Success));

            Test(ExitCode.Success, mock, "wsl", "attach", "--busid", TestBusId.ToString(), "--distribution", TestDistribution);
        }

        [TestMethod]
        public void SuccessWithUsbipPath()
        {
            var mock = CreateMock();
            mock.Setup(m => m.WslAttach(It.Is<BusId>(busId => busId == TestBusId), null, It.Is<string>(usbipPath => usbipPath == TestUsbipPath),
                It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(ExitCode.Success));

            Test(ExitCode.Success, mock, "wsl", "attach", "--busid", TestBusId.ToString(), "--usbip-path", TestUsbipPath);
        }

        [TestMethod]
        public void SuccessWithDistributionAndUsbipPath()
        {
            var mock = CreateMock();
            mock.Setup(m => m.WslAttach(It.Is<BusId>(busId => busId == TestBusId), It.Is<string>(distribution => distribution == TestDistribution), It.Is<string>(usbipPath => usbipPath == TestUsbipPath),
                It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(ExitCode.Success));

            Test(ExitCode.Success, mock, "wsl", "attach", "--busid", TestBusId.ToString(), "--distribution", TestDistribution, "--usbip-path", TestUsbipPath);
        }

        [TestMethod]
        public void Failure()
        {
            var mock = CreateMock();
            mock.Setup(m => m.WslAttach(It.Is<BusId>(busId => busId == TestBusId), null, null,
                It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(ExitCode.Failure));

            Test(ExitCode.Failure, mock, "wsl", "attach", "--busid", TestBusId.ToString());
        }

        [TestMethod]
        public void Canceled()
        {
            var mock = CreateMock();
            mock.Setup(m => m.WslAttach(It.Is<BusId>(busId => busId == TestBusId), null, null,
                It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Throws<OperationCanceledException>();

            Test(ExitCode.Canceled, mock, "wsl", "attach", "--busid", TestBusId.ToString());
        }

        [TestMethod]
        public void Help()
        {
            Test(ExitCode.Success, "wsl", "attach", "--help");
        }

        [TestMethod]
        public void BusIdOptionMissing()
        {
            Test(ExitCode.ParseError, "wsl", "attach");
        }

        [TestMethod]
        public void BusIdArgumentMissing()
        {
            Test(ExitCode.ParseError, "wsl", "attach", "--busid");
        }

        [TestMethod]
        public void BusIdArgumentInvalid()
        {
            Test(ExitCode.ParseError, "wsl", "attach", "--busid", "not-a-busid");
        }

        [TestMethod]
        public void StrayArgument()
        {
            Test(ExitCode.ParseError, "wsl", "attach", "stray-argument");
        }
    }
}
