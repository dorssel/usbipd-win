// SPDX-FileCopyrightText: 2021 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-2.0-only

using System;
using System.Threading;
using System.Threading.Tasks;

namespace UsbIpServer;

sealed class Lock : IDisposable
{
    public static Lock Create(SemaphoreSlim semaphore)
    {
        var result = new Lock(semaphore);
        semaphore.Wait();
        Interlocked.Exchange(ref result.Locked, 1);
        return result;
    }

    public static async Task<Lock> CreateAsync(SemaphoreSlim semaphore, CancellationToken cancellationToken)
    {
        var result = new Lock(semaphore);
        await semaphore.WaitAsync(cancellationToken);
        Interlocked.Exchange(ref result.Locked, 1);
        return result;
    }

    readonly SemaphoreSlim Semaphore;
    int Locked;

    Lock(SemaphoreSlim semaphore)
    {
        Semaphore = semaphore;
    }

    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref Locked, 0, 1) == 1)
        {
            Semaphore.Release();
        }
    }
}
