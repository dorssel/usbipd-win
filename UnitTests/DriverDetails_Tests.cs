// SPDX-FileCopyrightText: 2025 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

using Usbipd.Automation;

namespace UnitTests;

[TestClass]
sealed class DriverDetails_Tests
{
    [TestMethod]
    public void DriverPath()
    {
        Assert.IsTrue(File.Exists(DriverDetails.Instance.DriverPath));
    }

    [TestMethod]
    public void VidPid()
    {
        // This test exists to alert us when something drastic changes in the INF file.
        // We rely on this VID/PID to identify older driver versions.
        Assert.AreEqual(new VidPid(0x80ee, 0xcafe), DriverDetails.Instance.VidPid);
    }

    [TestMethod]
    public void ClassGuid()
    {
        // This test exists to alert us when something drastic changes in the INF file.
        // We rely on this class GUID to identify older driver versions.
        Assert.AreEqual(new Guid("{36FC9E60-C465-11CF-8056-444553540000}"), DriverDetails.Instance.ClassGuid);
    }

    [TestMethod]
    public void Version()
    {
        // Keep this test up-to-date when updating the driver.
        Assert.AreEqual(new Version(7, 2, 0, 20228), DriverDetails.Instance.Version);
    }
}
