// SPDX-FileCopyrightText: 2022 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

using System.CommandLine;
using Usbipd.Automation;

namespace UnitTests;

[TestClass]
sealed class Parse_detach_Tests
    : ParseTestBase
{
    static readonly BusId TestBusId = BusId.Parse("3-42");
    static readonly VidPid TestHardwareId = VidPid.Parse("0123:cdef");

    [TestMethod]
    public void AllSuccess()
    {
        var mock = CreateMock();
        mock.Setup(m => m.DetachAll(
            It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(ExitCode.Success));

        Test(ExitCode.Success, mock, "detach", "--all");
    }

    [TestMethod]
    public void AllFailure()
    {
        var mock = CreateMock();
        mock.Setup(m => m.DetachAll(
            It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(ExitCode.Failure));

        Test(ExitCode.Failure, mock, "detach", "--all");
    }

    [TestMethod]
    public void AllCanceled()
    {
        var mock = CreateMock();
        mock.Setup(m => m.DetachAll(
            It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Throws<OperationCanceledException>();

        Test(ExitCode.Canceled, mock, "detach", "--all");
    }

    [TestMethod]
    public void BusIdSuccess()
    {
        var mock = CreateMock();
        mock.Setup(m => m.Detach(It.Is<BusId>(busId => busId == TestBusId),
            It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(ExitCode.Success));

        Test(ExitCode.Success, mock, "detach", "--busid", TestBusId.ToString());
    }

    [TestMethod]
    public void BusIdFailure()
    {
        var mock = CreateMock();
        mock.Setup(m => m.Detach(It.Is<BusId>(busId => busId == TestBusId),
            It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(ExitCode.Failure));

        Test(ExitCode.Failure, mock, "detach", "--busid", TestBusId.ToString());
    }

    [TestMethod]
    public void BusIdCanceled()
    {
        var mock = CreateMock();
        mock.Setup(m => m.Detach(It.Is<BusId>(busId => busId == TestBusId),
            It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Throws<OperationCanceledException>();

        Test(ExitCode.Canceled, mock, "detach", "--busid", TestBusId.ToString());
    }

    [TestMethod]
    public void HardwareIdSuccess()
    {
        var mock = CreateMock();
        mock.Setup(m => m.Detach(It.Is<VidPid>(vidPid => vidPid == TestHardwareId),
            It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(ExitCode.Success));

        Test(ExitCode.Success, mock, "detach", "--hardware-id", TestHardwareId.ToString());
    }

    [TestMethod]
    public void HardwareIdFailure()
    {
        var mock = CreateMock();
        mock.Setup(m => m.Detach(It.Is<VidPid>(vidPid => vidPid == TestHardwareId),
            It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(ExitCode.Failure));

        Test(ExitCode.Failure, mock, "detach", "--hardware-id", TestHardwareId.ToString());
    }

    [TestMethod]
    public void HardwareIdCanceled()
    {
        var mock = CreateMock();
        mock.Setup(m => m.Detach(It.Is<VidPid>(vidPid => vidPid == TestHardwareId),
            It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Throws<OperationCanceledException>();

        Test(ExitCode.Canceled, mock, "detach", "--hardware-id", TestHardwareId.ToString());
    }

    [TestMethod]
    public void Help()
    {
        Test(ExitCode.Success, "detach", "--help");
    }

    [TestMethod]
    public void OptionMissing()
    {
        Test(ExitCode.ParseError, "detach");
    }

    [TestMethod]
    public void AllAndBusId()
    {
        Test(ExitCode.ParseError, "detach", "--all", "--busid", TestBusId.ToString());
    }

    [TestMethod]
    public void AllAndHardwareId()
    {
        Test(ExitCode.ParseError, "detach", "--all", "--hardware-id", TestHardwareId.ToString());
    }

    [TestMethod]
    public void BusIdAndHardwareId()
    {
        Test(ExitCode.ParseError, "detach", "--busid", TestBusId.ToString(), "--hardware-id", TestHardwareId.ToString());
    }

    [TestMethod]
    public void AllWithArgument()
    {
        Test(ExitCode.ParseError, "detach", "--all=argument");
    }

    [TestMethod]
    public void BusIdArgumentMissing()
    {
        Test(ExitCode.ParseError, "detach", "--busid");
    }

    [TestMethod]
    public void HardwareIdArgumentMissing()
    {
        Test(ExitCode.ParseError, "detach", "--hardware-id");
    }

    [TestMethod]
    public void BusIdArgumentInvalid()
    {
        Test(ExitCode.ParseError, "detach", "--busid", "not-a-busid");
    }

    [TestMethod]
    public void HardwareIdArgumentInvalid()
    {
        Test(ExitCode.ParseError, "detach", "--hardware-id", "not-a-hardware-id");
    }

    [TestMethod]
    public void StrayArgument()
    {
        Test(ExitCode.ParseError, "detach", "--busid", TestBusId.ToString(), "stray-argument");
    }
}
