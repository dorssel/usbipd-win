// SPDX-FileCopyrightText: 2022 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

using static Usbipd.Interop.VBoxUsbMon;

namespace UnitTests;

[TestClass]
sealed class Interop_VBoxUsbMon_Tests
{
    [TestMethod]
    public void UsbFilter_Create()
    {
        var usbFilter = UsbFilter.Create(UsbFilterType.CAPTURE);
        foreach (var field in usbFilter.Fields)
        {
            Assert.AreEqual(UsbFilterMatch.IGNORE, field.enmMatch);
        }
    }

    [TestMethod]
    public void UsbFilter_SetMatch()
    {
        var usbFilter = UsbFilter.Create(UsbFilterType.CAPTURE);
        usbFilter.SetMatch(UsbFilterIdx.DEVICE_CLASS, (UsbFilterMatch)0x1234, 0x5678);
        Assert.AreEqual(0x1234, (ushort)usbFilter.Fields[(int)UsbFilterIdx.DEVICE_CLASS].enmMatch);
        Assert.AreEqual(0x5678, usbFilter.Fields[(int)UsbFilterIdx.DEVICE_CLASS].u16Value);
        for (var i = 0; i < usbFilter.Fields.Length; ++i)
        {
            if (i != (int)UsbFilterIdx.DEVICE_CLASS)
            {
                Assert.AreEqual(UsbFilterMatch.IGNORE, usbFilter.Fields[i].enmMatch);
            }
        }
    }
}
