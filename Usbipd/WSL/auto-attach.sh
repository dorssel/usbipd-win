#!/bin/sh

# SPDX-FileCopyrightText: 2022 Frans van Dorsselaer
#
# SPDX-License-Identifier: GPL-3.0-only

# Usage (run as root):
#    auto-attach.sh <HOST-IP-ADDRESS> <BUSID>

set -e

if [ -z "$BASH" ]; then
  # Bash is required later in this script, check and report if it's not available
  which bash > /dev/null || { echo "--auto-attach is not available without bash shell"; exit 1; }

  # Relaunch this script with bash as a separate background process.
  bash ${0} ${@} & disown
  exit 0
fi

HOST=$1
BUSID=$2

IS_ATTACHED=0
LAST_ERROR=""
LAST_REPORTED_ERROR=""

report_attached() {
    local OLD_ATTACHED=$((IS_ATTACHED))
    IS_ATTACHED=$(($1))

    if ((IS_ATTACHED != OLD_ATTACHED)); then
        if ((IS_ATTACHED == 1)); then
            echo "Attached"
        else
            echo "Detached"
        fi
        LAST_REPORTED_ERROR=""
    fi
    if ((IS_ATTACHED == 0)) && [[ "$LAST_REPORTED_ERROR" != "$LAST_ERROR" ]]; then
        echo "$LAST_ERROR"
        LAST_REPORTED_ERROR="$LAST_ERROR"
    fi
}

try_attach() {
    # Use our distribution-independent build of usbip, which resides in the same directory as this very script.
    # NOTE: The working directory should have been set to the location of this script.
    LAST_ERROR=$(./usbip attach --remote="$HOST" --busid="$BUSID" 2>&1) || return 1
    LAST_ERROR=""
    return 0
}

is_attached() {

    # This function determines if the target device is already attached.
    # This uses bash-only functionality for two reasons:
    # - performance (does not start new processes every second)
    # - no dependencies on installed tools (should work on any distribution)

    # We enumerate all currently attached USBIP devices. Note that we cannot simply
    # enumerate /var/run/vhci_hcd/port*, as those are not removed when a device is detached.
    {
        # Expected format:
        # hub port sta spd dev      sockfd local_busid
        # hs  0000 006 002 00040002 000003 1-1
        # hs  0001 004 000 00000000 000000 0-0
        # hs  0002 004 000 00000000 000000 0-0
        # ...
        # ss  0009 004 000 00000000 000000 0-0
        # ss  0010 004 000 00000000 000000 0-0
        # ...

        read -r # skip headers
        while read -r line; do
            read -r -a strarr <<<"$line"

            local SOCKFD=$((10#${strarr[5]}))
            if ((SOCKFD == 0)); then
                # No device on this port.
                continue
            fi
            local PORT=$((10#${strarr[1]}))

            # Now figure out if this is the target device or not.

            read -r -a strarr </var/run/vhci_hcd/port$PORT

            # Expected format:
            # 172.21.0.1 3240 4-2

            local REMOTE_IP=${strarr[0]}
            # local REMOTE_PORT=${strarr[1]}
            local REMOTE_BUSID=${strarr[2]}

            if [[ "$REMOTE_IP" == "$HOST" && "$REMOTE_BUSID" == "$BUSID" ]]; then
                # Found it.
                return 0
            fi
        done
    } </sys/devices/platform/vhci_hcd.0/status

    # None of the devices matched the target device.
    return 1
}

sleep() {
    local SECONDS=$(($1))
    local ERROR=0
    # attempt to sleep without creating a new process
    read -r -t $SECONDS || ERROR=$?
    if [[ $ERROR -le 128 && $ERROR -ne 0 ]]; then
        # neither timeout nor success, use regular sleep instead
        command sleep $SECONDS
    fi
}

while :; do
    if is_attached; then
        report_attached 1
    else
        report_attached 0
        if try_attach; then
            report_attached 1
        fi
    fi
    sleep 1
done
