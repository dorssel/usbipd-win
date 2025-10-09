// SPDX-FileCopyrightText: 2022 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

namespace UnitTests;

[TestClass]
sealed class UnexpectedResultException_Tests
{
    const string TestMessage = "Some test message that must be (part of) the final message.";
    static readonly Exception TestInnerException = new NotImplementedException();

    [TestMethod]
    public void DefaultConstructor()
    {
        var unexpectedResultException = new UnexpectedResultException();
        var exception = (Exception)unexpectedResultException;
        Assert.IsNull(exception.InnerException);
    }

    [TestMethod]
    public void MessageConstructor()
    {
        var unexpectedResultException = new UnexpectedResultException(TestMessage);
        var exception = (Exception)unexpectedResultException;
        Assert.Contains(TestMessage, exception.Message);
        Assert.IsNull(exception.InnerException);
    }

    [TestMethod]
    public void MessageAndInnerConstructor()
    {
        var unexpectedResultException = new UnexpectedResultException(TestMessage, TestInnerException);
        var exception = (Exception)unexpectedResultException;
        Assert.Contains(TestMessage, exception.Message);
        Assert.AreSame(TestInnerException, exception.InnerException);
    }
}
