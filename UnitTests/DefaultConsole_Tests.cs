// SPDX-FileCopyrightText: 2025 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

namespace UnitTests;

[TestClass]
sealed class DefaultConsole_Tests
{
    [TestMethod]
    public void Out()
    {
        var console = new DefaultConsole();

        Assert.AreEqual(Console.Out, console.Out);
    }

    [TestMethod]
    public void Error()
    {
        var console = new DefaultConsole();

        Assert.AreEqual(Console.Error, console.Error);
    }

    [TestMethod]
    public void IsOutputRedirected()
    {
        if (!Console.IsOutputRedirected)
        {
            Assert.Fail("Tests should always run with output redirected.");
        }

        var console = new DefaultConsole();

        Assert.IsTrue(console.IsOutputRedirected);
    }

    [TestMethod]
    public void IsErrorRedirected()
    {
        if (!Console.IsErrorRedirected)
        {
            Assert.Fail("Tests should always run with error redirected.");
        }

        var console = new DefaultConsole();

        Assert.IsTrue(console.IsErrorRedirected);
    }

    [TestMethod]
    public void WindowWidth()
    {
        if (!Console.IsOutputRedirected)
        {
            Assert.Fail("Tests should always run with output redirected.");
        }

        var console = new DefaultConsole();

        Assert.ThrowsExactly<IOException>(() => _ = console.WindowWidth);
    }

    [TestMethod]
    public void CursorLeft_Get()
    {
        if (!Console.IsOutputRedirected)
        {
            Assert.Fail("Tests should always run with output redirected.");
        }

        var console = new DefaultConsole();

        Assert.ThrowsExactly<IOException>(() => _ = console.CursorLeft);
    }

    [TestMethod]
    public void CursorLeft_Set()
    {
        if (!Console.IsOutputRedirected)
        {
            Assert.Fail("Tests should always run with output redirected.");
        }

        var console = new DefaultConsole();

        Assert.ThrowsExactly<IOException>(() => console.CursorLeft = 42);
    }

    [TestMethod]
    public void SetError()
    {
        var console = new DefaultConsole();
        var originalError = console.Error;

        using var writer = new StringWriter();
        console.SetError(writer);
        console.Error.Write("test");
        console.SetError(originalError);

        Assert.AreEqual("test", writer.ToString());
    }
}
