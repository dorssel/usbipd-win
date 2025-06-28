// SPDX-FileCopyrightText: 2022 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

namespace UnitTests;

[TestClass]
sealed class Parse_license_Tests
    : ParseTestBase
{
    [TestMethod]
    public void Success()
    {
        var mock = CreateMock();
        mock.Setup(m => m.License(
            It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(ExitCode.Success));

        Test(ExitCode.Success, mock, "license");
    }

    [TestMethod]
    public void Failure()
    {
        var mock = CreateMock();
        mock.Setup(m => m.License(
            It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(ExitCode.Failure));

        Test(ExitCode.Failure, mock, "license");
    }

    [TestMethod]
    public void Canceled()
    {
        var mock = CreateMock();
        mock.Setup(m => m.License(
            It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Throws<OperationCanceledException>();

        Test(ExitCode.Canceled, mock, "license");
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
