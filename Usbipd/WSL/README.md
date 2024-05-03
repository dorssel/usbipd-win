<!--
SPDX-FileCopyrightText: 2023 Frans van Dorsselaer

SPDX-License-Identifier: GPL-3.0-only
-->

# Declaration of License Compliance

The binary `usbip` was built from sources subject to different (compatible) licenses:

- Part of the Linux kernel, in particular the sources in `tools/usb/usbip`

  A copy of these sources was used from [WSL2-Linux-Kernel](https://github.com/microsoft/WSL2-Linux-Kernel)
  subject to the terms and conditions of the
  [GNU General Public License, version 2](https://www.gnu.org/licenses/old-licenses/gpl-2.0.html).

  With the binary distribution of WSL2-Linux-Kernel came a written offer to download the source code from
  <https://github.com/microsoft/WSL2-Linux-Kernel/tree/linux-msft-wsl-5.15.146.1/>.

  This README is to conform to clause 3c of the license.

  Note that the subset of sources in `tools/usb/usbip` are in fact licensed under GNU General Public License, version 2 *or later*.

- `libudev-zero`

  `usbip` is statically linked against [`libudev-zero`](https://github.com/illiliti/libudev-zero) version 1.0.3
  subject to the terms and conditions of the
  [ISC License](https://opensource.org/license/isc-license-txt/).

# diff

The following changes were made to the build script for `usbip`:

- Linking against `libudev-zero`
- Static linking
- Stripped output

```diff
diff --git a/tools/usb/usbip/src/Makefile.am b/tools/usb/usbip/src/Makefile.am
index e26f39e0579d..2c9db4e68ced 100644
--- a/tools/usb/usbip/src/Makefile.am
+++ b/tools/usb/usbip/src/Makefile.am
@@ -2,6 +2,7 @@
 AM_CPPFLAGS = -I$(top_srcdir)/libsrc -DUSBIDS_FILE='"@USBIDS_DIR@/usb.ids"'
 AM_CFLAGS   = @EXTRA_CFLAGS@
 LDADD       = $(top_builddir)/libsrc/libusbip.la
+LDFLAGS     = -all-static -L../../../../../libudev-zero/ -s

 sbin_PROGRAMS := usbip usbipd

```
