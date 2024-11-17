// SPDX-FileCopyrightText: 2022 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

using System.CommandLine.IO;

namespace UnitTests;

abstract class ParseTestBase
{
    internal static Mock<ICommandHandlers> CreateMock()
    {
        return new(MockBehavior.Strict);
    }

    internal static void Test(ExitCode expect, params string[] args)
    {
        Test(expect, CreateMock(), args);
    }

    internal static void Test(ExitCode expect, Mock<ICommandHandlers> mock, params string[] args)
    {
        Assert.AreEqual(MockBehavior.Strict, mock.Behavior);
        var exitCode = Program.Run(new TestConsole(), mock.Object, args);
        Assert.AreEqual(expect, exitCode);
        mock.VerifyAll();
    }
}
