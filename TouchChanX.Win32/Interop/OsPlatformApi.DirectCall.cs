using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace TouchChanX.Win32.Interop;

public static partial class OsPlatformApi // DirectCall
{
    public static nint GetConsoleWindow() => PInvoke.GetConsoleWindow();

    [SupportedOSPlatform("windows10.0.14393")]
    public static uint GetDpiForWindow(nint hwnd) => PInvoke.GetDpiForWindow(new(hwnd));

    public static bool IsWindow(nint hwnd) => PInvoke.IsWindow(new(hwnd));

    public static bool IsForegroundWindow(nint hwnd) =>
        hwnd != nint.Zero && PInvoke.GetForegroundWindow() == new HWND(hwnd);

    public static void SetFocus(nint hwnd) => PInvoke.SetFocus(new(hwnd));
}
