// SPDX-FileCopyrightText: 2022 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

using System.CommandLine;
using Usbipd.Automation;

namespace UnitTests;

using ExitCode = Program.ExitCode;

[TestClass]
sealed class Parse_bind_Tests
    : ParseTestBase
{
    static readonly BusId TestBusId = BusId.Parse("3-42");
    static readonly VidPid TestHardwareId = VidPid.Parse("0123:cdef");

    [TestMethod]
    public void BusIdSuccess()
    {
        var mock = CreateMock();
        mock.Setup(m => m.Bind(It.Is<BusId>(busId => busId == TestBusId), false,
            It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(ExitCode.Success));

        Test(ExitCode.Success, mock, "bind", "--busid", TestBusId.ToString());
    }

    [TestMethod]
    public void BusIdForceSuccess()
    {
        var mock = CreateMock();
        mock.Setup(m => m.Bind(It.Is<BusId>(busId => busId == TestBusId), true,
            It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(ExitCode.Success));

        Test(ExitCode.Success, mock, "bind", "--busid", TestBusId.ToString(), "--force");
    }

    [TestMethod]
    public void BusIdFailure()
    {
        var mock = CreateMock();
        mock.Setup(m => m.Bind(It.Is<BusId>(busId => busId == TestBusId), false,
            It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(ExitCode.Failure));

        Test(ExitCode.Failure, mock, "bind", "--busid", TestBusId.ToString());
    }

    [TestMethod]
    public void BusIdCanceled()
    {
        var mock = CreateMock();
        mock.Setup(m => m.Bind(It.Is<BusId>(busId => busId == TestBusId), false,
            It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Throws<OperationCanceledException>();

        Test(ExitCode.Canceled, mock, "bind", "--busid", TestBusId.ToString());
    }

    [TestMethod]
    public void HardwareIdSuccess()
    {
        var mock = CreateMock();
        mock.Setup(m => m.Bind(It.Is<VidPid>(vidPid => vidPid == TestHardwareId), false,
            It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(ExitCode.Success));

        Test(ExitCode.Success, mock, "bind", "--hardware-id", TestHardwareId.ToString());
    }

    [TestMethod]
    public void HardwareIdForceSuccess()
    {
        var mock = CreateMock();
        mock.Setup(m => m.Bind(It.Is<VidPid>(vidPid => vidPid == TestHardwareId), true,
            It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(ExitCode.Success));

        Test(ExitCode.Success, mock, "bind", "--hardware-id", TestHardwareId.ToString(), "--force");
    }

    [TestMethod]
    public void HardwareIdFailure()
    {
        var mock = CreateMock();
        mock.Setup(m => m.Bind(It.Is<VidPid>(vidPid => vidPid == TestHardwareId), false,
            It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(ExitCode.Failure));

        Test(ExitCode.Failure, mock, "bind", "--hardware-id", TestHardwareId.ToString());
    }

    [TestMethod]
    public void HardwareIdCanceled()
    {
        var mock = CreateMock();
        mock.Setup(m => m.Bind(It.Is<VidPid>(vidPid => vidPid == TestHardwareId), false,
            It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Throws<OperationCanceledException>();

        Test(ExitCode.Canceled, mock, "bind", "--hardware-id", TestHardwareId.ToString());
    }

    [TestMethod]
    public void Help()
    {
        Test(ExitCode.Success, "bind", "--help");
    }

    [TestMethod]
    public void OptionMissing()
    {
        Test(ExitCode.ParseError, "bind");
    }

    [TestMethod]
    public void BusIdAndHardwareId()
    {
        Test(ExitCode.ParseError, "bind", "--busid", TestBusId.ToString(), "--hardware-id", TestHardwareId.ToString());
    }

    [TestMethod]
    public void BusIdArgumentMissing()
    {
        Test(ExitCode.ParseError, "bind", "--busid");
    }

    [TestMethod]
    public void HardwareIdArgumentMissing()
    {
        Test(ExitCode.ParseError, "bind", "--hardware-id");
    }

    [TestMethod]
    public void BusIdArgumentInvalid()
    {
        Test(ExitCode.ParseError, "bind", "--busid", "not-a-busid");
    }

    [TestMethod]
    public void HardwareIdArgumentInvalid()
    {
        Test(ExitCode.ParseError, "bind", "--hardware-id", "not-a-hardware-id");
    }

    [TestMethod]
    public void StrayArgument()
    {
        Test(ExitCode.ParseError, "bind", "--busid", TestBusId.ToString(), "stray-argument");
    }
}
