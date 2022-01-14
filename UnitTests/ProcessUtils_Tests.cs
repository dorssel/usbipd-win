// SPDX-FileCopyrightText: 2022 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-2.0-only

using System;
using System.ComponentModel;
using System.IO;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using UsbIpServer;

namespace UnitTests
{
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
            var result = ProcessUtils.RunCapturedProcessAsync(CompExe,
                new[] { "/M", File1, File2 },
                System.Text.Encoding.UTF8, CancellationToken.None).Result;
            Assert.AreEqual(0, result.ExitCode);
        }

        [TestMethod]
        public void RunCapturedProcessAsync_CommandNotExists()
        {
            var exception = Assert.ThrowsException<AggregateException>(() =>
            {
                ProcessUtils.RunCapturedProcessAsync(CompExe + "_does_not_exist",
                    Array.Empty<string>(), System.Text.Encoding.UTF8, CancellationToken.None).Wait();
            });
            Assert.IsInstanceOfType(exception.InnerException, typeof(Win32Exception));
        }

        [TestMethod]
        public void RunCapturedProcessAsync_CommandFailure()
        {
            var result = ProcessUtils.RunCapturedProcessAsync(CompExe,
                new[] { "/M", File1 + "_does_not_exist", File2 },
                System.Text.Encoding.UTF8, CancellationToken.None).Result;
            Assert.AreNotEqual(0, result.ExitCode);
        }

        [TestMethod]
        public void RunCapturedProcessAsync_Canceled()
        {
            using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
            var task = ProcessUtils.RunCapturedProcessAsync(CompExe,
                new[] { "/M" },
                System.Text.Encoding.UTF8, cancellationTokenSource.Token);
            var exception = Assert.ThrowsException<AggregateException>(() =>
            {
                task.Wait();
            });
            Assert.IsInstanceOfType(exception.InnerException, typeof(OperationCanceledException));
        }

        [TestMethod]
        public void RunUncapturedProcessAsync_CommandSuccess()
        {
            var result = ProcessUtils.RunUncapturedProcessAsync(CompExe,
                new[] { "/M", File1, File2 },
                CancellationToken.None).Result;
            Assert.AreEqual(0, result);
        }

        [TestMethod]
        public void RunUncapturedProcessAsync_CommandNotExists()
        {
            var exception = Assert.ThrowsException<AggregateException>(() =>
            {
                ProcessUtils.RunUncapturedProcessAsync(CompExe + "_does_not_exist",
                    Array.Empty<string>(), CancellationToken.None).Wait();
            });
            Assert.IsInstanceOfType(exception.InnerException, typeof(Win32Exception));
        }

        [TestMethod]
        public void RunUncapturedProcessAsync_CommandFailure()
        {
            var result = ProcessUtils.RunUncapturedProcessAsync(CompExe,
                new[] { "/M", File1 + "_does_not_exist", File2 },
                CancellationToken.None).Result;
            Assert.AreNotEqual(0, result);
        }

        [TestMethod]
        public void RunUncapturedProcessAsync_Canceled()
        {
            using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
            var task = ProcessUtils.RunUncapturedProcessAsync(CompExe,
                new[] { "/M" },
                cancellationTokenSource.Token);
            var exception = Assert.ThrowsException<AggregateException>(() =>
            {
                task.Wait();
            });
            Assert.IsInstanceOfType(exception.InnerException, typeof(OperationCanceledException));
        }
    }
}
