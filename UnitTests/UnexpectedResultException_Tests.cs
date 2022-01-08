// SPDX-FileCopyrightText: 2022 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-2.0-only

using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using UsbIpServer;

namespace UnitTests
{
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
            Assert.IsTrue(exception.Message.Contains(TestMessage));
            Assert.IsNull(exception.InnerException);
        }

        [TestMethod]
        public void MessageAndInnerConstructor()
        {
            var unexpectedResultException = new UnexpectedResultException(TestMessage, TestInnerException);
            var exception = (Exception)unexpectedResultException;
            Assert.IsTrue(exception.Message.Contains(TestMessage));
            Assert.AreSame(TestInnerException, exception.InnerException);
        }
    }
}
