// SPDX-FileCopyrightText: 2022 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-2.0-only

using System;
using System.CommandLine;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using UsbIpServer;

namespace UnitTests
{
    using ExitCode = Program.ExitCode;

    [TestClass]
    sealed class Parse_server_Tests
        : ParseTestBase
    {
        [TestMethod]
        public void Success()
        {
            var mock = CreateMock();
            mock.Setup(m => m.Server(It.Is<string[]>(array => array.SequenceEqual(Array.Empty<string>())),
                It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(ExitCode.Success));

            Test(ExitCode.Success, mock, "server");
        }

        [TestMethod]
        public void SuccessWithArguments()
        {
            var testArgs = new string[]
            {
                "arg1",
                "arg2",
                "key3=value4",
                "arg5 with spaces",
            };

            var mock = CreateMock();
            mock.Setup(m => m.Server(It.Is<string[]>(array => array.SequenceEqual(testArgs)),
                It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(ExitCode.Success));

            Test(ExitCode.Success, mock, testArgs.Prepend("server").ToArray());
        }

        [TestMethod]
        public void Failure()
        {
            var mock = CreateMock();
            mock.Setup(m => m.Server(It.Is<string[]>(array => array.SequenceEqual(Array.Empty<string>())),
                It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(ExitCode.Failure));

            Test(ExitCode.Failure, mock, "server");
        }

        [TestMethod]
        public void Canceled()
        {
            var mock = CreateMock();
            mock.Setup(m => m.Server(It.Is<string[]>(array => array.SequenceEqual(Array.Empty<string>())),
                It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Throws<OperationCanceledException>();

            Test(ExitCode.Canceled, mock, "server");
        }

        [TestMethod]
        public void Help()
        {
            Test(ExitCode.Success, "server", "--help");
        }
    }
}
