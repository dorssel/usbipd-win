// SPDX-FileCopyrightText: 2023 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

using Usbipd.Automation;

namespace UnitTests;

[TestClass]
sealed class UsbIds_Tests
{
    [TestMethod]
    public void GetName_Exists()
    {
        // Vendor 0x8087 (Intel) exists
        // Product 0x8001 (Integrated Hub) exists
        var (Vendor, Product) = UsbIds.GetNames(new(0x8087, 0x8001));
        Assert.IsNotNull(Vendor);
        Assert.IsNotNull(Product);
    }

    [TestMethod]
    [DataRow((ushort)0x0000)]
    [DataRow((ushort)0xffff)]
    public void GetName_UnknownVendor(ushort vid)
    {
        var (Vendor, Product) = UsbIds.GetNames(new(vid, 0x0000));
        Assert.IsNull(Vendor);
        Assert.IsNull(Product);
    }

    [TestMethod]
    // Vendor 0x8087 (Intel) exists
    [DataRow((ushort)0x8087, (ushort)0x0000)]
    [DataRow((ushort)0x8087, (ushort)0xffff)]
    public void GetName_UnknownProduct(ushort vid, ushort pid)
    {
        var (Vendor, Product) = UsbIds.GetNames(new(vid, pid));
        Assert.IsNotNull(Vendor);
        Assert.IsNull(Product);
    }
}
