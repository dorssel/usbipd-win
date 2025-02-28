// SPDX-FileCopyrightText: 2022 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

using System.CommandLine;
using System.Net;
using Usbipd.Automation;

namespace UnitTests;

[TestClass]
sealed class Parse_attach_Tests
    : ParseTestBase
{
    static readonly BusId TestBusId = BusId.Parse("3-42");
    static readonly VidPid TestHardwareId = VidPid.Parse("0123:cdef");
    const string TestDistribution = "Test Distribution";

    [TestMethod]
    public void BusIdSuccess()
    {
        var mock = CreateMock();
        mock.Setup(m => m.AttachWsl(It.Is<BusId>(busId => busId == TestBusId), false, false, null, null,
            It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(ExitCode.Success));

        Test(ExitCode.Success, mock, "attach", "--wsl", "--busid", TestBusId.ToString());
    }

    [TestMethod]
    public void BusIdSuccessWithAutoAttach()
    {
        var mock = CreateMock();
        mock.Setup(m => m.AttachWsl(It.Is<BusId>(busId => busId == TestBusId), true, false, null, null,
            It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(ExitCode.Success));

        Test(ExitCode.Success, mock, "attach", "--wsl", "--busid", TestBusId.ToString(), "--auto-attach");
    }

    [TestMethod]
    public void BusIdSuccessWithAutoAttachUnplugged()
    {
        var mock = CreateMock();
        mock.Setup(m => m.AttachWsl(It.Is<BusId>(busId => busId == TestBusId), true, true, null, null,
            It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(ExitCode.Success));

        Test(ExitCode.Success, mock, "attach", "--wsl", "--busid", TestBusId.ToString(), "--auto-attach", "--unplugged");
    }

    [TestMethod]
    public void BusIdSuccessWithDistribution()
    {
        var mock = CreateMock();
        mock.Setup(m => m.AttachWsl(It.Is<BusId>(busId => busId == TestBusId), false, false,
            It.Is<string>(distribution => distribution == TestDistribution), null,
            It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(ExitCode.Success));

        Test(ExitCode.Success, mock, "attach", "--wsl", TestDistribution, "--busid", TestBusId.ToString());
    }

    [TestMethod]
    public void BusIdSuccessWithHostIp()
    {
        var mock = CreateMock();
        mock.Setup(m => m.AttachWsl(It.Is<BusId>(busId => busId == TestBusId), false, false,
            It.Is<string>(distribution => distribution == TestDistribution), It.Is<IPAddress>(address => address.ToString() == "1.2.3.4"),
            It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(ExitCode.Success));

        Test(ExitCode.Success, mock, "attach", "--wsl", TestDistribution, "--busid", TestBusId.ToString(), "--host-ip", "1.2.3.4");
    }

    [TestMethod]
    public void BusIdFailure()
    {
        var mock = CreateMock();
        mock.Setup(m => m.AttachWsl(It.Is<BusId>(busId => busId == TestBusId), false, false, null, null,
            It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(ExitCode.Failure));

        Test(ExitCode.Failure, mock, "attach", "--wsl", "--busid", TestBusId.ToString());
    }

    [TestMethod]
    public void BusIdCanceled()
    {
        var mock = CreateMock();
        mock.Setup(m => m.AttachWsl(It.Is<BusId>(busId => busId == TestBusId), false, false, null, null,
            It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Throws<OperationCanceledException>();

        Test(ExitCode.Canceled, mock, "attach", "--wsl", "--busid", TestBusId.ToString());
    }

    [TestMethod]
    public void HardwareIdSuccess()
    {
        var mock = CreateMock();
        mock.Setup(m => m.AttachWsl(It.Is<VidPid>(vidPid => vidPid == TestHardwareId), false, null, null,
            It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(ExitCode.Success));

        Test(ExitCode.Success, mock, "attach", "--wsl", "--hardware-id", TestHardwareId.ToString());
    }

    [TestMethod]
    public void HardwareIdFailure()
    {
        var mock = CreateMock();
        mock.Setup(m => m.AttachWsl(It.Is<VidPid>(vidPid => vidPid == TestHardwareId), false, null, null,
            It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(ExitCode.Failure));

        Test(ExitCode.Failure, mock, "attach", "--wsl", "--hardware-id", TestHardwareId.ToString());
    }

    [TestMethod]
    public void HardwareIdCanceled()
    {
        var mock = CreateMock();
        mock.Setup(m => m.AttachWsl(It.Is<VidPid>(vidPid => vidPid == TestHardwareId), false, null, null,
            It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Throws<OperationCanceledException>();

        Test(ExitCode.Canceled, mock, "attach", "--wsl", "--hardware-id", TestHardwareId.ToString());
    }

    [TestMethod]
    public void Help()
    {
        Test(ExitCode.Success, "attach", "--help");
    }

    [TestMethod]
    public void WslMissing()
    {
        Test(ExitCode.ParseError, "attach");
    }

    [TestMethod]
    public void DeviceMissing()
    {
        Test(ExitCode.ParseError, "attach", "--wsl");
    }

    [TestMethod]
    public void BusIdAndHardwareId()
    {
        Test(ExitCode.ParseError, "attach", "--wsl", "--busid", TestBusId.ToString(), "--hardware-id", TestHardwareId.ToString());
    }

    [TestMethod]
    public void BusIdArgumentMissing()
    {
        Test(ExitCode.ParseError, "attach", "--wsl", "--busid");
    }

    [TestMethod]
    public void HardwareIdArgumentMissing()
    {
        Test(ExitCode.ParseError, "attach", "--wsl", "--hardware-id");
    }

    [TestMethod]
    public void BusIdArgumentInvalid()
    {
        Test(ExitCode.ParseError, "attach", "--wsl", "--busid", "not-a-busid");
    }

    [TestMethod]
    public void HardwareIdArgumentInvalid()
    {
        Test(ExitCode.ParseError, "attach", "--wsl", "--hardware-id", "not-a-hardware-id");
    }

    [TestMethod]
    public void HostIpArgumentInvalid()
    {
        Test(ExitCode.ParseError, "attach", "--wsl", "--busid", TestBusId.ToString(), "--host-ip", "not-an-ip-address");
    }

    [TestMethod]
    public void UnpluggedWithoutAutoAttach()
    {
        Test(ExitCode.ParseError, "attach", "--wsl", "--busid", TestBusId.ToString(), "--unplugged");
    }

    [TestMethod]
    public void StrayArgument()
    {
        Test(ExitCode.ParseError, "attach", "stray-argument");
    }
}
