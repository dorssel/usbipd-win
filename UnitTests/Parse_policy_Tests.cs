// SPDX-FileCopyrightText: 2022 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

using System.CommandLine;
using Usbipd.Automation;

namespace UnitTests;

[TestClass]
sealed class Parse_policy_Tests
    : ParseTestBase
{
    static readonly BusId TestBusId = BusId.Parse("3-42");
    static readonly Guid TestGuid = Guid.Parse("{E863A2AF-AE47-440B-A32B-FAB1C03017AB}");
    static readonly VidPid TestHardwareId = VidPid.Parse("0123:cdef");

    [TestMethod]
    public void Help()
    {
        Test(ExitCode.Success, "policy", "--help");
    }

    [TestMethod]
    public void CommandMissing()
    {
        Test(ExitCode.ParseError, "policy");
    }

    [TestMethod]
    public void CommandInvalid()
    {
        Test(ExitCode.ParseError, "policy", "invalid-command");
    }

    [TestMethod]
    public void OptionInvalid()
    {
        Test(ExitCode.ParseError, "policy", "--invalid-option");
    }

    [TestMethod]
    public void list_Success()
    {
        var mock = CreateMock();
        mock.Setup(m => m.PolicyList(It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(ExitCode.Success));

        Test(ExitCode.Success, mock, "policy", "list");
    }

    [TestMethod]
    public void list_Help()
    {
        Test(ExitCode.Success, "policy", "list", "--help");
    }

    [TestMethod]
    public void list_OptionInvalid()
    {
        Test(ExitCode.ParseError, "policy", "list", "--invalid-option");
    }

    [TestMethod]
    public void list_StrayArgument()
    {
        Test(ExitCode.ParseError, "policy", "list", "stray-argument");
    }

    [TestMethod]
    public void add_Success()
    {
        var mock = CreateMock();
        mock.Setup(m => m.PolicyAdd(It.IsAny<PolicyRule>(),
            It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(ExitCode.Success));

        Test(ExitCode.Success, mock, "policy", "add", "--effect", "Allow", "--operation", "AutoBind",
            "--busid", TestBusId.ToString(), "--hardware-id", TestHardwareId.ToString());
    }

    [TestMethod]
    public void remove_Success()
    {
        var mock = CreateMock();
        mock.Setup(m => m.PolicyRemove(It.IsAny<Guid>(),
            It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(ExitCode.Success));

        Test(ExitCode.Success, mock, "policy", "remove", "--guid", TestGuid.ToString());
    }
}
