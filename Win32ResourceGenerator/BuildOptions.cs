// SPDX-FileCopyrightText: 2026 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

namespace Win32ResourceGenerator;

record struct BuildOptions(
    string ProjectDir,
    string IntermediateOutputPath,
    string Configuration);
