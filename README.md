<!--
SPDX-FileCopyrightText: 2020 Frans van Dorsselaer

SPDX-License-Identifier: GPL-2.0-only
-->
# usbipd-win [![Build](https://github.com/dorssel/usbipd-win/workflows/Build/badge.svg?branch=master)](https://github.com/dorssel/usbipd-win/actions?query=workflow%3ABuild+branch%3Amaster) [![CodeQL](https://github.com/dorssel/usbipd-win/workflows/CodeQL/badge.svg?branch=master)](https://github.com/dorssel/usbipd-win/actions?query=workflow%3ACodeQL)

Windows software for hosting locally connected USB devices to other machines, including Hyper-V guests.

## How to use

This software requires Microsoft Windows 8 / Microsoft Windows Server 2012 or newer;
it does not depend on any other software.

1) Run the installer (.msi) on the Windows machine where your USB device is connected.
2) From another (possibly virtual) machine running Linux, use `usbip` to claim the USB device.

If you find that your device does not work, first read *limitations* below.
Please file an issue if you think your device should work with the current release.

## How to remove

Uninstall via Add/Remove Programs or via Settings/Apps.
There should be no left-overs, but if you do find any: please file an issue.

## Limitations

- For now, only USB devices with so called *bulk* endpoints work (USB flash drives, FTDI USB-to-serial, etc.).

## How it works

The software glues together the UsbIp network protocol (as implemented by the Linux kernel) and the USB drivers.
The installer includes the USB drivers from VirtualBox, which are also licensed under GPL and are properly signed by Oracle.
This *should* play nice with a coexisting full installation of VirtualBox, but that has not been tested extensively.

The software itself consists of an auto-start background service.

The installer also adds a firewall rule to allow all local subnets to connect to the service; this firewall rule can be tweaked to fine tune access control.
