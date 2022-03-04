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

[assembly: CLSCompliant(true)]
[assembly: DiscoverInternals]

namespace UnitTests
{
    using ExitCode = Program.ExitCode;

    [TestClass]
    sealed class Program_Tests
        : ParseTestBase
    {
        [TestMethod]
        public void MainSuccess()
        {
            var exitCode = (ExitCode)Program.Main(new string[] { "--version" });
            Assert.AreEqual(ExitCode.Success, exitCode);
        }

        [TestMethod]
        public void MainParseError()
        {
            var exitCode = (ExitCode)Program.Main(new string[] { "unknown-command" });
            Assert.AreEqual(ExitCode.ParseError, exitCode);
        }

        [TestMethod]
        public void RunInvalidExitCode()
        {
            var mock = CreateMock();
            mock.Setup(m => m.License(
                It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult((ExitCode)0x0badf00d));

            Assert.ThrowsException<UnexpectedResultException>(() =>
            {
                Test(ExitCode.Success, mock, "license");
            });
        }

        [TestMethod]
        public void RunException()
        {
            var mock = CreateMock();
            mock.Setup(m => m.License(
                It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Throws<NotImplementedException>();

            var exception = Assert.ThrowsException<AggregateException>(() =>
            {
                Test(ExitCode.Success, mock, "license");
            });
            Assert.IsInstanceOfType(exception.InnerException, typeof(NotImplementedException));
        }

        [TestMethod]
        public void CompletionGuard_DoesNotThrow()
        {
            var completions = Program.CompletionGuard(null!, null!);
            Assert.IsTrue(completions.SequenceEqual(Array.Empty<string>()));
        }
    }
}
