// SPDX-FileCopyrightText: 2024 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

#include "stdafx.h"

using std::wstring;


static void log(MSIHANDLE hInstall, wstring fmt, ...) {
    auto hRecord = MsiCreateRecord(0);
    if (!hRecord) {
        return;
    }
    va_list ap;
    va_start(ap, fmt);
    va_list ap_copy;
    va_copy(ap_copy, ap);
    size_t len = _vscwprintf(fmt.c_str(), ap_copy);
    va_end(ap_copy);
    wstring message;
    message.resize(len);
    vswprintf_s(message.data(), len + 1, fmt.c_str(), ap);
    va_end(ap);
    MsiRecordSetString(hRecord, 0, (L"CustomActions: " + message).c_str());
    MsiProcessMessage(hInstall, INSTALLMESSAGE_INFO, hRecord);
    MsiCloseHandle(hRecord);
}


static void require_reboot(MSIHANDLE hInstall) {
    log(hInstall, L"Requesting reboot");
    // This is what WixCheckRebootRequired looks for after InstallFinalize.
    GlobalAddAtom(L"WcaDeferredActionRequiresReboot");
}


static wstring get_property(MSIHANDLE hInstall, const wstring& name) {
    DWORD valueSize = 0;
    if (MsiGetProperty(hInstall, name.c_str(), (LPWSTR)L"", &valueSize) != ERROR_MORE_DATA) {
        return wstring();
    }
    ++valueSize;  // add NUL
    auto value = std::make_unique<WCHAR[]>(valueSize);
    if (MsiGetProperty(hInstall, name.c_str(), value.get(), &valueSize) != ERROR_SUCCESS) {
        return wstring();
    }
    return wstring(value.get(), valueSize);
}


// This action must run deferred, between InstallFiles and InstallFinalize.
UINT __stdcall InstallDrivers(MSIHANDLE hInstall) {
    auto data = get_property(hInstall, L"CustomActionData");
    BOOL request_reboot = FALSE;
    {
        BOOL need_reboot = FALSE;
        log(hInstall, L"Installing VBoxUSBMon");
        if (!DiInstallDriver(NULL, (data + L"Drivers\\VBoxUSBMon\\VBoxUSBMon.inf").c_str(), DIIRFLAG_FORCE_INF, &need_reboot)) {
            log(hInstall, L"ERROR installing VBoxUSBMon: 0x%08x", GetLastError());
            return ERROR_INSTALL_FAILURE;
        }
        request_reboot |= need_reboot;
    }
    {
        BOOL need_reboot = FALSE;
        log(hInstall, L"Installing VBoxUSB");
        if (!DiInstallDriver(NULL, (data + L"Drivers\\VBoxUSB\\VBoxUSB.inf").c_str(), DIIRFLAG_FORCE_INF, &need_reboot)) {
            log(hInstall, L"ERROR installing VBoxUSB: 0x%08x", GetLastError());
            return ERROR_INSTALL_FAILURE;
        }
        request_reboot |= need_reboot;
    }
    if (request_reboot) {
        require_reboot(hInstall);
    }
    return ERROR_SUCCESS;
}


// This action must run deferred, between InstallFiles and InstallFinalize.
UINT __stdcall UninstallDrivers(MSIHANDLE hInstall) {
    auto data = get_property(hInstall, L"CustomActionData");
    {
        BOOL need_reboot = FALSE;
        log(hInstall, L"Uninstalling VBoxUSB");
        if (!DiUninstallDriver(NULL, (data + L"Drivers\\VBoxUSB\\VBoxUSB.inf").c_str(), 0, &need_reboot)) {
            log(hInstall, L"ERROR uninstalling VBoxUSB: 0x%08x", GetLastError());
            // continue
        }
        // ignore need_reboot
    }
    {
        BOOL need_reboot = FALSE;
        log(hInstall, L"Uninstalling VBoxUSBMon");
        if (!DiUninstallDriver(NULL, (data + L"Drivers\\VBoxUSBMon\\VBoxUSBMon.inf").c_str(), 0, &need_reboot)) {
            log(hInstall, L"ERROR uninstalling VBoxUSBMon: 0x%08x", GetLastError());
            // continue
        }
        // ignore need_reboot
    }
    return ERROR_SUCCESS;
}
