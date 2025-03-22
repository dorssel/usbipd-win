<!--
SPDX-FileCopyrightText: 2020 Frans van Dorsselaer

SPDX-License-Identifier: GPL-3.0-only
-->

# usbipd-win

[![Build](https://github.com/dorssel/usbipd-win/actions/workflows/build-installer.yml/badge.svg?branch=master)](https://github.com/dorssel/usbipd-win/actions/workflows/build-installer.yml?query=branch%3Amaster)
[![CodeQL](https://github.com/dorssel/usbipd-win/actions/workflows/codeql-analysis.yml/badge.svg?branch=master)](https://github.com/dorssel/usbipd-win/actions/workflows/codeql-analysis.yml?query=workflow%3ACodeQL+branch%3Amaster)
[![MegaLinter](https://github.com/dorssel/usbipd-win/actions/workflows/lint.yml/badge.svg?branch=master)](https://github.com/dorssel/usbipd-win/actions/workflows/lint.yml?query=workflow%3ALint+branch%3Amaster)
[![REUSE status](https://api.reuse.software/badge/github.com/dorssel/usbipd-win)](https://api.reuse.software/info/github.com/dorssel/usbipd-win)
[![Codecov](https://codecov.io/gh/dorssel/usbipd-win/branch/master/graph/badge.svg?token=L0QI0AZRJI)](https://codecov.io/gh/dorssel/usbipd-win)
[![GitHub all releases](https://img.shields.io/github/downloads/dorssel/usbipd-win/total?logo=github)](https://github.com/dorssel/usbipd-win/releases)

Windows software for sharing locally connected USB devices to other machines, including Hyper-V guests and WSL 2.

## How to install

This software requires
Microsoft Windows 10 (x64 or ARM64) / Microsoft Windows Server 2019, version 1809 or newer;
it does not depend on any other software.

Run the installer (.msi) from the [latest release](https://github.com/dorssel/usbipd-win/releases/latest)
on the Windows machine where your USB device is connected.

Alternatively, use the Windows Package Manager:

```powershell
winget install usbipd
```

This will install:

- A service called `usbipd` (display name: USBIP Device Host).\
  You can check the status of this service using the Services app from Windows.
- A command line tool `usbipd`.\
  The location of this tool will be added to the `PATH` environment variable.
- A firewall rule called `usbipd` to allow all local subnets to connect to the service.\
  You can modify this firewall rule to fine tune access control.

> [!NOTE]
> If you are using a third-party firewall, you may have to reconfigure it to allow
> incoming connections on TCP port 3240.

## How to use

### Share Devices

By default devices are not shared with USBIP clients.
To lookup and share devices, run the following commands with administrator privileges:

```powershell
usbipd --help
usbipd list
usbipd bind --busid=<BUSID>
```

Sharing a device is persistent; it survives reboots.

> [!TIP]
> See the [wiki](https://github.com/dorssel/usbipd-win/wiki/Tested-Devices) for a list of tested devices.

### Connecting Devices

Attaching devices to a client is non-persistent. You will have to re-attach after a reboot,
or when the device resets or is physically unplugged/replugged.

#### Non-WSL 2

From another (possibly virtual) machine running Linux, use the `usbip` client-side tool:

```bash
usbip list --remote=<HOST>
sudo usbip attach --remote=<HOST> --busid=<BUSID>
```

> [!NOTE]
> Client-side tooling exists for other operating systems such as Microsoft Windows, but not as part of this project.

#### WSL 2

You can attach the device from within Windows with the following command, which does not require administrator privileges:

```powershell
usbipd attach --wsl --busid=<BUSID>
```

> [!TIP]
> See the [wiki](https://github.com/dorssel/usbipd-win/wiki/WSL-support) on how to add drivers
> for USB devices that are not supported by the default WSL 2 kernel.

### GUI

See the [wiki](https://github.com/dorssel/usbipd-win/wiki/Graphical-User-Interfaces)
for a list of GUI and IDE integration tools in case you prefer that over a CLI.

## How to remove

Uninstall via Add/Remove Programs or via Settings/Apps.

Alternatively, use the Windows Package Manager:

```powershell
winget uninstall usbipd
```
