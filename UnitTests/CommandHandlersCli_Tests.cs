// SPDX-FileCopyrightText: 2025 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

using Microsoft.Win32;

namespace UnitTests;

[TestClass]
[DoNotParallelize]
sealed class CommandHandlersCli_Tests
{
    static RegistryKey TestBaseKey = null!;

    const string TestBaseKeyName = @"usbipd-win-tests-can-be-removed";

    [ClassInitialize]
    public static void ClassInitialize(TestContext testContext)
    {
        _ = testContext;

        TestBaseKey = Registry.CurrentUser.CreateSubKey(TestBaseKeyName);
    }

    [ClassCleanup(ClassCleanupBehavior.EndOfClass)]
    public static void ClassCleanup()
    {
        Registry.CurrentUser.DeleteSubKeyTree(TestBaseKeyName, false);
    }

    string TestRegistryName = null!;
    RegistryKey TestRegistry = null!;

    [TestInitialize]
    public void TestInitialize()
    {
        // NOTE: None of these can be volatile, because UsbipdRegistry needs to be able to create stable subkeys in them.

        TestRegistryName = Guid.NewGuid().ToString("B");
        TestRegistry = TestBaseKey.CreateSubKey(TestRegistryName);
        using var usbipdKey = TestRegistry.CreateSubKey(@"SOFTWARE\usbipd-win");
        usbipdKey.SetValue("APPLICATIONFOLDER", AppDomain.CurrentDomain.BaseDirectory);
#pragma warning disable CS0436 // Type conflicts with imported type
        usbipdKey.SetValue("Version", GitVersionInformation.MajorMinorPatch);
#pragma warning restore CS0436 // Type conflicts with imported type
        using var devicesKey = usbipdKey.CreateSubKey("Devices");
        using var policyKey = usbipdKey.CreateSubKey("Policy");
        UsbipdRegistry.TestInstance?.Dispose();
        UsbipdRegistry.TestInstance = new(TestRegistry);
    }

    [TestCleanup]
    public void TestCleanup()
    {
        UsbipdRegistry.TestInstance?.Dispose();
        UsbipdRegistry.TestInstance = null;
        TestRegistry?.Dispose();
        TestRegistry = null!;
        TestBaseKey.DeleteSubKeyTree(TestRegistryName, false);
        TestRegistryName = null!;
    }

    public TestContext TestContext { get; set; }

    [TestMethod]
    public async Task License()
    {
        var cli = (ICommandHandlers)new CommandHandlers();
        var console = new TestConsole();

        await cli.License(console, TestContext.CancellationTokenSource.Token);
    }

    [TestMethod]
    public async Task List()
    {
        var cli = (ICommandHandlers)new CommandHandlers();
        var console = new TestConsole();

        await cli.List(false, console, TestContext.CancellationTokenSource.Token);
    }

    [TestMethod]
    public async Task List_UsbIds()
    {
        var cli = (ICommandHandlers)new CommandHandlers();
        var console = new TestConsole();

        await cli.List(true, console, TestContext.CancellationTokenSource.Token);
    }

    [TestMethod]
    public async Task State()
    {
        var cli = (ICommandHandlers)new CommandHandlers();
        var console = new TestConsole();

        await cli.State(console, TestContext.CancellationTokenSource.Token);
    }

    [TestMethod]
    public async Task PolicyList()
    {
        var cli = (ICommandHandlers)new CommandHandlers();
        var console = new TestConsole();

        await cli.PolicyList(console, TestContext.CancellationTokenSource.Token);
    }
}
