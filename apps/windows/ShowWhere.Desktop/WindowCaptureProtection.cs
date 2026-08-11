using System.Runtime.InteropServices;

namespace ShowWhere.Desktop;

internal static class WindowCaptureProtection
{
    private const uint WdaExcludeFromCapture = 0x00000011;

    public static void Apply(IntPtr windowHandle)
    {
        if (windowHandle != IntPtr.Zero) _ = SetWindowDisplayAffinity(windowHandle, WdaExcludeFromCapture);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowDisplayAffinity(IntPtr windowHandle, uint affinity);
}
