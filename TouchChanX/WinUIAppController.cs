using Microsoft.UI.Xaml;
using R3;
using R3.ObservableEvents;
using System.Diagnostics;
using TouchChanX.Persistence;
using TouchChanX.Win32;
using TouchChanX.Win32.Interop;
using TouchChanX.Win32.Menu;

namespace TouchChanX;

internal sealed partial class WinUIAppController(nint gameWindowHandle, IDisposable? splash)
{
    private nint _gameWindowHandle = gameWindowHandle;
    private readonly IDisposable? _splash = splash;
    private nint _touchWindowHandle;
    private IDisposable? _clientSizeSubscription;
    private IDisposable? _windowDestroyedSubscription;
    private bool _touchToMouseEnabled;
    private bool _gestureEnabled;

    public void Start()
    {
        InitializeReactiveRuntime();

        var window = CreateMainWindow();
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        _touchWindowHandle = hwnd;
        WindowConfiguration.ConfigureEmbeddedWindow(hwnd, _gameWindowHandle);

        var overlays = new GameWindowOverlayController(
            _gameWindowHandle,
            window.ShowMessage);
        SubscribeClientSize(hwnd, overlays);
        SubscribeObservableRegions(window, hwnd);
        SubscribeMenuCommands(window, overlays);
        SubscribeMenuToggles(window, hwnd);
        SubscribeGestures(window);
        ApplySettings(hwnd);
        SubscribeGameWindowDestroyed(hwnd, overlays);

        window.InitializeBindings();
        window.Events().Closed.Subscribe(_ =>
        {
            DisposeGameWindowSubscriptions();
            TouchMenuCommandService.DisconnectGameWindowInteractions();
            overlays.Dispose();
        });
        window.Activate();

        _splash?.Dispose();
    }

    private static void InitializeReactiveRuntime()
    {
        ObservableSystem.RegisterUnhandledExceptionHandler(ex => Debug.WriteLine(ex.ToString()));
        ObservableSystem.DefaultTimeProvider = WinUI3DispatcherTimeProvider.Default;
    }

    private static WinUI.MainWindow CreateMainWindow() =>
        new()
        {
            SystemBackdrop = new TransparentBackdrop()
        };

    private void SubscribeClientSize(nint hwnd, GameWindowOverlayController overlays)
    {
        // Keep the embedded window and its auxiliary overlays aligned with the game client area.
        _clientSizeSubscription?.Dispose();
        _clientSizeSubscription = GameWindowService.ClientSizeChanged(_gameWindowHandle)
            .Subscribe(size =>
            {
                OsPlatformApi.ResizeWindow(hwnd, size);
                overlays.UpdateClientSize(size);
            });
    }

    private static void SubscribeObservableRegions(WinUI.MainWindow window, nint hwnd)
    {
        var observableRegions = new WindowObservableRegionSet(hwnd);
        WinUI.Touch.TouchControl.ObservableRegionResetRequested
            .Merge(WinUI.Menu.MenuControl.ObservableRegionResetRequested)
            .Subscribe(_ => observableRegions.UseOriginalRegion());
        WinUI.Touch.TouchControl.ObservableTouchRegionChanged
            .Select(touchRect => touchRect.Scale(window.Dpi).ToGdiRect())
            .Subscribe(observableRegions.SetBaseRegion);
        window.MessageFlyoutVisibleRegionChanged
            .Select(rect => rect?.Scale(window.Dpi).ToGdiRect())
            .Subscribe(observableRegions.SetMessageFlyoutRegion);
    }

    private void SubscribeMenuCommands(WinUI.MainWindow window, GameWindowOverlayController overlays)
    {
        WinUI.Menu.MenuControl.ObservableCommandRequested
            .TakeUntil(window.Events().Closed)
            .SubscribeAwait(async (commandId, _) =>
            {
                switch (commandId)
                {
                    case "stretch":
                        StretchWindowService.Toggle(_gameWindowHandle);
                        return;
                    case "brightness-down":
                        overlays.Dim();
                        return;
                    case "brightness-up":
                        overlays.RestoreBrightness();
                        return;
                    case "lock-game":
                        overlays.OpenLockWindow();
                        return;
                    default:
                        await TouchMenuCommandService.ExecuteAsync(commandId, _gameWindowHandle);
                        return;
                }
            });
    }

    private void SubscribeMenuToggles(WinUI.MainWindow window, nint hwnd)
    {
        WinUI.Menu.MenuControl.ObservableToggleChanged
            .TakeUntil(window.Events().Closed)
            .Subscribe(toggle =>
            {
                switch (toggle.Id)
                {
                    case "touch-to-mouse":
                        _touchToMouseEnabled = toggle.IsOn;
                        break;
                    case "gesture":
                        _gestureEnabled = toggle.IsOn;
                        break;
                }

                TouchMenuCommandService.SetToggleState(toggle.Id, toggle.IsOn, _gameWindowHandle, hwnd);
            });
    }

    private static void SubscribeGestures(WinUI.MainWindow window)
    {
        TouchMenuCommandService.ObservableGestureRecognized
            .TakeUntil(window.Events().Closed)
            .Select(GetGestureMessage)
            .Subscribe(window.ShowMessage);
    }

    private void ApplySettings(nint hwnd)
    {
        var settings = new AppSettings();
        _touchToMouseEnabled = settings.TouchToMouse;
        _gestureEnabled = settings.Gesture;
        if (settings.TouchToMouse)
            TouchMenuCommandService.SetToggleState("touch-to-mouse", true, _gameWindowHandle, hwnd);
        if (settings.Gesture)
            TouchMenuCommandService.SetToggleState("gesture", true, _gameWindowHandle, hwnd);
    }

    private void SubscribeGameWindowDestroyed(nint hwnd, GameWindowOverlayController overlays)
    {
        // TODO: monitor parent destruction and attach to the next game window.
        _windowDestroyedSubscription?.Dispose();
        _windowDestroyedSubscription = GameWindowService.WindowDestroyed(_gameWindowHandle).Subscribe(_ =>
        {
            _gameWindowHandle = nint.Zero;
            DisposeGameWindowSubscriptions();
            overlays.HandleGameWindowDestroyed();
            WindowConfiguration.DetachEmbeddedWindow(hwnd);
        });
    }

    private void DisposeGameWindowSubscriptions()
    {
        _clientSizeSubscription?.Dispose();
        _clientSizeSubscription = null;
        _windowDestroyedSubscription?.Dispose();
        _windowDestroyedSubscription = null;
    }

    private static string GetGestureMessage(RecognizedGesture gesture) =>
        gesture switch
        {
            RecognizedGesture.ThreeFingerTap => "空格",
            RecognizedGesture.TwoFingerTap => "鼠标右键",
            RecognizedGesture.TwoFingerSwipeUp => "滚轮上划",
            RecognizedGesture.TwoFingerSwipeDown => "滚轮下滑",
            _ => gesture.ToString(),
        };

    private sealed class WindowObservableRegionSet(nint hwnd)
    {
        private System.Drawing.Rectangle? _baseRegion;
        private System.Drawing.Rectangle? _messageFlyoutRegion;
        private bool _usesOriginalRegion = true;

        public void UseOriginalRegion()
        {
            _usesOriginalRegion = true;
            Apply();
        }

        public void SetBaseRegion(System.Drawing.Rectangle rect)
        {
            _baseRegion = rect;
            _usesOriginalRegion = false;
            Apply();
        }

        public void SetMessageFlyoutRegion(System.Drawing.Rectangle? rect)
        {
            _messageFlyoutRegion = rect;
            Apply();
        }

        private void Apply()
        {
            if (_usesOriginalRegion)
            {
                OsPlatformApi.ResetWindowOriginalObservableRegion(hwnd);
                return;
            }

            var regions = new List<System.Drawing.Rectangle>();
            if (_baseRegion is { } baseRegion)
                regions.Add(baseRegion);
            if (_messageFlyoutRegion is { } messageFlyoutRegion)
                regions.Add(messageFlyoutRegion);

            OsPlatformApi.SetWindowObservableRegions(hwnd, regions);
        }
    }
}

public static class WinUIExtension
{
    private const int AntiClippingOffset = 1;

    extension(Windows.Foundation.Rect rect)
    {
        public Windows.Foundation.Rect Scale(double f) =>
            new(rect.X * f, rect.Y * f, rect.Width * f, rect.Height * f);

        public System.Drawing.Rectangle ToGdiRect() =>
            new((int)rect.X, (int)rect.Y, (int)rect.Width + AntiClippingOffset, (int)rect.Height + AntiClippingOffset);
    }

    extension(Window window)
    {
        public double Dpi => window.Content.XamlRoot.RasterizationScale;
    }
}
