// SPDX-FileCopyrightText: 2020 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-2.0-only

using System;
using System.ComponentModel;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
using static UsbIpServer.Interop.WinSDK;

[assembly: SupportedOSPlatform("windows")]
namespace UsbIpServer
{
    sealed class DeviceFile : IDisposable
    {
        public DeviceFile(string fileName)
        {
            handle = NativeMethods.CreateFile(fileName, FileAccess.ReadWrite, FileShare.ReadWrite,
                IntPtr.Zero, FileMode.Open, (FileAttributes)FileFlags.FILE_FLAG_OVERLAPPED, IntPtr.Zero);
            if (handle.IsInvalid)
            {
                throw new Win32Exception("CreateFile");
            }
            if (!ThreadPool.BindHandle(handle))
            {
                handle.Dispose();
                throw new UnexpectedResultException("ThreadPool.BindHandle() failed");
            }
        }

        readonly SafeFileHandle handle;

        Task<uint> IoControlAsync(uint ioControlCode, byte[]? input, byte[]? output, bool exactOutput = true)
        {
            var taskCompletionSource = new TaskCompletionSource<uint>();
            var overlapped = new Overlapped();

            unsafe
            {
                void OnCompletion(uint errorCode, uint numBytes, NativeOverlapped* nativeOverlapped)
                {
                    if ((Win32Error)errorCode == Win32Error.ERROR_SUCCESS)
                    {
                        if (exactOutput && ((output?.Length ?? 0) != numBytes))
                        {
                            taskCompletionSource.SetException(new ProtocolViolationException($"DeviceIoControl returned {numBytes} bytes, expected {output?.Length ?? 0}"));
                        }
                        else
                        {
                            taskCompletionSource.SetResult(numBytes);
                        }
                    }
                    else
                    {
                        taskCompletionSource.SetException(new Win32Exception((int)errorCode, $"DeviceIoControl returned error {(Win32Error)errorCode}"));
                    }
                    Overlapped.Free(nativeOverlapped);
                }

                var nativeOverlapped = overlapped.Pack(OnCompletion, new object?[] { input, output });
                fixed (byte* pInput = input, pOutput = output)
                {
                    if (!NativeMethods.DeviceIoControl(handle, ioControlCode, (IntPtr)pInput, (uint)(input?.Length ?? 0),
                        (IntPtr)pOutput, (uint)(output?.Length ?? 0), out var bytesReturned, (IntPtr)nativeOverlapped))
                    {
                        var errorCode = (Win32Error)Marshal.GetLastWin32Error();
                        if (errorCode != Win32Error.ERROR_IO_PENDING)
                        {
                            OnCompletion((uint)errorCode, 0, nativeOverlapped);
                        }
                    }
                }
            }
            return taskCompletionSource.Task;
        }

        public Task<uint> IoControlAsync<T>(T ioControlCode, byte[]? input, byte[]? output, bool exactOutput = true) where T : Enum
        {
            return IoControlAsync((uint)(object)ioControlCode, input, output, exactOutput);
        }

        bool IsDisposed;
        public void Dispose()
        {
            if (!IsDisposed)
            {
                handle.Dispose();
                IsDisposed = true;
            }
        }
    }
}
