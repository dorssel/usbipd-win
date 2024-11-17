// SPDX-FileCopyrightText: 2024 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

namespace UnitTests;

sealed partial class TemporaryFile : IDisposable
{
    static long Count;

    public TemporaryFile(bool create = false)
    {
        var name = Interlocked.Increment(ref Count);
        AbsolutePath = Path.Combine(GlobalFixture.TemporaryDirectory, name.ToString());
        if (create)
        {
            using var _ = File.Create(AbsolutePath);
        }
    }

    public string AbsolutePath { get; }

    public void Dispose()
    {
        File.Delete(AbsolutePath);
    }
}
