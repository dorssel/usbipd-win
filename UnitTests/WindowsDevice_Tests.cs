// SPDX-FileCopyrightText: 2025 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

using Windows.Win32;

namespace UnitTests;

[TestClass]
sealed class WindowsDevice_Tests
{
    static readonly Guid BogusGuid = Guid.Parse("12345678-0000-4000-0000-000000000000");

    [TestMethod]
    public void GetAll()
    {
        var devices = WindowsDevice.GetAll(null, false);

        Assert.IsNotEmpty(devices);
    }

    [TestMethod]
    public void GetAll_Present()
    {
        var devices = WindowsDevice.GetAll(null, true);

        Assert.IsNotEmpty(devices);
    }

    [TestMethod]
    public void GetAll_InstallerClass()
    {
        var devices = WindowsDevice.GetAll(TestPInvoke.GUID_DEVCLASS_SYSTEM, false);

        Assert.IsNotEmpty(devices);
    }

    [TestMethod]
    public void GetAll_InstallerClass_None()
    {
        var devices = WindowsDevice.GetAll(BogusGuid, false);

        Assert.IsEmpty(devices);
    }

    [TestMethod]
    public void GetAll_InterfaceClass()
    {
        var devices = WindowsDevice.GetAll(TestPInvoke.GUID_DEVINTERFACE_VOLUME);

        Assert.IsNotEmpty(devices);
    }

    [TestMethod]
    public void GetAll_InterfaceClass_None()
    {
        var devices = WindowsDevice.GetAll(BogusGuid);

        Assert.IsEmpty(devices);
    }

    [TestMethod]
    public void Equals_Null()
    {
        var device = WindowsDevice.GetAll(null, false).First();

        Assert.IsFalse(device.Equals((object)null!));
    }

    [TestMethod]
    public void Equals_Same()
    {
        var expected = WindowsDevice.GetAll(null, false).First();
        var device = WindowsDevice.GetAll(null, false).First();

        Assert.IsTrue(device.Equals((object)expected));
    }

    [TestMethod]
    public void Equals_Different()
    {
        var notExpected = WindowsDevice.GetAll(null, false).Skip(1).First();
        var device = WindowsDevice.GetAll(null, false).First();

        Assert.IsFalse(device.Equals((object)notExpected));
    }

    [TestMethod]
    public void TryCreate()
    {
        foreach (var expected in WindowsDevice.GetAll(null, false))
        {
            Assert.IsTrue(WindowsDevice.TryCreate(expected.InstanceId, out var device));
            Assert.AreEqual(expected.Node, device.Node);
            Assert.AreEqual(expected.InstanceId, device.InstanceId);
        }
    }

    [TestMethod]
    public void TryCreate_NonExistent()
    {
        Assert.IsFalse(WindowsDevice.TryCreate(@"NON\EXISTENT\DEVICE", out var _));
    }

    [TestMethod]
    public void IsStub()
    {
        foreach (var device in WindowsDevice.GetAll(null, false))
        {
            _ = device.IsStub;
        }
    }

    [TestMethod]
    public void IsHub()
    {
        foreach (var device in WindowsDevice.GetAll(null, false))
        {
            _ = device.IsHub;
        }
    }

    [TestMethod]
    public void IsPresent()
    {
        var present = WindowsDevice.GetAll(null, true).ToList();
        var nonPresent = WindowsDevice.GetAll(null, false).Except(present).ToList();

        foreach (var device in present)
        {
            Assert.IsTrue(device.IsPresent);
        }
        foreach (var device in nonPresent)
        {
            Assert.IsFalse(device.IsPresent);
        }
    }

    [TestMethod]
    public void IsDisabled()
    {
        foreach (var device in WindowsDevice.GetAll(null, false))
        {
            _ = device.IsDisabled;
        }
    }

    [TestMethod]
    public void Description()
    {
        foreach (var device in WindowsDevice.GetAll(null, false))
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(device.Description));
        }
    }

    [TestMethod]
    public void BusId()
    {
        foreach (var device in WindowsDevice.GetAll(null, false))
        {
            _ = device.BusId;
        }
    }

    [TestMethod]
    public void HasVBoxDriver()
    {
        foreach (var device in WindowsDevice.GetAll(null, false))
        {
            _ = device.HasVBoxDriver;
        }
    }

    [TestMethod]
    public void DriverVersion()
    {
        foreach (var device in WindowsDevice.GetAll(null, false))
        {
            _ = device.DriverVersion;
        }
    }

    [TestMethod]
    public void Children()
    {
        foreach (var device in WindowsDevice.GetAll(null, false))
        {
            _ = device.Children.ToList();
        }
    }

    [TestMethod]
    public void OpenVBoxInterface()
    {
        foreach (var device in WindowsDevice.GetAll(null, false))
        {
            try
            {
                using var deviceFile = device.OpenVBoxInterface();
            }
            catch (FileNotFoundException)
            {
                // This is the only allowed exception.
            }
        }
    }

    [TestMethod]
    public void OpenHubInterface()
    {
        foreach (var device in WindowsDevice.GetAll(null, false))
        {
            try
            {
                using var deviceFile = device.OpenHubInterface();
            }
            catch (FileNotFoundException)
            {
                // This is the only allowed exception.
            }
        }
    }
}
