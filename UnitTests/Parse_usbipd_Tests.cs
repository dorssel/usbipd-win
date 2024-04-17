// SPDX-FileCopyrightText: 2022 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

namespace UnitTests;

[TestClass]
sealed class Parse_usbipd_Tests
    : ParseTestBase
{
    [TestMethod]
    public void NoCommand()
    {
        Test(ExitCode.ParseError);
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
