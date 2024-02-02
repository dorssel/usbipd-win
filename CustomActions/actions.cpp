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
    {
        // See: https://learn.microsoft.com/en-us/windows-hardware/drivers/install/preinstalling-driver-packages
        log(hInstall, L"Installing VBoxUSB");
        if (!SetupCopyOEMInf((data + L"Drivers\\VBoxUSB.inf").c_str(), NULL, SPOST_PATH, 0, NULL, 0, NULL, NULL)) {
            log(hInstall, L"ERROR installing VBoxUSB: 0x%08x", GetLastError());
            return ERROR_INSTALL_FAILURE;
        }
    }
    {
        log(hInstall, L"Installing VBoxUSBMon");
        SC_HANDLE manager = OpenSCManager(NULL, SERVICES_ACTIVE_DATABASE, SC_MANAGER_ALL_ACCESS);
        if (!manager) {
            log(hInstall, L"ERROR OpenSCManager: 0x%08x", GetLastError());
            return ERROR_INSTALL_FAILURE;
        }
        SC_HANDLE service = CreateService(manager, L"VBoxUSBMon", L"VirtualBox USB Monitor Service", GENERIC_ALL, SERVICE_KERNEL_DRIVER,
            SERVICE_DEMAND_START, SERVICE_ERROR_NORMAL, (data + L"Drivers\\VBoxUSBMon.sys").c_str(), NULL, NULL, NULL, NULL, NULL);
        if (!service) {
            log(hInstall, L"ERROR CreateService: 0x%08x", GetLastError());
            CloseServiceHandle(manager);
            return ERROR_INSTALL_FAILURE;
        }
        CloseServiceHandle(service);
        CloseServiceHandle(manager);
    }
    return ERROR_SUCCESS;
}


// This action must run deferred, between InstallFiles and InstallFinalize.
UINT __stdcall UninstallDrivers(MSIHANDLE hInstall) {
    auto data = get_property(hInstall, L"CustomActionData");
    {
        BOOL need_reboot = FALSE;
        log(hInstall, L"Uninstalling VBoxUSB");
        if (!DiUninstallDriver(NULL, (data + L"Drivers\\VBoxUSB.inf").c_str(), 0, &need_reboot)) {
            log(hInstall, L"ERROR uninstalling VBoxUSB: 0x%08x", GetLastError());
            // continue
        }
        // ignore need_reboot
    }
    return ERROR_SUCCESS;
}
