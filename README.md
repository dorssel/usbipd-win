<!--
SPDX-FileCopyrightText: 2020 Frans van Dorsselaer

SPDX-License-Identifier: GPL-2.0-only
-->

# usbipd-win

[![Build](https://github.com/dorssel/usbipd-win/workflows/Build/badge.svg?branch=master)](https://github.com/dorssel/usbipd-win/actions?query=workflow%3ABuild+branch%3Amaster)
[![CodeQL](https://github.com/dorssel/usbipd-win/workflows/CodeQL/badge.svg?branch=master)](https://github.com/dorssel/usbipd-win/actions?query=workflow%3ACodeQL+branch%3Amaster)
[![REUSE](https://github.com/dorssel/usbipd-win/workflows/REUSE/badge.svg?branch=master)](https://github.com/dorssel/usbipd-win/actions?query=workflow%3AREUSE+branch%3Amaster)
[![Markdown](https://github.com/dorssel/usbipd-win/workflows/Markdown/badge.svg?branch=master)](https://github.com/dorssel/usbipd-win/actions?query=workflow%3AMarkdown+branch%3Amaster)
[![codecov](https://codecov.io/gh/dorssel/usbipd-win/branch/master/graph/badge.svg?token=L0QI0AZRJI)](https://codecov.io/gh/dorssel/usbipd-win)

Windows software for sharing locally connected USB devices to other machines, including Hyper-V guests and WSL 2.

## How to install

This software requires Microsoft Windows 8.1 x64 / Microsoft Windows Server 2012 or newer;
it does not depend on any other software.

Run the installer (.msi) from the [latest release](https://github.com/dorssel/usbipd-win/releases/latest)
on the Windows machine where your USB device is connected.

Alternatively, use the Windows Package Manager:

```powershell
winget install --interactive --exact dorssel.usbipd-win
```

If you leave out `--interactive`, winget may immediately restart your computer if that is required to install the drivers.

This will install:

- A service called `usbipd` (display name: USBIP Device Host).\
  You can check the status of this service using the Services app from Windows.
- A command line tool `usbipd`.\
  The location of this tool will be added to the `PATH` environment variable.
- A firewall rule called `usbipd` to allow all local subnets to connect to the service.\
  You can modify this firewall rule to fine tune access control.\
  :information_source:\
  If you are using a third-party firewall, you may have to reconfigure it to allow
  incoming connections on TCP port 3240.

## How to use

### Share Devices

By default devices are not shared with USBIP clients.
To lookup and share devices, open a command prompt as an Administrator and use the `usbipd` tool.
For example:

```powershell
usbipd --help
usbipd list
usbipd bind --busid=<BUSID>
```

### Connecting Devices

From another (possibly virtual) machine running Linux, use `usbip` to claim the USB device:

```bash
usbip list --remote=<HOST>
sudo usbip attach --remote=<HOST> --busid=<BUSID>
```

A list of tested devices can be found on the [wiki](https://github.com/dorssel/usbipd-win/wiki).
Please file an issue if your device is not working.

### WSL 2

You can use the `usbipd wsl` subcommand to share and connect a device with a single command.
For example, open a command prompt:

```powershell
usbipd wsl --help
usbipd wsl list
usbipd wsl attach --busid=<BUSID>
```

:information_source:\
Currently, WSL 2 does not support USB devices by default.\
As a workaround, instructions on how to setup a USBIP client for WSL 2 can be found on the [wiki](https://github.com/dorssel/usbipd-win/wiki/WSL-support).

## How to remove

Uninstall via Add/Remove Programs or via Settings/Apps.

Alternatively, use the Windows Package Manager:

```powershell
winget uninstall usbipd
```

There should be no left-overs; please file an issue if you do find any.
