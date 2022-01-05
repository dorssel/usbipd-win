// SPDX-FileCopyrightText: 2022 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-2.0-only

using Microsoft.VisualStudio.TestTools.UnitTesting;
using UsbIpServer;

namespace UnitTests
{
    using ExitCode = Program.ExitCode;

    [TestClass]
    public class ParseRootCommand
        : ParseTest
    {
        [TestMethod]
        public void Success()
        {
            Test(ExitCode.Success);
        }

        [TestMethod]
        public void Help()
        {
            Test(ExitCode.Success, "--help");
        }

        [TestMethod]
        public void Version()
        {
            Test(ExitCode.Success, "--version");
        }

        [TestMethod]
        public void UnknownCommand()
        {
            Test(ExitCode.ParseError, "unknown-command");
        }
    }
}
