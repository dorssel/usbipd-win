// SPDX-FileCopyrightText: 2022 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-2.0-only

using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using UsbIpServer;

namespace UnitTests
{
    [TestClass]
    sealed class Lock_Tests
    {
        [TestMethod]
        public void Create()
        {
            using var semaphore = new SemaphoreSlim(1);
            Assert.AreEqual(1, semaphore.CurrentCount);
            using var testLock = Lock.Create(semaphore);
            Assert.AreEqual(0, semaphore.CurrentCount);
        }

        [TestMethod]
        public void CreateDelay()
        {
            using var semaphore = new SemaphoreSlim(0);
            var stopwatch = Stopwatch.StartNew();
            Task.Run(async () =>
            {
                await Task.Delay(100);
                semaphore.Release();
            });
            using var testLock = Lock.Create(semaphore);
            stopwatch.Stop();
            Assert.AreEqual(0, semaphore.CurrentCount);
            Assert.IsTrue(stopwatch.ElapsedMilliseconds >= 100);
        }

        [TestMethod]
        public void Dispose()
        {
            using var semaphore = new SemaphoreSlim(1);
            Assert.AreEqual(1, semaphore.CurrentCount);
            var testLock = Lock.Create(semaphore);
            Assert.AreEqual(0, semaphore.CurrentCount);
            testLock.Dispose();
            Assert.AreEqual(1, semaphore.CurrentCount);
        }

        [TestMethod]
        public void DisposeTwice()
        {
            using var semaphore = new SemaphoreSlim(2);
            Assert.AreEqual(2, semaphore.CurrentCount);
            var testLock = Lock.Create(semaphore);
            Assert.AreEqual(1, semaphore.CurrentCount);
            testLock.Dispose();
            Assert.AreEqual(2, semaphore.CurrentCount);
            testLock.Dispose();
            Assert.AreEqual(2, semaphore.CurrentCount);
        }

        [TestMethod]
        public async Task CreateAsync()
        {
            using var semaphore = new SemaphoreSlim(1);
            Assert.AreEqual(1, semaphore.CurrentCount);
            using var testLock = await Lock.CreateAsync(semaphore, CancellationToken.None);
            Assert.AreEqual(0, semaphore.CurrentCount);
        }

        [TestMethod]
        public async Task CreateAsyncDelay()
        {
            using var semaphore = new SemaphoreSlim(0);
            var stopwatch = Stopwatch.StartNew();
            _ = Task.Run(async () =>
            {
                await Task.Delay(100);
                semaphore.Release();
            });
            using var testLock = await Lock.CreateAsync(semaphore, CancellationToken.None);
            stopwatch.Stop();
            Assert.AreEqual(0, semaphore.CurrentCount);
            Assert.IsTrue(stopwatch.ElapsedMilliseconds >= 100);
        }

        [TestMethod]
        public void CreateAsyncCanceled()
        {
            using var semaphore = new SemaphoreSlim(0);
            var task = Lock.CreateAsync(semaphore, new CancellationToken(true));
            Assert.AreEqual(TaskStatus.Canceled, task.Status);
        }
    }
}
