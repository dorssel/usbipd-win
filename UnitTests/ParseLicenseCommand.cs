// SPDX-FileCopyrightText: 2022 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-2.0-only

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
    public class ParseLicenseCommand
        : ParseTest
    {
        [TestMethod]
        public void Success()
        {
            var mock = CreateMock();
            mock.Setup(m => m.License(
                It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(true));

            Test(ExitCode.Success, mock, "license");
        }

        [TestMethod]
        public void Failed()
        {
            var mock = CreateMock();
            mock.Setup(m => m.License(
                It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(false));

            Test(ExitCode.Failure, mock, "license");
        }

        [TestMethod]
        public void Help()
        {
            Test(ExitCode.Success, "license", "--help");
        }

        [TestMethod]
        public void StrayArgument()
        {
            Test(ExitCode.ParseError, "license", "stray-argument");
        }
    }
}
