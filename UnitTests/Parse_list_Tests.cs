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
    sealed class Parse_list_Tests
        : ParseTestBase
    {
        [TestMethod]
        public void Success()
        {
            var mock = CreateMock();
            mock.Setup(m => m.List(
                It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(ExitCode.Success));

            Test(ExitCode.Success, mock, "list");
        }

        [TestMethod]
        public void Failure()
        {
            var mock = CreateMock();
            mock.Setup(m => m.List(
                It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(ExitCode.Failure));

            Test(ExitCode.Failure, mock, "list");
        }

        [TestMethod]
        public void Canceled()
        {
            var mock = CreateMock();
            mock.Setup(m => m.List(
                It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Throws<OperationCanceledException>();

            Test(ExitCode.Canceled, mock, "list");
        }

        [TestMethod]
        public void Help()
        {
            Test(ExitCode.Success, "list", "--help");
        }

        [TestMethod]
        public void StrayArgument()
        {
            Test(ExitCode.ParseError, "list", "stray-argument");
        }
    }
}
