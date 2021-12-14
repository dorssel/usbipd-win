// SPDX-FileCopyrightText: 2022 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-2.0-only

using Microsoft.VisualStudio.TestTools.UnitTesting;
using static UsbIpServer.Interop.VBoxUsbMon;

namespace UnitTests
{
    [TestClass]
    sealed class Interop_VBoxUsbMon_Tests
    {
        [TestMethod]
        public void UsbFilter_Create()
        {
            var usbFilter = UsbFilter.Create(UsbFilterType.CAPTURE);
            foreach (var field in usbFilter.aFields)
            {
                Assert.AreEqual(UsbFilterMatch.IGNORE, field.enmMatch);
            }
        }

        [TestMethod]
        public void UsbFilter_SetMatch()
        {
            var usbFilter = UsbFilter.Create(UsbFilterType.CAPTURE);
            usbFilter.SetMatch(UsbFilterIdx.DEVICE_CLASS, (UsbFilterMatch)0x1234, 0x5678);
            Assert.AreEqual((ushort)usbFilter.aFields[(int)UsbFilterIdx.DEVICE_CLASS].enmMatch, 0x1234);
            Assert.AreEqual(usbFilter.aFields[(int)UsbFilterIdx.DEVICE_CLASS].u16Value, 0x5678);
            for (var i = 0; i < usbFilter.aFields.Length; ++i)
            {
                if (i != (int)UsbFilterIdx.DEVICE_CLASS)
                {
                    Assert.AreEqual(UsbFilterMatch.IGNORE, usbFilter.aFields[i].enmMatch);
                }
            }
        }
    }
}
