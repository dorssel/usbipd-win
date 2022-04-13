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

namespace UnitTests;

using ExitCode = Program.ExitCode;

[TestClass]
sealed class Parse_wsl_attach_Tests
    : ParseTestBase
{
    static readonly BusId TestBusId = BusId.Parse("3-42");
    static readonly VidPid TestHardwareId = VidPid.Parse("0123:cdef");
    const string TestDistribution = "Test Distribution";
    const string TestUsbipPath = "/Test/Path/To/usbip";

    [TestMethod]
    public void BusIdSuccess()
    {
        var mock = CreateMock();
        mock.Setup(m => m.WslAttach(It.Is<BusId>(busId => busId == TestBusId), null, null,
            It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(ExitCode.Success));

        Test(ExitCode.Success, mock, "wsl", "attach", "--busid", TestBusId.ToString());
    }

    [TestMethod]
    public void BusIdSuccessWithDistribution()
    {
        var mock = CreateMock();
        mock.Setup(m => m.WslAttach(It.Is<BusId>(busId => busId == TestBusId), It.Is<string>(distribution => distribution == TestDistribution), null,
            It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(ExitCode.Success));

        Test(ExitCode.Success, mock, "wsl", "attach", "--busid", TestBusId.ToString(), "--distribution", TestDistribution);
    }

    [TestMethod]
    public void BusIdSuccessWithUsbipPath()
    {
        var mock = CreateMock();
        mock.Setup(m => m.WslAttach(It.Is<BusId>(busId => busId == TestBusId), null, It.Is<string>(usbipPath => usbipPath == TestUsbipPath),
            It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(ExitCode.Success));

        Test(ExitCode.Success, mock, "wsl", "attach", "--busid", TestBusId.ToString(), "--usbip-path", TestUsbipPath);
    }

    [TestMethod]
    public void BusIdSuccessWithDistributionAndUsbipPath()
    {
        var mock = CreateMock();
        mock.Setup(m => m.WslAttach(It.Is<BusId>(busId => busId == TestBusId), It.Is<string>(distribution => distribution == TestDistribution), It.Is<string>(usbipPath => usbipPath == TestUsbipPath),
            It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(ExitCode.Success));

        Test(ExitCode.Success, mock, "wsl", "attach", "--busid", TestBusId.ToString(), "--distribution", TestDistribution, "--usbip-path", TestUsbipPath);
    }

    [TestMethod]
    public void BusIdFailure()
    {
        var mock = CreateMock();
        mock.Setup(m => m.WslAttach(It.Is<BusId>(busId => busId == TestBusId), null, null,
            It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(ExitCode.Failure));

        Test(ExitCode.Failure, mock, "wsl", "attach", "--busid", TestBusId.ToString());
    }

    [TestMethod]
    public void BusIdCanceled()
    {
        var mock = CreateMock();
        mock.Setup(m => m.WslAttach(It.Is<BusId>(busId => busId == TestBusId), null, null,
            It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Throws<OperationCanceledException>();

        Test(ExitCode.Canceled, mock, "wsl", "attach", "--busid", TestBusId.ToString());
    }

    [TestMethod]
    public void HardwareIdSuccess()
    {
        var mock = CreateMock();
        mock.Setup(m => m.WslAttach(It.Is<VidPid>(vidPid => vidPid == TestHardwareId), null, null,
            It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(ExitCode.Success));

        Test(ExitCode.Success, mock, "wsl", "attach", "--hardware-id", TestHardwareId.ToString());
    }

    [TestMethod]
    public void HardwareIdFailure()
    {
        var mock = CreateMock();
        mock.Setup(m => m.WslAttach(It.Is<VidPid>(vidPid => vidPid == TestHardwareId), null, null,
            It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(ExitCode.Failure));

        Test(ExitCode.Failure, mock, "wsl", "attach", "--hardware-id", TestHardwareId.ToString());
    }

    [TestMethod]
    public void HardwareIdCanceled()
    {
        var mock = CreateMock();
        mock.Setup(m => m.WslAttach(It.Is<VidPid>(vidPid => vidPid == TestHardwareId), null, null,
            It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Throws<OperationCanceledException>();

        Test(ExitCode.Canceled, mock, "wsl", "attach", "--hardware-id", TestHardwareId.ToString());
    }

    [TestMethod]
    public void Help()
    {
        Test(ExitCode.Success, "wsl", "attach", "--help");
    }

    [TestMethod]
    public void OptionMissing()
    {
        Test(ExitCode.ParseError, "wsl", "attach");
    }

    [TestMethod]
    public void BusIdAndHardwareId()
    {
        Test(ExitCode.ParseError, "wsl", "attach", "--busid", TestBusId.ToString(), "--hardware-id", TestHardwareId.ToString());
    }

    [TestMethod]
    public void BusIdArgumentMissing()
    {
        Test(ExitCode.ParseError, "wsl", "attach", "--busid");
    }

    [TestMethod]
    public void HardwareIdArgumentMissing()
    {
        Test(ExitCode.ParseError, "wsl", "attach", "--hardware-id");
    }

    [TestMethod]
    public void BusIdArgumentInvalid()
    {
        Test(ExitCode.ParseError, "wsl", "attach", "--busid", "not-a-busid");
    }

    [TestMethod]
    public void HardwareIdArgumentInvalid()
    {
        Test(ExitCode.ParseError, "wsl", "attach", "--hardware-id", "not-a-hardware-id");
    }

    [TestMethod]
    public void StrayArgument()
    {
        Test(ExitCode.ParseError, "wsl", "attach", "stray-argument");
    }
}
