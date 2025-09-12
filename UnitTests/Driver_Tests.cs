// SPDX-FileCopyrightText: 2025 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

using Usbipd.Automation;
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

    [TestMethod]
    [DynamicData(nameof(SupportedPlatforms))]
    public void HardwareId(string platform)
    {
        // Expected format in INF:
        //
        // [VBoxUSB.NTAMD64]
        // %VBoxUSB_DrvDesc%=VBoxUSB,USB\VID_80EE&PID_CAFE

        string hardwareId;
        unsafe // DevSkim: ignore DS172412
        {
            using var inf = GetInf(platform);
            var success = TestPInvoke.SetupFindFirstLine(inf, GetPlatformSection(platform), null, out var context);
            Assert.IsTrue(success);
            Span<char> buffer = stackalloc char[64];
            success = TestPInvoke.SetupGetStringField(context, 2, buffer, null);
            Assert.IsTrue(success);
            hardwareId = new(buffer.TrimEnd('\0'));
        }

        Assert.IsTrue(VidPid.TryParseId(hardwareId, out var vidPid));
        Assert.AreEqual(Usbipd.Interop.VBoxUsb.Stub, vidPid);
    }
}
