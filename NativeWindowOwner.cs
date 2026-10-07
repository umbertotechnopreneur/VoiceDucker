using System.ComponentModel;
using System.Runtime.InteropServices;

namespace VoiceDucker;

internal static class NativeWindowOwner
{
    private const int OwnerWindowIndex = -8;

    public static void SetOwner(IntPtr windowHandle, IntPtr ownerHandle)
    {
        if (windowHandle == IntPtr.Zero || ownerHandle == IntPtr.Zero)
        {
            throw new ArgumentException("Both window handles are required.");
        }

        Marshal.SetLastPInvokeError(0);
        var previous = SetWindowLongPtr(windowHandle, OwnerWindowIndex, ownerHandle);
        var error = Marshal.GetLastPInvokeError();
        if (previous == IntPtr.Zero && error != 0)
        {
            throw new Win32Exception(error, "The close dialog could not be attached to the main window.");
        }
    }

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr windowHandle, int index, IntPtr value);
}
