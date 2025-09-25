// SPDX-FileCopyrightText: 2021 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

using Windows.Win32;
using Windows.Win32.Devices.DeviceAndDriverInstallation;
using Windows.Win32.Devices.Usb;

namespace Usbipd;


/// <summary>
/// See https://docs.microsoft.com/en-us/windows-hardware/drivers/install/porting-from-setupapi-to-cfgmgr32#restart-device
/// </summary>
sealed partial class RestartingDevice
    : IDisposable
{
    public RestartingDevice(WindowsDevice device)
    {
        Device = device;
        unsafe // DevSkim: ignore DS172412
        {
            PNP_VETO_TYPE vetoType;
            var buffer = new char[checked((int)PInvoke.MAX_PATH)];
            fixed (char* pBuffer = buffer)
            {
                var cr = PInvoke.CM_Query_And_Remove_SubTree(Device.Node, &vetoType, pBuffer, PInvoke.MAX_PATH,
                    PInvoke.CM_REMOVE_NO_RESTART | PInvoke.CM_REMOVE_UI_NOT_OK);
                if (cr == CONFIGRET.CR_REMOVE_VETOED)
                {
                    buffer[^1] = '\0';
                    var vetoName = new string(pBuffer);
                    throw new ConfigurationManagerException(cr, $"{nameof(PInvoke.CM_Query_And_Remove_SubTree)} returned {cr}: {vetoType}, {vetoName}");
                }
                cr.ThrowOnError(nameof(PInvoke.CM_Query_And_Remove_SubTree));
            }
        }
    }

    readonly WindowsDevice Device;

    public void Dispose()
    {
        // We ignore errors for multiple reasons:
        // a) Dispose is not supposed to throw.
        // b) Race condition with physical device removal.
        // c) Race condition with the device node being marked ready by something else and
        //    device enumeration already replaced the DevNode with its (non-)VBox counterpart.

        try
        {
            // Some drivers fail to initialize if the device is left in a non-default state.
            // They expect to be loaded after the device is just plugged in. Hence, we cycle the port,
            // which acts as an unplug/replug. Therefore, the driver (host or client) will see a nice clean device.

            // NOTE: Some devices (SanDisk flash drives, mostly) don't like this right after driver load.
            // Since this code is run right after switching drivers, give the device some time to settle.
            // Experiments show 20ms almost does the job (very few failures), and 30ms no longer
            // reproduced errors after 100 attach/detach cycles. So, we use 100ms for good measure.

            Thread.Sleep(TimeSpan.FromMilliseconds(100));

            using var hubFile = Device.OpenHubInterface();

            var data = new USB_CYCLE_PORT_PARAMS() { ConnectionIndex = Device.BusId.Port };
            var buf = Tools.StructToBytes(data);
            hubFile.IoControlAsync(PInvoke.IOCTL_USB_HUB_CYCLE_PORT, buf, buf).Wait();

            // This is the reverse of what the constructor accomplished.
            _ = PInvoke.CM_Setup_DevNode(Device.Node, PInvoke.CM_SETUP_DEVNODE_READY);
        }
#pragma warning disable CA1031 // Do not catch general exception types
        catch { }
#pragma warning restore CA1031 // Do not catch general exception types
    }
}
