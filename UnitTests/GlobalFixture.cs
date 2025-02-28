// SPDX-FileCopyrightText: 2024 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

namespace UnitTests;

[TestClass]
static class GlobalFixture
{
    public static string TemporaryDirectory { get; } = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    [AssemblyInitialize]
    public static void AssemblyInitialize(TestContext context)
    {
        _ = context;
        Directory.CreateDirectory(TemporaryDirectory);
    }

    [AssemblyCleanup]
    public static void AssemblyCleanup()
    {
        try
        {
            Directory.Delete(TemporaryDirectory, true);
        }
        catch (DirectoryNotFoundException) { }
    }
}
