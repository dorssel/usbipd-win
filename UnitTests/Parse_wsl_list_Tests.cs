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
    sealed class Parse_wsl_list_Tests
        : ParseTestBase
    {
        [TestMethod]
        public void Success()
        {
            var mock = CreateMock();
            mock.Setup(m => m.WslList(
                It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(ExitCode.Success));

            Test(ExitCode.Success, mock, "wsl", "list");
        }

        [TestMethod]
        public void Failure()
        {
            var mock = CreateMock();
            mock.Setup(m => m.WslList(
                It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(ExitCode.Failure));

            Test(ExitCode.Failure, mock, "wsl", "list");
        }

        [TestMethod]
        public void Canceled()
        {
            var mock = CreateMock();
            mock.Setup(m => m.WslList(
                It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Throws<OperationCanceledException>();

            Test(ExitCode.Canceled, mock, "wsl", "list");
        }

        [TestMethod]
        public void Help()
        {
            Test(ExitCode.Success, "wsl", "list", "--help");
        }

        [TestMethod]
        public void StrayArgument()
        {
            Test(ExitCode.ParseError, "wsl", "list", "stray-argument");
        }
    }
}
