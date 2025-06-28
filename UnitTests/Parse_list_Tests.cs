// SPDX-FileCopyrightText: 2022 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

namespace UnitTests;

[TestClass]
sealed class Parse_list_Tests
    : ParseTestBase
{
    [TestMethod]
    public void Success()
    {
        var mock = CreateMock();
        mock.Setup(m => m.List(false,
            It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(ExitCode.Success));

        Test(ExitCode.Success, mock, "list");
    }

    [TestMethod]
    public void SuccessWithUsbids()
    {
        var mock = CreateMock();
        mock.Setup(m => m.List(true,
            It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(ExitCode.Success));

        Test(ExitCode.Success, mock, "list", "--usbids");
    }

    [TestMethod]
    public void Failure()
    {
        var mock = CreateMock();
        mock.Setup(m => m.List(false,
            It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(ExitCode.Failure));

        Test(ExitCode.Failure, mock, "list");
    }

    [TestMethod]
    public void Canceled()
    {
        var mock = CreateMock();
        mock.Setup(m => m.List(false,
            It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Throws<OperationCanceledException>();

        Test(ExitCode.Canceled, mock, "list");
    }

    [TestMethod]
    public void Help()
    {
        Test(ExitCode.Success, "list", "--help");
    }

    [TestMethod]
    public void StrayArgument()
    {
        Test(ExitCode.ParseError, "list", "stray-argument");
    }
}
