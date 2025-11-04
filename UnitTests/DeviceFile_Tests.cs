// SPDX-FileCopyrightText: 2022 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

using System.ComponentModel;
using System.Net;
using Windows.Win32;
using Windows.Win32.System.Ioctl;

namespace UnitTests;

[TestClass]
sealed class DeviceFile_Tests
{
    public TestContext TestContext { get; set; }

    [TestMethod]
    public void Constructor_Success()
    {
        using var temporaryFile = new TemporaryFile(true);
        using var deviceFile = new DeviceFile(temporaryFile.AbsolutePath);
    }

    [TestMethod]
    public void Constructor_FileNotFound()
    {
        using var temporaryFile = new TemporaryFile();
        Assert.ThrowsExactly<Win32Exception>(() =>
        {
            using var deviceFile = new DeviceFile(temporaryFile.AbsolutePath);
        });
    }

    [TestMethod]
    public void Dispose()
    {
        using var temporaryFile = new TemporaryFile(true);
        var deviceFile = new DeviceFile(temporaryFile.AbsolutePath);
        deviceFile.Dispose();
        Assert.ThrowsExactly<ObjectDisposedException>(() =>
        {
            deviceFile.DangerousGetHandle();
        });
    }

    [TestMethod]
    public void Dispose_Twice()
    {
        using var temporaryFile = new TemporaryFile(true);
        var deviceFile = new DeviceFile(temporaryFile.AbsolutePath);
        deviceFile.Dispose();
        deviceFile.Dispose();
    }

    [TestMethod]
    public void DangerousGetHandle_Success()
    {
        using var temporaryFile = new TemporaryFile(true);
        using var deviceFile = new DeviceFile(temporaryFile.AbsolutePath);
        deviceFile.DangerousGetHandle();
    }

    enum TEST_IOCTL : uint
    {
        FSCTL_QUERY_ALLOCATED_RANGES = TestPInvoke.FSCTL_QUERY_ALLOCATED_RANGES,
    }

    [TestMethod]
    public void IoControlAsync_Success()
    {
        using var temporaryFile = new TemporaryFile(true);
        using var deviceFile = new DeviceFile(temporaryFile.AbsolutePath);
        var rangeBuffer = new FILE_ALLOCATED_RANGE_BUFFER();
        byte[] outputBuffer = [];
        var result = deviceFile.IoControlAsync(TEST_IOCTL.FSCTL_QUERY_ALLOCATED_RANGES, Tools.StructToBytes(rangeBuffer), outputBuffer).Result;
        Assert.AreEqual(0u, result);
    }

    [TestMethod]
    public void IoControlAsync_null_Output()
    {
        using var temporaryFile = new TemporaryFile(true);
        using var deviceFile = new DeviceFile(temporaryFile.AbsolutePath);
        var rangeBuffer = new FILE_ALLOCATED_RANGE_BUFFER();
        var result = deviceFile.IoControlAsync(TEST_IOCTL.FSCTL_QUERY_ALLOCATED_RANGES, Tools.StructToBytes(rangeBuffer), null).Result;
        Assert.AreEqual(0u, result);
    }

    [TestMethod]
    public void IoControlAsync_ShortOutput()
    {
        using var temporaryFile = new TemporaryFile(true);
        using var deviceFile = new DeviceFile(temporaryFile.AbsolutePath);
        var rangeBuffer = new FILE_ALLOCATED_RANGE_BUFFER();
        var outputBuffer = new byte[1];
        var result = deviceFile.IoControlAsync(TEST_IOCTL.FSCTL_QUERY_ALLOCATED_RANGES, Tools.StructToBytes(rangeBuffer), outputBuffer, false).Result;
        Assert.AreEqual(0u, result);
    }

    [TestMethod]
    public void IoControlAsync_Win32Exception()
    {
        using var temporaryFile = new TemporaryFile(true);
        using var deviceFile = new DeviceFile(temporaryFile.AbsolutePath);
        Assert.ThrowsExactly<Win32Exception>(() =>
        {
            deviceFile.IoControlAsync(TEST_IOCTL.FSCTL_QUERY_ALLOCATED_RANGES, null, null)
                .Wait(TestContext.CancellationToken);
        });
    }

    [TestMethod]
    public void IoControlAsync_ProtocolViolation()
    {
        using var temporaryFile = new TemporaryFile(true);
        using var deviceFile = new DeviceFile(temporaryFile.AbsolutePath);
        var rangeBuffer = new FILE_ALLOCATED_RANGE_BUFFER();
        var outputBuffer = new byte[1];
        Assert.ThrowsExactly<ProtocolViolationException>(() =>
        {
            deviceFile.IoControlAsync(TEST_IOCTL.FSCTL_QUERY_ALLOCATED_RANGES, Tools.StructToBytes(rangeBuffer), outputBuffer)
                .Wait(TestContext.CancellationToken);
        });
    }
}
