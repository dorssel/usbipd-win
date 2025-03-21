// SPDX-FileCopyrightText: 2022 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;
using Usbipd.Automation;

namespace UnitTests;

[TestClass]
[DoNotParallelize]
[DeploymentItem("VidPid_vendors.ids")]
[DeploymentItem("VidPid_products.ids")]
sealed class VidPid_Tests
{
    public TestContext TestContext { get; set; }

    [TestCleanup()]
    public void Cleanup()
    {
        UsbIds.TestDataPath = null;
        UsbIds.TestEmptyBytePointers = false;
    }

    [TestMethod]
    public void DefaultConstructor()
    {
        var vidPid = new VidPid();

        Assert.AreEqual(0, vidPid.Vid);
        Assert.AreEqual(0, vidPid.Pid);
    }

    [TestMethod]
    public void JsonConstructor()
    {
        const ushort testVid = 0x1234;
        const ushort testPid = 0xcdef;

        var vidPid = new VidPid(testVid, testPid);

        Assert.AreEqual(testVid, vidPid.Vid);
        Assert.AreEqual(testPid, vidPid.Pid);
    }

    sealed class VidPidData
    {
        static readonly string[] _Invalid = [
            "",
            ":",
            "000:0000",
            "0000:000",
            "00000:0000",
            "0000:00000",
            " 0000:0000",
            "0000 :0000",
            "0000: 0000",
            "0000:0000 ",
            "000g:0000",
            "0000:000g",
        ];

        public static IEnumerable<string[]> Invalid => from value in _Invalid select new string[] { value };

        static readonly string[] _Valid = [
            "0000:0000",
            "0123:0000",
            "4567:0000",
            "89ab:0000",
            "89AB:0000",
            "cdef:0000",
            "CDEF:0000",
            "0000:0123",
            "0000:4567",
            "0000:89ab",
            "0000:89AB",
            "0000:cdef",
            "0000:CDEF",
            "fFfF:FfFf",
        ];

        public static IEnumerable<string[]> Valid => from value in _Valid select new string[] { value };

        static int ExpectedCompare(string left, string right)
        {
            var leftVid = ushort.Parse(left.Split(':')[0], NumberStyles.AllowHexSpecifier);
            var leftPid = ushort.Parse(left.Split(':')[1], NumberStyles.AllowHexSpecifier);
            var rightVid = ushort.Parse(right.Split(':')[0], NumberStyles.AllowHexSpecifier);
            var rightPid = ushort.Parse(right.Split(':')[1], NumberStyles.AllowHexSpecifier);

            return
                leftVid < rightVid ? -1 :
                leftVid > rightVid ? 1 :
                leftPid < rightPid ? -1 :
                leftPid > rightPid ? 1 : 0;
        }

        public static IEnumerable<object[]> Compare
            => from left in _Valid from right in _Valid select new object[] { left, right, ExpectedCompare(left, right) };
    }

    [TestMethod]
    [DynamicData(nameof(VidPidData.Invalid), typeof(VidPidData))]
    public void TryParseInvalid(string text)
    {
        var result = VidPid.TryParse(text, out var vidPid);
        Assert.IsFalse(result);
        Assert.AreEqual(0, vidPid.Vid);
        Assert.AreEqual(0, vidPid.Pid);
    }

    [TestMethod]
    [DynamicData(nameof(VidPidData.Valid), typeof(VidPidData))]
    public void TryParseValid(string text)
    {
        var result = VidPid.TryParse(text, out var vidPid);
        Assert.IsTrue(result);

        var expectedVid = ushort.Parse(text.Split(':')[0], NumberStyles.AllowHexSpecifier);
        var expectedPid = ushort.Parse(text.Split(':')[1], NumberStyles.AllowHexSpecifier);
        Assert.AreEqual(expectedVid, vidPid.Vid);
        Assert.AreEqual(expectedPid, vidPid.Pid);
    }

    [TestMethod]
    [DynamicData(nameof(VidPidData.Invalid), typeof(VidPidData))]
    public void ParseInvalid(string text)
    {
        Assert.ThrowsExactly<FormatException>(() =>
        {
            var vidPid = VidPid.Parse(text);
        });
    }

    [TestMethod]
    [DynamicData(nameof(VidPidData.Valid), typeof(VidPidData))]
    public void ParseValid(string text)
    {
        var vidPid = VidPid.Parse(text);
        var expectedVid = ushort.Parse(text.Split(':')[0], NumberStyles.AllowHexSpecifier);
        var expectedPid = ushort.Parse(text.Split(':')[1], NumberStyles.AllowHexSpecifier);
        Assert.AreEqual(expectedVid, vidPid.Vid);
        Assert.AreEqual(expectedPid, vidPid.Pid);
    }

    [TestMethod]
    [DynamicData(nameof(VidPidData.Compare), typeof(VidPidData))]
    public void Compare(string left, string right, int expected)
    {
        var result = VidPid.Parse(left).CompareTo(VidPid.Parse(right));
        Assert.AreEqual(expected, result);
    }

    [TestMethod]
    [DynamicData(nameof(VidPidData.Compare), typeof(VidPidData))]
    public void LessThan(string left, string right, int expected)
    {
        var result = VidPid.Parse(left) < VidPid.Parse(right);
        Assert.AreEqual(expected < 0, result);
    }

    [TestMethod]
    [DynamicData(nameof(VidPidData.Compare), typeof(VidPidData))]
    public void LessThanOrEqual(string left, string right, int expected)
    {
        var result = VidPid.Parse(left) <= VidPid.Parse(right);
        Assert.AreEqual(expected <= 0, result);
    }

    [TestMethod]
    [DynamicData(nameof(VidPidData.Compare), typeof(VidPidData))]
    public void GreaterThan(string left, string right, int expected)
    {
        var result = VidPid.Parse(left) > VidPid.Parse(right);
        Assert.AreEqual(expected > 0, result);
    }

    [TestMethod]
    [DynamicData(nameof(VidPidData.Compare), typeof(VidPidData))]
    public void GreaterThanOrEqual(string left, string right, int expected)
    {
        var result = VidPid.Parse(left) >= VidPid.Parse(right);
        Assert.AreEqual(expected >= 0, result);
    }

    [TestMethod]
    [DynamicData(nameof(VidPidData.Valid), typeof(VidPidData))]
    public void ToStringValid(string text)
    {
        var vidPid = VidPid.Parse(text);
        var expected = text.ToLowerInvariant();
        var result = vidPid.ToString();
        Assert.AreEqual(expected, result);
    }

    [TestMethod]
    [DataRow("8087:0000")] // vendor Intel, product unknown
    [DataRow("8087:8001")] // vendor Intel, product Hub
    [DataRow("8087:ffff")] // vendor Intel, product unknown
    public void VendorKnown(string text)
    {
        var vidPid = VidPid.Parse(text);
        Assert.IsNotNull(vidPid.Vendor);
    }

    [TestMethod]
    [DataRow("0000:0000")] // vendor unknown, product irrelevant
    [DataRow("0000:8001")] // vendor unknown, product irrelevant
    [DataRow("0000:ffff")] // vendor unknown, product irrelevant
    [DataRow("ffff:0000")] // vendor unknown, product irrelevant
    [DataRow("ffff:8001")] // vendor unknown, product irrelevant
    [DataRow("ffff:ffff")] // vendor unknown, product irrelevant
    public void VendorUnknown(string text)
    {
        var vidPid = VidPid.Parse(text);
        Assert.IsNull(vidPid.Vendor);
    }

    [TestMethod]
    [DataRow("8087:8001")] // vendor Intel, product Hub
    public void ProductKnown(string text)
    {
        var vidPid = VidPid.Parse(text);
        Assert.IsNotNull(vidPid.Product);
    }

    [TestMethod]
    [DataRow("0000:0000")] // vendor unknown, product irrelevant
    [DataRow("0000:8001")] // vendor unknown, product irrelevant
    [DataRow("0000:ffff")] // vendor unknown, product irrelevant
    [DataRow("05c6:8001")] // vendor known (Qualcomm), product unknown (but valid for Intel)
    [DataRow("8087:0000")] // vendor Intel, product unknown
    [DataRow("8087:ffff")] // vendor Intel, product unknown
    [DataRow("ffff:0000")] // vendor unknown, product irrelevant
    [DataRow("ffff:8001")] // vendor unknown, product irrelevant
    [DataRow("ffff:ffff")] // vendor unknown, product irrelevant
    public void ProductUnknown(string text)
    {
        var vidPid = VidPid.Parse(text);
        Assert.IsNull(vidPid.Product);
    }

    sealed class HardwareIdData
    {
        static readonly string[] _Invalid = [
            "",
            "VID_&PID_",
            "VID_000&PID_0000",
            "VID_0000&PID_000",
            "VID_00000&PID_0000",
            "VID_0000&PID_00000",
            "VID_0000 &PID_0000",
            "VID_0000& PID_0000",
            "VID_000g&PID_0000",
            "VID_0000&PID_000g",
            "ID_0000&PID_0000",
            "0000&0000",
            "VID_0000:PID_0000",
            "0000:0000",
        ];

        public static IEnumerable<string[]> Invalid => from value in _Invalid select new string[] { value };

        static readonly string[] _Valid = [
            "VID_0000&PID_0000",
            "VID_0123&PID_0000",
            "VID_4567&PID_0000",
            "VID_89ab&PID_0000",
            "VID_89AB&PID_0000",
            "VID_cdef&PID_0000",
            "VID_CDEF&PID_0000",
            "VID_0000&PID_0123",
            "VID_0000&PID_4567",
            "VID_0000&PID_89ab",
            "VID_0000&PID_89AB",
            "VID_0000&PID_cdef",
            "VID_0000&PID_CDEF",
            "VID_fFfF&PID_FfFf",
            "xxxVID_0000&PID_0000xxx",
        ];

        public static IEnumerable<string[]> Valid => from value in _Valid select new string[] { value };
    }

    [TestMethod]
    [DynamicData(nameof(HardwareIdData.Invalid), typeof(HardwareIdData))]
    public void FromHardwareOrInstanceIdInvalid(string text)
    {
        Assert.ThrowsExactly<FormatException>(() =>
        {
            var vidPid = VidPid.FromHardwareOrInstanceId(text);
        });
    }

    [TestMethod]
    [DynamicData(nameof(HardwareIdData.Valid), typeof(HardwareIdData))]
    public void FromHardwareOrInstanceIdValid(string text)
    {
        var vidPid = VidPid.FromHardwareOrInstanceId(text);
        var expectedVid = ushort.Parse(text.Split("VID_")[1][..4], NumberStyles.AllowHexSpecifier);
        var expectedPid = ushort.Parse(text.Split("PID_")[1][..4], NumberStyles.AllowHexSpecifier);
        Assert.AreEqual(expectedVid, vidPid.Vid);
        Assert.AreEqual(expectedPid, vidPid.Pid);
    }

    [TestMethod]
    public void Vendor_NotTrimmed()
    {
        UsbIds.TestDataPath = Path.Combine(TestContext.DeploymentDirectory!, "VidPid_vendors.ids");

        var vendor = VidPid.Parse("0001:0000").GetVendorProduct(false).Vendor;

        Assert.IsNotNull(vendor);
        Assert.AreEqual(vendor.Trim(), vendor);
    }

    [TestMethod]
    public void Vendor_InvalidUtf8()
    {
        UsbIds.TestDataPath = Path.Combine(TestContext.DeploymentDirectory!, "VidPid_vendors.ids");

        var vendor = VidPid.Parse("0002:0000").GetVendorProduct(false).Vendor;

        Assert.IsNotNull(vendor);
        Assert.AreEqual(vendor.Trim(), vendor);
    }

    [TestMethod]
    public void Vendor_Empty()
    {
        UsbIds.TestDataPath = Path.Combine(TestContext.DeploymentDirectory!, "VidPid_vendors.ids");

        var vendor = VidPid.Parse("0003:0000").GetVendorProduct(false).Vendor;

        Assert.IsNull(vendor);
    }

    [TestMethod]
    public void Vendor_EOF()
    {
        UsbIds.TestDataPath = Path.Combine(TestContext.DeploymentDirectory!, "VidPid_vendors.ids");

        var vendor = VidPid.Parse("0004:0000").GetVendorProduct(false).Vendor;

        Assert.IsNull(vendor);
    }

    [TestMethod]
    public void Vendor_NotFound_EOF()
    {
        UsbIds.TestDataPath = Path.Combine(TestContext.DeploymentDirectory!, "VidPid_vendors.ids");

        var vendor = VidPid.Parse("0005:0000").GetVendorProduct(false).Vendor;

        Assert.IsNull(vendor);
    }

    [TestMethod]
    public void Product_NotTrimmed()
    {
        UsbIds.TestDataPath = Path.Combine(TestContext.DeploymentDirectory!, "VidPid_products.ids");

        var product = VidPid.Parse("0001:0001").GetVendorProduct(true).Product;

        Assert.IsNotNull(product);
        Assert.AreEqual(product.Trim(), product);
    }

    [TestMethod]
    public void Product_InvalidUtf8()
    {
        UsbIds.TestDataPath = Path.Combine(TestContext.DeploymentDirectory!, "VidPid_products.ids");

        var product = VidPid.Parse("0001:0002").GetVendorProduct(true).Product;

        Assert.IsNotNull(product);
        Assert.AreEqual(product.Trim(), product);
    }

    [TestMethod]
    public void Product_Empty()
    {
        UsbIds.TestDataPath = Path.Combine(TestContext.DeploymentDirectory!, "VidPid_products.ids");

        var product = VidPid.Parse("0001:0003").GetVendorProduct(true).Product;

        Assert.IsNull(product);
    }

    [TestMethod]
    public void Product_EOF()
    {
        UsbIds.TestDataPath = Path.Combine(TestContext.DeploymentDirectory!, "VidPid_products.ids");

        var product = VidPid.Parse("0001:0004").GetVendorProduct(true).Product;

        Assert.IsNull(product);
    }

    [TestMethod]
    public void Product_NotFound_EOF()
    {
        UsbIds.TestDataPath = Path.Combine(TestContext.DeploymentDirectory!, "VidPid_products.ids");

        var product = VidPid.Parse("0001:0005").GetVendorProduct(true).Product;

        Assert.IsNull(product);
    }

    [TestMethod]
    public void EmptyBytePointers()
    {
        UsbIds.TestEmptyBytePointers = true;

        var vendor = VidPid.Parse("0001:0000").GetVendorProduct(false).Vendor;

        Assert.IsNull(vendor);
    }

    [TestMethod]
    public void Data_NotFound()
    {
        UsbIds.TestDataPath = Path.Combine(TestContext.DeploymentDirectory!, "non-existing.ids");

        var vendor = VidPid.Parse("0001:0000").GetVendorProduct(false).Vendor;

        Assert.IsNull(vendor);
    }
}
