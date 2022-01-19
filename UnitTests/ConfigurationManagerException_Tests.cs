// SPDX-FileCopyrightText: 2022 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-2.0-only

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using UsbIpServer;
using Windows.Win32.Devices.DeviceAndDriverInstallation;
using Windows.Win32.Foundation;

namespace UnitTests
{
    [TestClass]
    sealed class ConfigurationManagerException_Tests
    {
        const string TestMessage = "Some test message that must be (part of) the final message.";
        static readonly Exception TestInnerException = new NotImplementedException();

        [TestMethod]
        public void DefaultConstructor()
        {
            var configurationManagerException = new ConfigurationManagerException();
            var win32Exception = (Win32Exception)configurationManagerException;
            Assert.IsNull(win32Exception.InnerException);
        }

        [TestMethod]
        public void MessageConstructor()
        {
            var configurationManagerException = new ConfigurationManagerException(TestMessage);
            var win32Exception = (Win32Exception)configurationManagerException;
            Assert.IsTrue(win32Exception.Message.Contains(TestMessage));
            Assert.IsNull(win32Exception.InnerException);
        }

        [TestMethod]
        public void MessageAndInnerConstructor()
        {
            var configurationManagerException = new ConfigurationManagerException(TestMessage, TestInnerException);
            var win32Exception = (Win32Exception)configurationManagerException;
            Assert.IsTrue(win32Exception.Message.Contains(TestMessage));
            Assert.AreSame(TestInnerException, win32Exception.InnerException);
        }

        sealed class MappingData
        {
            static readonly Dictionary<CONFIGRET, WIN32_ERROR> _KnownGood = new()
            {
                { CONFIGRET.CR_SUCCESS, WIN32_ERROR.NO_ERROR }, // no error
                { CONFIGRET.CR_ACCESS_DENIED, WIN32_ERROR.ERROR_ACCESS_DENIED }, // non-trivial error
                { CONFIGRET.CR_FAILURE, WIN32_ERROR.ERROR_CAN_NOT_COMPLETE }, // trivial error
                { CONFIGRET.CR_DEFAULT, WIN32_ERROR.ERROR_CAN_NOT_COMPLETE }, // default error
                { (CONFIGRET)0xbaadf00d, WIN32_ERROR.ERROR_CAN_NOT_COMPLETE }, // unknown error
            };

            public static IEnumerable<object[]> KnownGood
            {
                get => from value in _KnownGood select new object[] { value.Key, value.Value };
            }
        }

        [DataTestMethod]
        [DynamicData(nameof(MappingData.KnownGood), typeof(MappingData))]
        public void CodeAndMessageConstructor(CONFIGRET configRet, WIN32_ERROR win32Error)
        {
            var configurationManagerException = new ConfigurationManagerException(configRet, TestMessage);
            Assert.AreEqual(configRet, configurationManagerException.ConfigRet);
            var win32Exception = (Win32Exception)configurationManagerException;
            Assert.IsTrue(win32Exception.Message.Contains(TestMessage));
            Assert.AreEqual(win32Error, (WIN32_ERROR)win32Exception.NativeErrorCode);
        }
    }
}
