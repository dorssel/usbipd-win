// SPDX-FileCopyrightText: 2022 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-2.0-only

using System;
using System.CommandLine;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Usbipd;
using Usbipd.Automation;

namespace UnitTests;

using ExitCode = Program.ExitCode;

[TestClass]
sealed class Parse_unbind_Tests
    : ParseTestBase
{
    static readonly BusId TestBusId = BusId.Parse("3-42");
    static readonly Guid TestGuid = Guid.Parse("{E863A2AF-AE47-440B-A32B-FAB1C03017AB}");
    static readonly VidPid TestHardwareId = VidPid.Parse("0123:cdef");

    [TestMethod]
    public void AllSuccess()
    {
        var mock = CreateMock();
        mock.Setup(m => m.UnbindAll(
            It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(ExitCode.Success));

        Test(ExitCode.Success, mock, "unbind", "--all");
    }

    [TestMethod]
    public void AllFailure()
    {
        var mock = CreateMock();
        mock.Setup(m => m.UnbindAll(
            It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(ExitCode.Failure));

        Test(ExitCode.Failure, mock, "unbind", "--all");
    }

    [TestMethod]
    public void AllCanceled()
    {
        var mock = CreateMock();
        mock.Setup(m => m.UnbindAll(
            It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Throws<OperationCanceledException>();

        Test(ExitCode.Canceled, mock, "unbind", "--all");
    }

    [TestMethod]
    public void BusIdSuccess()
    {
        var mock = CreateMock();
        mock.Setup(m => m.Unbind(It.Is<BusId>(busId => busId == TestBusId),
            It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(ExitCode.Success));

        Test(ExitCode.Success, mock, "unbind", "--busid", TestBusId.ToString());
    }

    [TestMethod]
    public void BusIdFailure()
    {
        var mock = CreateMock();
        mock.Setup(m => m.Unbind(It.Is<BusId>(busId => busId == TestBusId),
            It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(ExitCode.Failure));

        Test(ExitCode.Failure, mock, "unbind", "--busid", TestBusId.ToString());
    }

    [TestMethod]
    public void BusIdCanceled()
    {
        var mock = CreateMock();
        mock.Setup(m => m.Unbind(It.Is<BusId>(busId => busId == TestBusId),
            It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Throws<OperationCanceledException>();

        Test(ExitCode.Canceled, mock, "unbind", "--busid", TestBusId.ToString());
    }

    [TestMethod]
    public void GuidSuccess()
    {
        var mock = CreateMock();
        mock.Setup(m => m.Unbind(It.Is<Guid>(guid => guid == Guid.Parse("{E863A2AF-AE47-440B-A32B-FAB1C03017AB}")),
            It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(ExitCode.Success));

        Test(ExitCode.Success, mock, "unbind", "--guid", "{E863A2AF-AE47-440B-A32B-FAB1C03017AB}");
    }

    [TestMethod]
    public void GuidFailure()
    {
        var mock = CreateMock();
        mock.Setup(m => m.Unbind(It.Is<Guid>(guid => guid == Guid.Parse("{E863A2AF-AE47-440B-A32B-FAB1C03017AB}")),
            It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(ExitCode.Failure));

        Test(ExitCode.Failure, mock, "unbind", "--guid", "{E863A2AF-AE47-440B-A32B-FAB1C03017AB}");
    }

    [TestMethod]
    public void GuidCanceled()
    {
        var mock = CreateMock();
        mock.Setup(m => m.Unbind(It.Is<Guid>(guid => guid == TestGuid),
            It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Throws<OperationCanceledException>();

        Test(ExitCode.Canceled, mock, "unbind", "--guid", TestGuid.ToString());
    }

    [TestMethod]
    public void HardwareIdSuccess()
    {
        var mock = CreateMock();
        mock.Setup(m => m.Unbind(It.Is<VidPid>(vidPid => vidPid == TestHardwareId),
            It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(ExitCode.Success));

        Test(ExitCode.Success, mock, "unbind", "--hardware-id", TestHardwareId.ToString());
    }

    [TestMethod]
    public void HardwareIdFailure()
    {
        var mock = CreateMock();
        mock.Setup(m => m.Unbind(It.Is<VidPid>(vidPid => vidPid == TestHardwareId),
            It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(ExitCode.Failure));

        Test(ExitCode.Failure, mock, "unbind", "--hardware-id", TestHardwareId.ToString());
    }

    [TestMethod]
    public void HardwareIdCanceled()
    {
        var mock = CreateMock();
        mock.Setup(m => m.Unbind(It.Is<VidPid>(vidPid => vidPid == TestHardwareId),
            It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Throws<OperationCanceledException>();

        Test(ExitCode.Canceled, mock, "unbind", "--hardware-id", TestHardwareId.ToString());
    }

    [TestMethod]
    public void Help()
    {
        Test(ExitCode.Success, "unbind", "--help");
    }

    [TestMethod]
    public void OptionMissing()
    {
        Test(ExitCode.ParseError, "unbind");
    }

    [TestMethod]
    public void AllAndBusId()
    {
        Test(ExitCode.ParseError, "unbind", "--all", "--busid", TestBusId.ToString());
    }

    [TestMethod]
    public void AllAndGuid()
    {
        Test(ExitCode.ParseError, "unbind", "--all", "--guid", TestGuid.ToString());
    }

    [TestMethod]
    public void AllAndHardwareId()
    {
        Test(ExitCode.ParseError, "unbind", "--all", "--hardware-id", TestHardwareId.ToString());
    }

    [TestMethod]
    public void BusIdAndGuid()
    {
        Test(ExitCode.ParseError, "unbind", "--busid", TestBusId.ToString(), "--guid", TestGuid.ToString());
    }

    [TestMethod]
    public void BusIdAndHardwareId()
    {
        Test(ExitCode.ParseError, "unbind", "--busid", TestBusId.ToString(), "--hardware-id", TestHardwareId.ToString());
    }

    [TestMethod]
    public void GuidAndHardwareId()
    {
        Test(ExitCode.ParseError, "unbind", "--guid", TestGuid.ToString(), "--hardware-id", TestHardwareId.ToString());
    }

    [TestMethod]
    public void AllWithArgument()
    {
        Test(ExitCode.ParseError, "unbind", "--all=argument");
    }

    [TestMethod]
    public void BusIdArgumentMissing()
    {
        Test(ExitCode.ParseError, "unbind", "--busid");
    }

    [TestMethod]
    public void GuidArgumentMissing()
    {
        Test(ExitCode.ParseError, "unbind", "--guid");
    }

    [TestMethod]
    public void HardwareIdArgumentMissing()
    {
        Test(ExitCode.ParseError, "unbind", "--hardware-id");
    }

    [TestMethod]
    public void BusIdArgumentInvalid()
    {
        Test(ExitCode.ParseError, "unbind", "--busid", "not-a-busid");
    }

    [TestMethod]
    public void GuidArgumentInvalid()
    {
        Test(ExitCode.ParseError, "unbind", "--guid", "not-a-guid");
    }

    [TestMethod]
    public void HardwareIdArgumentInvalid()
    {
        Test(ExitCode.ParseError, "unbind", "--hardware-id", "not-a-hardware-id");
    }

    [TestMethod]
    public void StrayArgument()
    {
        Test(ExitCode.ParseError, "unbind", "--busid", TestBusId.ToString(), "stray-argument");
    }
}
