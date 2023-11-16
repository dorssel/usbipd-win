// SPDX-FileCopyrightText: 2022 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

using System.ComponentModel;
using System.Text;

namespace UnitTests;

[TestClass]
sealed class ProcessUtils_Tests
{
    static readonly string CompExe = Environment.GetFolderPath(Environment.SpecialFolder.System) + @"\comp.exe";
    static readonly string File1 = Path.GetTempFileName();
    static readonly string File2 = Path.GetTempFileName();

    [ClassCleanup]
    public static void ClassCleanup()
    {
        File.Delete(File1);
        File.Delete(File2);
    }

    [TestMethod]
    public void RunCapturedProcessAsync_CommandSuccess()
    {
        var result = ProcessUtils.RunCapturedProcessAsync(CompExe, Encoding.UTF8, CancellationToken.None, "/M", File1, File2).Result;
        Assert.AreEqual(0, result.ExitCode);
    }

    [TestMethod]
    public void RunCapturedProcessAsync_CommandNotExists()
    {
        var exception = Assert.ThrowsException<AggregateException>(() =>
        {
            ProcessUtils.RunCapturedProcessAsync(CompExe + "_does_not_exist", Encoding.UTF8, CancellationToken.None).Wait();
        });
        Assert.IsInstanceOfType(exception.InnerException, typeof(Win32Exception));
    }

    [TestMethod]
    public void RunCapturedProcessAsync_CommandFailure()
    {
        var result = ProcessUtils.RunCapturedProcessAsync(CompExe, Encoding.UTF8, CancellationToken.None, "/M", File1 + "_does_not_exist", File2).Result;
        Assert.AreNotEqual(0, result.ExitCode);
    }

    [TestMethod]
    public void RunCapturedProcessAsync_Canceled()
    {
        using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var task = ProcessUtils.RunCapturedProcessAsync(CompExe, Encoding.UTF8, cancellationTokenSource.Token, "/M");
        var exception = Assert.ThrowsException<AggregateException>(task.Wait);
        Assert.IsInstanceOfType(exception.InnerException, typeof(OperationCanceledException));
    }

    [TestMethod]
    public void RunUncapturedProcessAsync_CommandSuccess()
    {
        var result = ProcessUtils.RunUncapturedProcessAsync(CompExe, CancellationToken.None,
            "/M", File1, File2).Result;
        Assert.AreEqual(0, result);
    }

    [TestMethod]
    public void RunUncapturedProcessAsync_CommandNotExists()
    {
        var exception = Assert.ThrowsException<AggregateException>(() =>
        {
            ProcessUtils.RunUncapturedProcessAsync(CompExe + "_does_not_exist", CancellationToken.None).Wait();
        });
        Assert.IsInstanceOfType(exception.InnerException, typeof(Win32Exception));
    }

    [TestMethod]
    public void RunUncapturedProcessAsync_CommandFailure()
    {
        var result = ProcessUtils.RunUncapturedProcessAsync(CompExe, CancellationToken.None, "/M", File1 + "_does_not_exist", File2).Result;
        Assert.AreNotEqual(0, result);
    }

    [TestMethod]
    public void RunUncapturedProcessAsync_Canceled()
    {
        using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var task = ProcessUtils.RunUncapturedProcessAsync(CompExe, cancellationTokenSource.Token, "/M");
        var exception = Assert.ThrowsException<AggregateException>(task.Wait);
        Assert.IsInstanceOfType(exception.InnerException, typeof(OperationCanceledException));
    }
}
