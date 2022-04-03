// SPDX-FileCopyrightText: 2022 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-2.0-only

using Microsoft.VisualStudio.TestTools.UnitTesting;
using Windows.Win32.Devices.Properties;
using Windows.Win32.UI.Shell.PropertiesSystem;

namespace UnitTests;

[TestClass]
sealed class DEVPROPKEY_Tests
{
    [TestMethod]
    public void Implicit_Cast()
    {
        PROPERTYKEY propertyKey = Windows.Win32.TestPInvoke.DEVPKEY_NAME;
        DEVPROPKEY devPropKey;

        devPropKey = propertyKey;

        Assert.AreEqual(propertyKey.fmtid, devPropKey.fmtid);
        Assert.AreEqual(propertyKey.pid, devPropKey.pid);
    }
}
