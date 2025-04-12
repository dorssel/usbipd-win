// SPDX-FileCopyrightText: 2025 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

using Windows.Win32;
using Windows.Win32.Devices.DeviceAndDriverInstallation;

namespace UnitTests;

[TestClass]
[DeploymentItem("../../../../../Drivers")]
sealed class Driver_Tests
{
    public TestContext TestContext { get; set; }

    public static string[] SupportedPlatforms => ["x64", "arm64"];

    static SafeInfHandle GetInf(string platform)
    {
        unsafe // DevSkim: ignore DS172412
        {
            var inf = new SafeInfHandle(TestPInvoke.SetupOpenInfFile(Path.Combine(platform, "VBoxUSB.inf"), "USB", INF_STYLE.INF_STYLE_WIN4, null));

            Assert.IsFalse(inf.IsInvalid);

            return inf;
        }
    }

    static string GetPlatformSection(string platform)
    {
        var section = platform switch
        {
            "x64" => "AMD64",
            "arm64" => "ARM64",
            _ => throw new ArgumentException("Unsupported platform", nameof(platform)),
        };
        return $"VBoxUSB.NT{section}";
    }

    static string GetString(SafeInfHandle inf, string section, string key)
    {
        unsafe // DevSkim: ignore DS172412
        {
            var requiredSize = 64;
            var buffer = stackalloc char[requiredSize];
            var success = TestPInvoke.SetupGetLineText(null, inf, section, key, new(buffer, requiredSize), (uint*)&requiredSize);

            Assert.IsTrue(success);

            return new(buffer);
        }
    }

    static string GetDriverDescription(SafeInfHandle inf)
    {
        return GetString(inf, "Strings", "VBoxUSB_DrvDesc");
    }

    [TestMethod]
    [DynamicData(nameof(SupportedPlatforms))]
    public void HardwareId(string platform)
    {
        using var inf = GetInf(platform);

        var hardwareId = GetString(inf, GetPlatformSection(platform), GetDriverDescription(inf)).Split(',')[1];

        Assert.AreEqual($@"USB\{Usbipd.Interop.VBoxUsb.StubHardwareId}", hardwareId);
    }

    [TestMethod]
    [DynamicData(nameof(SupportedPlatforms))]
    public void DriverDescription(string platform)
    {
        using var inf = GetInf(platform);

        var driverDescription = GetDriverDescription(inf);

        Assert.AreEqual(Usbipd.Interop.VBoxUsb.DriverDescription, driverDescription);
    }
}
