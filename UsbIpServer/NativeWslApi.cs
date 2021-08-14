// SPDX-FileCopyrightText: Copyright (c) Microsoft Corporation
//
// SPDX-License-Identifier: GPL-2.0-only

using System;
using System.Runtime.InteropServices;

namespace UsbIpServer
{
    class NativeWslApi
    {
        public enum RpcAuthnLevel
        {
            Default = 0,
            None = 1,
            Connect = 2,
            Call = 3,
            Pkt = 4,
            PktIntegrity = 5,
            PktPrivacy = 6
        }

        public enum RpcImpLevel
        {
            Default = 0,
            Anonymous = 1,
            Identify = 2,
            Impersonate = 3,
            Delegate = 4
        }

        public enum EoAuthnCap
        {
            None = 0x00,
            MutualAuth = 0x01,
            StaticCloaking = 0x20,
            DynamicCloaking = 0x40,
            AnyAuthority = 0x80,
            MakeFullSIC = 0x100,
            Default = 0x800,
            SecureRefs = 0x02,
            AccessControl = 0x04,
            AppID = 0x08,
            Dynamic = 0x10,
            RequireFullSIC = 0x200,
            AutoImpersonate = 0x400,
            NoCustomMarshal = 0x2000,
            DisableAAA = 0x1000
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1712:Do not prefix enum values with type name", Justification = "Matches names in official documentatino.")]
        public enum WSL_DISTRIBUTION_FLAGS
        {
            WSL_DISTRIBUTION_FLAGS_NONE = 0,
            WSL_DISTRIBUTION_FLAGS_ENABLE_INTEROP = 1,
            WSL_DISTRIBUTION_FLAGS_APPEND_NT_PATH = 2,
            WSL_DISTRIBUTION_FLAGS_ENABLE_DRIVE_MOUNTING = 3
        }

        // This function is not part of the WSL API, but is needed to initialize COM
        // messaging in a way that is compatible with the WSL service.
        // https://github.com/microsoft/WSL/issues/5824
        [DllImport("ole32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern int CoInitializeSecurity(IntPtr pVoid, int cAuthSvc,
            IntPtr asAuthSvc, IntPtr pReserved1, RpcAuthnLevel level, RpcImpLevel impers,
            IntPtr pAuthList, EoAuthnCap dwCapabilities, IntPtr pReserved3);


        [DllImport("wslapi.dll", CharSet = CharSet.Unicode)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern int WslGetDistributionConfiguration(string distributionName,
            out ulong distributionVersion, out ulong defaultUID, out WSL_DISTRIBUTION_FLAGS wslDistributionFlags,
            out IntPtr defaultEnvironmentVariables, out ulong defaultEnvironmentVariableCount);
    }
}
