// SPDX-FileCopyrightText: 2024 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Windows.Win32.Devices.DeviceAndDriverInstallation;
using Windows.Win32.Devices.Properties;
using Windows.Win32.Devices.Usb;
using static Usbipd.Interop.UsbIp;
using static Usbipd.Interop.VBoxUsb;
using static Usbipd.Interop.VBoxUsbMon;
using static Usbipd.Interop.WinSDK;

namespace UnitTests;

[TestClass]
sealed class Struct_Tests
{
    /// <summary>
    /// This verifies that:
    /// <list type="number">
    /// <item>The struct is unmanaged.</item>
    /// <item>The struct is free from marshalling shenanigans.</item>
    /// </list>
    /// The goal is that <see cref="Unsafe.SizeOf{T}"/> can always be used and that
    /// <see cref="MemoryMarshal"/> is sufficient. I.e., <see cref="Marshal"/> is not needed.
    /// I.e., all structures are blittable.
    /// </summary>
    /// <typeparam name="T">The unmanaged struct type to test.</typeparam>
    static void Test<T>() where T : unmanaged
    {
        Assert.AreEqual(Marshal.SizeOf<T>(), Unsafe.SizeOf<T>());

        var instance = new T();
        var instanceBytes = MemoryMarshal.AsBytes(new Span<T>(ref instance));
        for (var i = 0; i < instanceBytes.Length; i++)
        {
            // Fill the instance with some "random" bytes. 53 and 47 are primes.
            instanceBytes[i] = unchecked((byte)(0x42 + (i * 53 % 47)));
        }

        var marshalled = new byte[Marshal.SizeOf<T>()];
        unsafe // DevSkim: ignore DS172412
        {
            fixed (byte* dst = marshalled)
            {
                Marshal.StructureToPtr(instance, (nint)dst, false);

                Assert.IsTrue(instanceBytes.SequenceEqual(marshalled));

                Marshal.DestroyStructure<T>((nint)dst);
            }
        }
    }

    [TestMethod]
    public void CM_NOTIFY_FILTER_Test()
    {
        Test<CM_NOTIFY_FILTER>();
    }

    [TestMethod]
    public void DEVPROP_BOOLEAN_Test()
    {
        Test<DEVPROP_BOOLEAN>();
    }

    [TestMethod]
    public void SP_DEVINFO_DATA_Test()
    {
        Test<SP_DEVINFO_DATA>();
    }

    [TestMethod]
    public void SP_DEVINSTALL_PARAMS_W_Test()
    {
        Test<SP_DEVINSTALL_PARAMS_W>();
    }

    [TestMethod]
    public void SP_DRVINFO_DATA_V2_W_Test()
    {
        Test<SP_DRVINFO_DATA_V2_W>();
    }

    [TestMethod]
    public void SP_DRVINFO_DETAIL_DATA_W_Test()
    {
        Test<SP_DRVINFO_DETAIL_DATA_W>();
    }

    [TestMethod]
    public void USB_COMMON_DESCRIPTOR_Test()
    {
        Test<USB_COMMON_DESCRIPTOR>();
    }

    [TestMethod]
    public void USB_CONFIGURATION_DESCRIPTOR_Test()
    {
        Test<USB_CONFIGURATION_DESCRIPTOR>();
    }

    [TestMethod]
    public void USB_DEFAULT_PIPE_SETUP_PACKET_Test()
    {
        Test<USB_DEFAULT_PIPE_SETUP_PACKET>();
    }

    [TestMethod]
    public void USB_DESCRIPTOR_REQUEST_Test()
    {
        Test<USB_DESCRIPTOR_REQUEST>();
    }

    [TestMethod]
    public void USB_INTERFACE_DESCRIPTOR_Test()
    {
        Test<USB_INTERFACE_DESCRIPTOR>();
    }

    [TestMethod]
    public void USB_NODE_CONNECTION_INFORMATION_EX_V2_Test()
    {
        Test<USB_NODE_CONNECTION_INFORMATION_EX_V2>();
    }

    [TestMethod]
    public void UsbIpHeader_Test()
    {
        Test<UsbIpHeader>();
    }

    [TestMethod]
    public void UsbIpIsoPacketDescriptor_Test()
    {
        Test<UsbIpIsoPacketDescriptor>();
    }

    [TestMethod]
    public void UsbSupClaimDev_Test()
    {
        Test<UsbSupClaimDev>();
    }

    [TestMethod]
    public void UsbSupFltAddOut_Test()
    {
        Test<UsbSupFltAddOut>();
    }

    [TestMethod]
    public void UsbSupUrb_Test()
    {
        Test<UsbSupUrb>();
    }

    [TestMethod]
    public void UsbSupVersion_Test()
    {
        Test<UsbSupVersion>();
    }
}
