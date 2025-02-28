// SPDX-FileCopyrightText: 2022 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

using System.CommandLine;

namespace UnitTests;

[TestClass]
sealed class Program_Tests
    : ParseTestBase
{
    [TestMethod]
    public void MainSuccess()
    {
        var exitCode = (ExitCode)Program.Main("--version");
        Assert.AreEqual(ExitCode.Success, exitCode);
    }

    [TestMethod]
    public void MainParseError()
    {
        var exitCode = (ExitCode)Program.Main("unknown-command");
        Assert.AreEqual(ExitCode.ParseError, exitCode);
    }

    [TestMethod]
    public void RunInvalidExitCode()
    {
        var mock = CreateMock();
        mock.Setup(m => m.License(
            It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult((ExitCode)0x0badf00d));

        Assert.ThrowsExactly<UnexpectedResultException>(() =>
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

        var exception = Assert.ThrowsExactly<AggregateException>(() =>
        {
            Test(ExitCode.Success, mock, "license");
        });
        Assert.IsInstanceOfType<NotImplementedException>(exception.InnerException);
    }

    [TestMethod]
    public void CompletionGuard_DoesNotThrow()
    {
        var completions = Program.CompletionGuard(null!, null!);
        Assert.IsTrue(completions.SequenceEqual([]));
    }
}
