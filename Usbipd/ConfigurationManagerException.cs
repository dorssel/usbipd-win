// SPDX-FileCopyrightText: 2021 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

using System.ComponentModel;
using Windows.Win32;
using Windows.Win32.Devices.DeviceAndDriverInstallation;
using Windows.Win32.Foundation;

namespace Usbipd;

sealed class ConfigurationManagerException : Win32Exception
{
    internal CONFIGRET ConfigRet { get; init; }

    public ConfigurationManagerException()
    {
    }

    public ConfigurationManagerException(string message)
        : base(message)
    {
    }

    public ConfigurationManagerException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    internal ConfigurationManagerException(CONFIGRET configRet, string message)
        : base((int)PInvoke.CM_MapCrToWin32Err(configRet, (uint)WIN32_ERROR.ERROR_CAN_NOT_COMPLETE), message)
    {
        ConfigRet = configRet;
    }
}
