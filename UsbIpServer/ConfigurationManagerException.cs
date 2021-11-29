// SPDX-FileCopyrightText: 2021 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-2.0-only

using System;
using System.ComponentModel;
using Windows.Win32;
using Windows.Win32.Devices.DeviceAndDriverInstallation;

namespace UsbIpServer
{
    public sealed class ConfigurationManagerException : Win32Exception
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
            : base((int)PInvoke.CM_MapCrToWin32Err(configRet, PInvoke.E_FAIL), message)
        {
            ConfigRet = configRet;
        }
    }
}
