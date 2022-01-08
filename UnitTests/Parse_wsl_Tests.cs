// SPDX-FileCopyrightText: 2022 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-2.0-only

using Microsoft.VisualStudio.TestTools.UnitTesting;
using UsbIpServer;

namespace UnitTests
{
    using ExitCode = Program.ExitCode;

    [TestClass]
    sealed class Parse_wsl_Tests
        : ParseTestBase
    {
        [TestMethod]
        public void Success()
        {
            Test(ExitCode.Success, "wsl");
        }

        [TestMethod]
        public void Help()
        {
            Test(ExitCode.Success, "wsl", "--help");
        }

        [TestMethod]
        public void UnknownCommand()
        {
            Test(ExitCode.ParseError, "wsl", "unknown-command");
        }
    }
}
