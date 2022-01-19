// SPDX-FileCopyrightText: 2022 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-2.0-only

using Microsoft.VisualStudio.TestTools.UnitTesting;
using static UsbIpServer.Interop.VBoxUsb;

namespace UnitTests
{
    [TestClass]
    sealed class Interop_VBoxUsb_Tests
    {
        [TestMethod]
        public void GUID_CLASS_VBOXUSB_Value()
        {
            Assert.AreEqual(GUID_CLASS_VBOXUSB.ToString("B"), "{00873fdf-cafe-80ee-aa5e-00c04fb1720b}");
        }
    }
}
