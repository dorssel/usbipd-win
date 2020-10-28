# usbipd-win [![.NET Core](https://github.com/dorssel/usbipd-win/workflows/.NET%20Core/badge.svg?branch=master)](https://github.com/dorssel/usbipd-win/actions?query=workflow%3A%22.NET+Core%22+branch%3Amaster) [![CodeQL](https://github.com/dorssel/usbipd-win/workflows/CodeQL/badge.svg?branch=master)](https://github.com/dorssel/usbipd-win/actions?query=workflow%3ACodeQL)

Windows software for hosting locally connected USB devices to other machines, including Hyper-V guests.

## How to use (ignoring caveats)

1) Run the installer (.msi) on the Windows machine where your USB device is connected.
2) From another (possibly virtual) machine running Linux, use `usbip` to claim the USB device.

If you find that your device does not work, first read *caveats* below. Please file an issue if you think your device should work with the current release.

## How to remove

Uninstall via Add/Remove Programs or via Settings/Apps. There should be no left-overs, but if you do find any: please file an issue.

## The caveats...

- For now, only USB devices with so called *bulk* endpoints work (USB flash drives, FTDI USB-to-serial, etc.).
- **No security** restrictions on who claims which device. Unlike Linux `usbipd`, this software (for now) exposes all available USB devices to all other computers on you local subnet.
- The software is not digitally signed (yet), so you will get some nasty warnings about untrusted software. For the paranoid: review the code and build it yourself (this is open source after all...). Consider to become a sponsor so I can afford a software signing certificate.

## Some details on how it works

The software glues together the UsbIp network protocol as implemented by the Linux kernel and the VirtualBox USB drivers. The installer includes the VirtualBox drivers (which fortunately are also licensed under GPL), so you may get popups about Oracle software. This *should* play nice with a coexisting full installation of VirtualBox, but that has not been tested extensively. The software itself consists of an auto-start background service, but for debugging purposes it can be run in a console window instead. The service logs to the EventLog. The installer also adds a firewall rule to allow all local subnets to connect to the application.
