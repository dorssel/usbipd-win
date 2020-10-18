using Microsoft.Win32.SafeHandles;
using static UsbIpServer.Interop.WinSDK;

namespace UsbIpServer
{
    sealed class SafeDeviceInfoSetHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafeDeviceInfoSetHandle()
            : base(true)
        {
        }

        protected override bool ReleaseHandle() => NativeMethods.SetupDiDestroyDeviceInfoList(handle);
    }
}
