using Microsoft.UI.Xaml;
using R3;
using R3.ObservableEvents;
using System.Diagnostics;
using TouchChanX.Persistence;
using TouchChanX.Win32;
using TouchChanX.Win32.Gamepad;
using TouchChanX.Win32.Interop;
using TouchChanX.Win32.Battery;
using TouchChanX.Win32.Menu;

namespace TouchChanX;

internal sealed partial class WinUIAppController(nint gameWindowHandle)
{
    private nint _gameWindowHandle = gameWindowHandle;
    private nint _touchWindowHandle;
    private nint _hudWindowHandle;
    private WinUI.HudWindow? _hudWindow;
    private IDisposable? _clientSizeSubscription;
    private IDisposable? _windowDestroyedSubscription;
    private IDisposable? _batterySubscription;
    private BatteryMonitor? _batteryMonitor;
    private GamepadController? _gamepadController;
    private WinUI.GamepadWindow? _gamepadWindow;
    private IDisposable? _gamepadWindowClosedSubscription;
    private bool _touchToMouseEnabled;
    private bool _gestureEnabled;

    public void Start()
    {
        InitializeReactiveRuntime();
        WinUI.Menu.MenuControl.IsBatteryFeatureAvailable = BatteryMonitor.IsAvailable();

        var window = CreateMainWindow();
        var hudWindow = CreateHudWindow();
        var gamepadController = new GamepadController(_gameWindowHandle);
        _gamepadController = gamepadController;
        _hudWindow = hudWindow;
        window.SetGamepadFeatureAvailable(gamepadController.HasConnectedController);

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        var hudHwnd = WinRT.Interop.WindowNative.GetWindowHandle(hudWindow);
        _touchWindowHandle = hwnd;
        _hudWindowHandle = hudHwnd;

        WindowConfiguration.ConfigureEmbeddedWindow(hwnd, _gameWindowHandle);
        WindowConfiguration.ConfigureEmbeddedWindow(hudHwnd, _gameWindowHandle, clickThrough: true);

        var overlays = new GameWindowOverlayController(
            _gameWindowHandle,
            hudWindow.ShowMessage,
            hudWindow.Dim,
            hudWindow.RestoreBrightness);
        SubscribeClientSize(hwnd, hudHwnd);
        SubscribeObservableRegions(window, hwnd);
        SubscribeMenuCommands(window, overlays);
        SubscribeMenuToggles(window, hwnd, hudWindow, gamepadController);
        SubscribeGamepad(window, gamepadController);
        SubscribeGestures(window, hudWindow);
        ApplySettings(hwnd, hudWindow, gamepadController);
        SubscribeGameWindowDestroyed(hwnd, hudHwnd, overlays, gamepadController);

        window.InitializeBindings();
        window.Events().Closed.Subscribe(_ =>
        {
            DisposeGameWindowSubscriptions();
            TouchMenuCommandService.DisconnectGameWindowInteractions();
            gamepadController.Dispose();
            CloseGamepadWindow();
            overlays.Dispose();
            CloseHudWindow();
        });

        window.Activate();
        hudWindow.Activate();

        WinUIApplication.SignalStartupCompleted();
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

    private static WinUI.HudWindow CreateHudWindow() =>
        new()
        {
            SystemBackdrop = new TransparentBackdrop()
        };

    private void SubscribeClientSize(nint hwnd, nint hudHwnd)
    {
        // Keep the embedded window and its auxiliary overlays aligned with the game client area.
        _clientSizeSubscription?.Dispose();
        _clientSizeSubscription = GameWindowService.ClientSizeChanged(_gameWindowHandle)
            .Subscribe(size =>
            {
                OsPlatformApi.ResizeWindow(hwnd, size);
                OsPlatformApi.ResizeWindow(hudHwnd, size);
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

    private void SubscribeMenuToggles(
        WinUI.MainWindow window,
        nint hwnd,
        WinUI.HudWindow hudWindow,
        GamepadController gamepadController)
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
                    case "battery":
                        SetBatteryMonitoring(hudWindow, toggle.IsOn);
                        break;
                    case "game-handler":
                        gamepadController.SetEnabled(toggle.IsOn);
                        if (!toggle.IsOn)
                            CloseGamepadWindow();
                        break;
                }

                TouchMenuCommandService.SetToggleState(toggle.Id, toggle.IsOn, _gameWindowHandle, hwnd);
            });
    }

    private void SubscribeGamepad(WinUI.MainWindow window, GamepadController gamepadController)
    {
        gamepadController.ObservableAvailabilityChanged
            .TakeUntil(window.Events().Closed)
            .Subscribe(isAvailable =>
            {
                window.SetGamepadFeatureAvailable(isAvailable);
                if (!isAvailable)
                    gamepadController.SetEnabled(false);
            });

        gamepadController.ObservableMappingRequested
            .TakeUntil(window.Events().Closed)
            .Subscribe(_ => ToggleGamepadWindow());
    }

    private void SetBatteryMonitoring(WinUI.HudWindow hudWindow, bool isOn)
    {
        _batterySubscription?.Dispose();
        _batterySubscription = null;
        _batteryMonitor = null;

        if (!isOn)
        {
            hudWindow.SetBatteryVisible(false);
            return;
        }

        if (!BatteryMonitor.IsAvailable())
        {
            hudWindow.SetBatteryVisible(false);
            return;
        }

        var monitor = new BatteryMonitor();
        _batteryMonitor = monitor;
        hudWindow.SetBatteryVisible(true);
        _batterySubscription = monitor.Observe()
            .Subscribe(snapshot => hudWindow.ApplyBatteryState(ToHudState(snapshot)));
    }

    private static WinUI.Controls.BatteryHudState ToHudState(BatteryHudSnapshot snapshot) =>
        new(
            snapshot.StatusText,
            snapshot.PercentText,
            snapshot.TimeLeftText,
            snapshot.PowerDrawText,
            snapshot.CapacityText,
            snapshot.PercentFraction,
            snapshot.HasBattery,
            snapshot.IsCharging);


    private static void SubscribeGestures(WinUI.MainWindow window, WinUI.HudWindow hudWindow)
    {
        TouchMenuCommandService.ObservableGestureRecognized
            .TakeUntil(window.Events().Closed)
            .Select(GetGestureMessage)
            .Subscribe(hudWindow.ShowMessage);
    }

    private void ApplySettings(
        nint hwnd,
        WinUI.HudWindow hudWindow,
        GamepadController gamepadController)
    {
        var settings = new AppSettings();
        _touchToMouseEnabled = settings.TouchToMouse;
        _gestureEnabled = settings.Gesture;
        if (settings.TouchToMouse)
            TouchMenuCommandService.SetToggleState("touch-to-mouse", true, _gameWindowHandle, hwnd);
        if (settings.Gesture)
            TouchMenuCommandService.SetToggleState("gesture", true, _gameWindowHandle, hwnd);
        if (settings.Battery && BatteryMonitor.IsAvailable())
            SetBatteryMonitoring(hudWindow, true);
        if (gamepadController.HasConnectedController &&
            (!settings.HasGamepadSetting || settings.Gamepad))
            gamepadController.SetEnabled(true);
    }

    private void SubscribeGameWindowDestroyed(
        nint hwnd,
        nint hudHwnd,
        GameWindowOverlayController overlays,
        GamepadController gamepadController)
    {
        // TODO: monitor parent destruction and attach to the next game window.
        _windowDestroyedSubscription?.Dispose();
        _windowDestroyedSubscription = GameWindowService.WindowDestroyed(_gameWindowHandle).Subscribe(_ =>
        {
            _gameWindowHandle = nint.Zero;
            DisposeGameWindowSubscriptions();
            gamepadController.SetEnabled(false);
            CloseGamepadWindow();
            overlays.HandleGameWindowDestroyed();
            WindowConfiguration.DetachEmbeddedWindow(hwnd);
            WindowConfiguration.DetachEmbeddedWindow(hudHwnd);
        });
    }

    private void DisposeGameWindowSubscriptions()
    {
        _clientSizeSubscription?.Dispose();
        _clientSizeSubscription = null;
        _windowDestroyedSubscription?.Dispose();
        _windowDestroyedSubscription = null;
        _batterySubscription?.Dispose();
        _batterySubscription = null;
        _batteryMonitor = null;
    }

    private void ToggleGamepadWindow()
    {
        if (_gamepadWindow is not null)
        {
            CloseGamepadWindow();
            return;
        }

        if (_gamepadController is not { IsEnabled: true })
            return;

        var mappings = GamepadController.Mappings
            .Select(static mapping => (mapping.Button, mapping.Key))
            .ToArray();
        var candidate = new WinUI.GamepadWindow(mappings);
        _gamepadWindow = candidate;
        _gamepadWindowClosedSubscription = candidate.Events().Closed.Subscribe(_ =>
        {
            if (!ReferenceEquals(_gamepadWindow, candidate))
                return;

            _gamepadWindow = null;
            var subscription = _gamepadWindowClosedSubscription;
            _gamepadWindowClosedSubscription = null;
            subscription?.Dispose();
        });
        candidate.ShowWithoutActivation();
    }

    private void CloseGamepadWindow()
    {
        var window = _gamepadWindow;
        _gamepadWindow = null;

        var subscription = _gamepadWindowClosedSubscription;
        _gamepadWindowClosedSubscription = null;
        subscription?.Dispose();
        window?.Close();
    }

    private void CloseHudWindow()
    {
        var hud = _hudWindow;
        _hudWindow = null;
        _hudWindowHandle = nint.Zero;
        hud?.Close();
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

        private void Apply()
        {
            if (_usesOriginalRegion)
            {
                OsPlatformApi.ResetWindowOriginalObservableRegion(hwnd);
                return;
            }

            if (_baseRegion is { } baseRegion)
                OsPlatformApi.SetWindowObservableRegions(hwnd, [baseRegion]);
            else
                OsPlatformApi.ResetWindowOriginalObservableRegion(hwnd);
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

