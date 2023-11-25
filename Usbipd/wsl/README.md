<!--
SPDX-FileCopyrightText: 2023 Frans van Dorsselaer

SPDX-License-Identifier: GPL-3.0-only
-->

# Declaration of License Compliance

The binary `usbip` was built from sources subject to different (compatible) licenses:

- Part of the Linux kernel, in particular the sources in `tools/usb/usbip`

  A copy of these sources was used from [Windows Subsystem for Linux (WSL)](https://github.com/microsoft/WSL2-Linux-Kernel/tree/linux-msft-wsl-5.15.133.1)
  subject to the terms and conditions of the
  [GNU General Public License, version 2](https://www.gnu.org/licenses/old-licenses/gpl-2.0.html).

  With the binary distribution of WSL came a written offer to download the source code from
  <https://github.com/microsoft/WSL2-Linux-Kernel/tree/linux-msft-wsl-5.15.133.1/>.

  This README is to conform to clause 3c of the license.

  Note that the subset of sources in `tools/usb/usbip` are in fact licensed under GNU General Public License, version 2 *or later*.

- `libudev-zero`

  `usbip` is statically linked against [`libudev-zero`](https://github.com/illiliti/libudev-zero)
  subject to the terms and conditions of the
  [ISC License](https://opensource.org/license/isc-license-txt/).

# diff

The following changes were made to the source and the build script for `usbip`:

- All commands except 'attach' removed
- Linking against `libudev-zero`
- Static linking
- Stripped output

```diff
diff --git a/tools/usb/usbip/src/Makefile.am b/tools/usb/usbip/src/Makefile.am
index e26f39e0579d..212f40de7607 100644
--- a/tools/usb/usbip/src/Makefile.am
+++ b/tools/usb/usbip/src/Makefile.am
@@ -2,11 +2,11 @@
 AM_CPPFLAGS = -I$(top_srcdir)/libsrc -DUSBIDS_FILE='"@USBIDS_DIR@/usb.ids"'
 AM_CFLAGS   = @EXTRA_CFLAGS@
 LDADD       = $(top_builddir)/libsrc/libusbip.la
+LDFLAGS     = -all-static -L../../../../../libudev-zero/ -s

 sbin_PROGRAMS := usbip usbipd

 usbip_SOURCES := usbip.h utils.h usbip.c utils.c usbip_network.c \
-                usbip_attach.c usbip_detach.c usbip_list.c \
-                usbip_bind.c usbip_unbind.c usbip_port.c
+                usbip_attach.c

 usbipd_SOURCES := usbip_network.h usbipd.c usbip_network.c
diff --git a/tools/usb/usbip/src/usbip.c b/tools/usb/usbip/src/usbip.c
index f7c7220d9766..4183d6c72572 100644
--- a/tools/usb/usbip/src/usbip.c
+++ b/tools/usb/usbip/src/usbip.c
@@ -57,6 +57,7 @@ static const struct command cmds[] = {
                .help  = "Attach a remote USB device",
                .usage = usbip_attach_usage
        },
+#if 0
        {
                .name  = "detach",
                .fn    = usbip_detach,
@@ -87,6 +88,7 @@ static const struct command cmds[] = {
                .help  = "Show imported USB devices",
                .usage = NULL
        },
+#endif
        { NULL, NULL, NULL, NULL }
 };

```
