<!--
SPDX-FileCopyrightText: 2020 Frans van Dorsselaer

SPDX-License-Identifier: GPL-2.0-only
-->

# usbipd-win

[![Build](https://github.com/dorssel/usbipd-win/workflows/Build/badge.svg?branch=master)](https://github.com/dorssel/usbipd-win/actions?query=workflow%3ABuild+branch%3Amaster)
[![CodeQL](https://github.com/dorssel/usbipd-win/workflows/CodeQL/badge.svg?branch=master)](https://github.com/dorssel/usbipd-win/actions?query=workflow%3ACodeQL+branch%3Amaster)
[![REUSE](https://github.com/dorssel/usbipd-win/workflows/REUSE/badge.svg?branch=master)](https://github.com/dorssel/usbipd-win/actions?query=workflow%3AREUSE+branch%3Amaster)
[![Markdown](https://github.com/dorssel/usbipd-win/workflows/Markdown/badge.svg?branch=master)](https://github.com/dorssel/usbipd-win/actions?query=workflow%3AMarkdown+branch%3Amaster)

Windows software for sharing locally connected USB devices to other machines, including Hyper-V guests and WSL 2.

## How to use

This software requires Microsoft Windows 8 / Microsoft Windows Server 2012 or newer;
it does not depend on any other software.

### Installation

Run the installer (.msi) from the [latest release](https://github.com/dorssel/usbipd-win/releases/latest)
on the Windows machine where your USB device is connected.

Alternatively, use the Windows Package Manager:

```pwsh
winget install usbipd
```

This will install and run a service called `usbipd` (display name: USBIP Device Host).
You can check the status of this service using the Services app from Windows.
Additionally, it will add the command line tool `usbipd` in your path.

### Lookup and Enable Devices

By default devices are not shared with usbip clients.
To lookup and share devices, open a command prompt as an Administrator and use the `usbipd` tool.
For example:

```pwsh
usbipd --help
usbipd list
usbipd bind --busid=<bus-port>
```

### Connecting Devices

From another (possibly virtual) machine running Linux, use `usbip` to claim the USB device:

```bash
usbip list --remote=<host>
sudo usbip attach --remote=<host> --busid=<bus-port>
```

A list of tested devices can be found on the [wiki](https://github.com/dorssel/usbipd-win/wiki).
Please file an issue if your device is not working.

## How to remove

Uninstall via Add/Remove Programs or via Settings/Apps.

Alternatively, use the Windows Package Manager:

```pwsh
winget uninstall usbipd
```

There should be no left-overs, but if you do find any: please file an issue.

## How it works

The software glues together the UsbIp network protocol (as implemented by the Linux kernel) and the USB drivers.
The installer includes the USB drivers from VirtualBox, which are also licensed under GPL and are properly signed by Oracle.
This *should* play nice with a coexisting full installation of VirtualBox, but that has not been tested extensively.

The software itself consists of an auto-start background service.

The installer also adds a firewall rule called `usbipd` to allow all local subnets to connect to the service;
this firewall rule can be tweaked to fine tune access control.

## USBIP on WSL 2

Currently, WSL 2 does not support USB devices by default. A workaround to this limitation is to use usbip.
Instructions on how to setup a Linux usbip client can be found [here](WSL_USBIP.md).
