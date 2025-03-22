// SPDX-FileCopyrightText: 2022 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

#if NETSTANDARD

#pragma warning disable IDE0130 // Namespace does not match folder structure
namespace System.Runtime.CompilerServices;
#pragma warning restore IDE0130 // Namespace does not match folder structure

/// <summary>
/// Fix for using C# 9 feature in netstandard2.0.
/// </summary>
static class IsExternalInit
{
}

#endif
