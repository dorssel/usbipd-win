// SPDX-FileCopyrightText: 2024 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

#pragma once

#include <cstdarg>
#include <memory>
#include <string>

// Target Windows 10 or newer (including Windows Server 2019) only
#define WINVER 0x0A00
#define _WIN32_WINNT 0x0A00

#define WIN32_LEAN_AND_MEAN

// Windows Header Files
#include <Windows.h>
#include <Msi.h>
#include <MsiQuery.h>
#pragma comment(lib, "msi.lib")
#include <Newdev.h>
#pragma comment(lib, "newdev.lib")
#include <Setupapi.h>
#pragma comment(lib, "setupapi.lib")
