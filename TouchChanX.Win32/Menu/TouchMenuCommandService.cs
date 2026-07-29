using TouchChanX.Win32.Interop;
using R3;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace TouchChanX.Win32.Menu;

public static class TouchMenuCommandService
{
    private const int CloseGameDelay = 200;
    private const int ScreenshotDelay = 500;
    private const int MoveStep = 1;
    private static readonly Subject<RecognizedGesture> GestureRecognizedSubject = new();
    private static readonly SerialDisposable GestureRecognitionSession = new();

    public static Observable<RecognizedGesture> ObservableGestureRecognized => GestureRecognizedSubject;

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

                GestureRecognitionSession.Disposable =
                    ObserveGestureRecognition(touchWindowHandle, gameWindowHandle)
                    .Subscribe(GestureRecognizedSubject.OnNext);
                break;
            case "gesture":
                if (!OperatingSystem.IsWindowsVersionAtLeast(8))
                    return;

                GestureRecognitionSession.Disposable = Disposable.Empty;
                break;
        }
    }

    public static void DisconnectGameWindowInteractions()
    {
        TouchConversionHooker.Uninstall();
        GestureRecognitionSession.Disposable = Disposable.Empty;
    }

    private static Observable<RecognizedGesture> ObserveGestureRecognition(nint touchWindowHandle, nint gameWindowHandle) =>
        Observable.Create<RecognizedGesture>(observer =>
        {
            var service = new GestureRecognitionService(touchWindowHandle)
            {
                IsEnabled = true,
            };
            var subscription = service.ObservableGestureRecognized.Subscribe(gesture =>
            {
                SendGestureInput(gesture, service.LastGesturePosition, gameWindowHandle);
                observer.OnNext(gesture);
            });

            return Disposable.Create(() =>
            {
                subscription.Dispose();
                service.Dispose();
            });
        });

    private static void SendGestureInput(RecognizedGesture gesture, System.Drawing.Point position, nint gameWindowHandle)
    {
        if (gameWindowHandle == nint.Zero ||
            PInvoke.GetForegroundWindow() != new HWND(gameWindowHandle))
            return;

        switch (gesture)
        {
            case RecognizedGesture.TwoFingerTap:
                PInvoke.SetCursorPos(position.X, position.Y);
                _ = InputSimulator.RightClickAsync();
                break;
            case RecognizedGesture.ThreeFingerTap:
                _ = InputSimulator.PressAsync(VirtualKeyCode.Space);
                break;
            case RecognizedGesture.TwoFingerSwipeUp:
                InputSimulator.Scroll(1);
                break;
            case RecognizedGesture.TwoFingerSwipeDown:
                InputSimulator.Scroll(-1);
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
            using var process = Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Debug.WriteLine($"Failed to launch {uri}: {ex}");
        }
    }
}
