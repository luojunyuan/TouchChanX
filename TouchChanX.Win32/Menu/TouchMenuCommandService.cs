using TouchChanX.Win32.Interop;
using Windows.Win32;

namespace TouchChanX.Win32.Menu;

public static class TouchMenuCommandService
{
    private const int CloseGameDelay = 200;
    private const int ScreenshotDelay = 500;
    private const int MoveStep = 1;
    private static GestureRecognitionService? _gestureRecognitionService;

    public static async Task ExecuteAsync(string commandId, nint gameWindowHandle)
    {
        if (string.IsNullOrWhiteSpace(commandId) ||
            gameWindowHandle == nint.Zero ||
            !OsPlatformApi.IsWindow(gameWindowHandle))
        {
            return;
        }

        switch (commandId)
        {
            case "volume-down":
                await InputSimulator.PressAsync(VirtualKeyCode.VolumeDown);
                break;
            case "volume-up":
                await InputSimulator.PressAsync(VirtualKeyCode.VolumeUp);
                break;
            case "screenshot":
                await Task.Delay(ScreenshotDelay);
                await InputSimulator.PressAsync(VirtualKeyCode.LeftWindows, VirtualKeyCode.Shift, VirtualKeyCode.S);
                break;
            case "task-view":
                await InputSimulator.PressAsync(VirtualKeyCode.LeftWindows, VirtualKeyCode.Tab);
                break;
            case "action-center":
                await InputSimulator.PressAsync(VirtualKeyCode.LeftWindows, VirtualKeyCode.A);
                break;
            case "virtual-touchpad":
                LaunchUri("ms-virtualtouchpad:");
                break;
            case "desktop":
                await InputSimulator.PressAsync(VirtualKeyCode.LeftWindows, VirtualKeyCode.D);
                break;
            case "fullscreen":
                BringGameWindowToForeground(gameWindowHandle);
                await InputSimulator.PressAsync(VirtualKeyCode.Menu, VirtualKeyCode.Enter);
                break;
            case "close-game":
                await Task.Delay(CloseGameDelay);
                RequestCloseGameWindow(gameWindowHandle);
                break;
            case "move-up":
                MoveGameWindowBy(gameWindowHandle, 0, -MoveStep);
                break;
            case "move-left":
                MoveGameWindowBy(gameWindowHandle, -MoveStep, 0);
                break;
            case "move-right":
                MoveGameWindowBy(gameWindowHandle, MoveStep, 0);
                break;
            case "move-down":
                MoveGameWindowBy(gameWindowHandle, 0, MoveStep);
                break;
        }
    }

    public static void SetToggleState(string toggleId, bool isOn, nint gameWindowHandle, nint touchWindowHandle)
    {
        switch (toggleId)
        {
            case "touch-to-mouse" when isOn && gameWindowHandle != nint.Zero && OsPlatformApi.IsWindow(gameWindowHandle):
                TouchConversionHooker.Install(gameWindowHandle, touchWindowHandle);
                break;
            case "touch-to-mouse":
                TouchConversionHooker.Uninstall();
                break;
            case "gesture" when isOn && touchWindowHandle != nint.Zero && OsPlatformApi.IsWindow(touchWindowHandle):
                if (!OperatingSystem.IsWindowsVersionAtLeast(8))
                    return;

                _gestureRecognitionService ??= new GestureRecognitionService(touchWindowHandle);
                _gestureRecognitionService.IsEnabled = true;
                break;
            case "gesture":
                if (!OperatingSystem.IsWindowsVersionAtLeast(8))
                    return;

                _gestureRecognitionService?.Dispose();
                _gestureRecognitionService = null;
                break;
        }
    }

    private static void BringGameWindowToForeground(nint hwnd) =>
        PInvoke.SetForegroundWindow(new(hwnd));

    private static void RequestCloseGameWindow(nint hwnd)
    {
        const uint WM_SYSCOMMAND = 0x0112;
        const nuint SC_CLOSE = 0xF060;

        PInvoke.PostMessage(new(hwnd), WM_SYSCOMMAND, SC_CLOSE, 0);
    }

    private static void MoveGameWindowBy(nint hwnd, int deltaX, int deltaY)
    {
        if (!PInvoke.GetWindowRect(new(hwnd), out var rect))
            return;

        PInvoke.MoveWindow(
            new(hwnd),
            rect.left + deltaX,
            rect.top + deltaY,
            rect.Width,
            rect.Height,
            false);
    }

    private static void LaunchUri(string uri)
    {
        try
        {
            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Debug.WriteLine($"Failed to launch {uri}: {ex}");
        }
    }
}
