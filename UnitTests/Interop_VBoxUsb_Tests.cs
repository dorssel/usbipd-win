// SPDX-FileCopyrightText: 2022 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

using static Usbipd.Interop.VBoxUsb;

namespace UnitTests;

[TestClass]
sealed class Interop_VBoxUsb_Tests
{
    [TestMethod]
    public void GUID_CLASS_VBOXUSB_Value()
    {
        Assert.AreEqual("{00873fdf-cafe-80ee-aa5e-00c04fb1720b}", GUID_CLASS_VBOXUSB.ToString("B"));
    }
}
