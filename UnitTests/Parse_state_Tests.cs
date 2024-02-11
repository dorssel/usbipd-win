// SPDX-FileCopyrightText: 2022 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

using System.CommandLine;

namespace UnitTests;

[TestClass]
sealed class Parse_state_Tests
    : ParseTestBase
{
    [TestMethod]
    public void Success()
    {
        var mock = CreateMock();
        mock.Setup(m => m.State(
            It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(ExitCode.Success));

        Test(ExitCode.Success, mock, "state");
    }

    [TestMethod]
    public void Failure()
    {
        var mock = CreateMock();
        mock.Setup(m => m.State(
            It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(ExitCode.Failure));

        Test(ExitCode.Failure, mock, "state");
    }

    [TestMethod]
    public void Canceled()
    {
        var mock = CreateMock();
        mock.Setup(m => m.State(
            It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Throws<OperationCanceledException>();

        Test(ExitCode.Canceled, mock, "state");
    }

    [TestMethod]
    public void Help()
    {
        Test(ExitCode.Success, "state", "--help");
    }

    [TestMethod]
    public void StrayArgument()
    {
        Test(ExitCode.ParseError, "state", "stray-argument");
    }
}
