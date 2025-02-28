// SPDX-FileCopyrightText: 2022 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

namespace UnitTests;

[TestClass]
sealed class Parse_wsl_Tests
    : ParseTestBase
{
    [TestMethod]
    public void ParseError()
    {
        // "wsl" has been removed, so this is an error.
        Test(ExitCode.ParseError, "wsl");
    }

    [TestMethod]
    public void Help()
    {
        // Even --help will give a parse error, just to remind the user the command is entirely gone.
        Test(ExitCode.ParseError, "wsl", "--help");
    }
}
