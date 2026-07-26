using Microsoft.UI.Xaml;
using R3;
using R3.ObservableEvents;
using System.Diagnostics;
using TouchChanX.Persistence;
using TouchChanX.Win32;
using TouchChanX.Win32.Battery;
using TouchChanX.Win32.Gamepad;
using TouchChanX.Win32.Interop;
using TouchChanX.Win32.Menu;

namespace TouchChanX;

/// <summary>
/// Represents one embedded game-window session.
/// </summary>
/// <remarks>
/// The completion observable emits when the game window is destroyed or the TouchChanX
/// window is closed. This lets the process controller react to the session without owning
/// any of its XAML resources or native subscriptions.
/// </remarks>
internal sealed partial class GameWindowLifetime(
    Process process,
    nint gameWindowHandle,
    bool isFirstGameWindow) : IDisposable
{
    private readonly Process _process = process;
    private readonly nint _gameWindowHandle = gameWindowHandle;
    private readonly bool _isFirstGameWindow = isFirstGameWindow;
    private readonly Subject<Unit> _completed = new();

    private WinUI.MainWindow? _mainWindow;
    private WinUI.HudWindow? _hudWindow;
    private GameWindowOverlayController? _overlays;
    private IDisposable? _clientSizeSubscription;
    private IDisposable? _windowDestroyedSubscription;
    private IDisposable? _batterySubscription;
    private BatteryMonitor? _batteryMonitor;
    private GamepadController? _gamepadController;
    private WinUI.GamepadWindow? _gamepadWindow;
    private IDisposable? _gamepadWindowClosedSubscription;
    private nint _touchWindowHandle;
    private nint _hudWindowHandle;
    private int _completionState;

    public Observable<Unit> Completed => _completed;

    public void Start()
    {
        try
        {
            StartCore();
        }
        catch (Exception ex)
        {
            FailWindowLifetime(ex);
        }
    }

    public void Dispose() => CompleteWindowLifetime();

    private void StartCore()
    {
        if (_isFirstGameWindow &&
            OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) &&
            OsPlatformApi.IsDpiUnaware(_process))
        {
            WinUIApplication.ShowUnknownGameDpiNotification();
        }

        OsPlatformApi.ActivateWindow(_gameWindowHandle);

        var mainWindow = CreateMainWindow();
        _mainWindow = mainWindow;
        var hudWindow = CreateHudWindow();
        _hudWindow = hudWindow;
        var gamepadController = new GamepadController(_gameWindowHandle);
        _gamepadController = gamepadController;
        var mainWindowHandle = WinRT.Interop.WindowNative.GetWindowHandle(mainWindow);
        var hudWindowHandle = WinRT.Interop.WindowNative.GetWindowHandle(hudWindow);
        _touchWindowHandle = mainWindowHandle;
        _hudWindowHandle = hudWindowHandle;
        var overlays = new GameWindowOverlayController(
            _gameWindowHandle,
            hudWindow.ShowMessage,
            hudWindow.Dim,
            hudWindow.RestoreBrightness);
        _overlays = overlays;

        mainWindow.SetGamepadFeatureAvailable(gamepadController.HasConnectedController);
        WindowConfiguration.ConfigureEmbeddedWindow(mainWindowHandle, _gameWindowHandle);
        WindowConfiguration.ConfigureEmbeddedWindow(
            hudWindowHandle,
            _gameWindowHandle,
            clickThrough: true);

        SubscribeClientSize(mainWindowHandle, hudWindowHandle);
        SubscribeObservableRegions(mainWindow, mainWindowHandle);
        SubscribeMenuCommands(mainWindow, overlays);
        SubscribeMenuToggles(mainWindow, mainWindowHandle, hudWindow, gamepadController);
        SubscribeGamepad(mainWindow, gamepadController);
        SubscribeGestures(mainWindow, hudWindow);
        ApplySettings(mainWindowHandle, hudWindow, gamepadController);

        mainWindow.InitializeBindings();
        mainWindow.Events().Closed.Subscribe(_ => CompleteWindowLifetime());
        SubscribeGameWindowDestroyed();

        if (Volatile.Read(ref _completionState) != 0)
            return;

        mainWindow.Activate();
        hudWindow.Activate();

        if (_isFirstGameWindow)
            WinUIApplication.SignalStartupCompleted();
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

    private void SubscribeClientSize(nint mainWindowHandle, nint hudWindowHandle)
    {
        // Keep the embedded window and its auxiliary overlays aligned with the game client area.
        _clientSizeSubscription = GameWindowService.ClientSizeChanged(_gameWindowHandle)
            .Subscribe(size =>
            {
                OsPlatformApi.ResizeWindow(mainWindowHandle, size);
                OsPlatformApi.ResizeWindow(hudWindowHandle, size);
            });
    }

    private static void SubscribeObservableRegions(WinUI.MainWindow window, nint windowHandle)
    {
        var observableRegions = new WindowObservableRegionSet(windowHandle);
        WinUI.Touch.TouchControl.ObservableRegionResetRequested
            .Merge(WinUI.Menu.MenuControl.ObservableRegionResetRequested)
            .TakeUntil(window.Events().Closed)
            .Subscribe(_ => observableRegions.UseOriginalRegion());
        WinUI.Touch.TouchControl.ObservableTouchRegionChanged
            .Select(touchRect => touchRect.Scale(window.Dpi).ToGdiRect())
            .TakeUntil(window.Events().Closed)
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
        nint windowHandle,
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
                    case "gesture":
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

                TouchMenuCommandService.SetToggleState(
                    toggle.Id,
                    toggle.IsOn,
                    _gameWindowHandle,
                    windowHandle);
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

        if (!isOn || !BatteryMonitor.IsAvailable())
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
        nint windowHandle,
        WinUI.HudWindow hudWindow,
        GamepadController gamepadController)
    {
        var settings = new AppSettings();
        if (settings.TouchToMouse)
            TouchMenuCommandService.SetToggleState("touch-to-mouse", true, _gameWindowHandle, windowHandle);
        if (settings.Gesture)
            TouchMenuCommandService.SetToggleState("gesture", true, _gameWindowHandle, windowHandle);
        if (settings.Battery && BatteryMonitor.IsAvailable())
            SetBatteryMonitoring(hudWindow, true);
        if (gamepadController.HasConnectedController &&
            (!settings.HasGamepadSetting || settings.Gamepad))
            gamepadController.SetEnabled(true);
    }

    private void SubscribeGameWindowDestroyed()
    {
        if (!OsPlatformApi.IsWindow(_gameWindowHandle))
        {
            CompleteWindowLifetime();
            return;
        }

        _windowDestroyedSubscription = GameWindowService.WindowDestroyed(_gameWindowHandle)
            .Subscribe(_ => CompleteWindowLifetime());

        // The game can disappear between the initial lookup and hook creation.
        if (!OsPlatformApi.IsWindow(_gameWindowHandle))
            CompleteWindowLifetime();
    }

    private void CompleteWindowLifetime()
    {
        if (Interlocked.Exchange(ref _completionState, 1) != 0)
            return;

        var touchWindowHandle = _touchWindowHandle;
        var hudWindowHandle = _hudWindowHandle;
        _touchWindowHandle = nint.Zero;
        _hudWindowHandle = nint.Zero;

        DisposeGameWindowSubscriptions();
        TouchMenuCommandService.DisconnectGameWindowInteractions();

        var gamepadController = _gamepadController;
        _gamepadController = null;
        gamepadController?.Dispose();
        CloseGamepadWindow();

        var overlays = _overlays;
        _overlays = null;
        overlays?.HandleGameWindowDestroyed();
        overlays?.Dispose();

        if (touchWindowHandle != nint.Zero)
            WindowConfiguration.DetachEmbeddedWindow(touchWindowHandle);
        if (hudWindowHandle != nint.Zero)
            WindowConfiguration.DetachEmbeddedWindow(hudWindowHandle);

        CloseHudWindow();
        CloseMainWindow();
        _completed.OnNext(Unit.Default);
        _completed.OnCompleted(Result.Success);
    }

    private void FailWindowLifetime(Exception exception)
    {
        if (Interlocked.Exchange(ref _completionState, 1) != 0)
            return;

        DisposeGameWindowSubscriptions();
        TouchMenuCommandService.DisconnectGameWindowInteractions();

        var gamepadController = _gamepadController;
        _gamepadController = null;
        gamepadController?.Dispose();
        CloseGamepadWindow();

        var overlays = _overlays;
        _overlays = null;
        overlays?.Dispose();

        if (_touchWindowHandle != nint.Zero)
            WindowConfiguration.DetachEmbeddedWindow(_touchWindowHandle);
        if (_hudWindowHandle != nint.Zero)
            WindowConfiguration.DetachEmbeddedWindow(_hudWindowHandle);

        CloseHudWindow();
        CloseMainWindow();
        _completed.OnCompleted(Result.Failure(exception));
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
        hud?.Close();
    }

    private void CloseMainWindow()
    {
        var window = _mainWindow;
        _mainWindow = null;
        if (window is null)
            return;

        try
        {
            window.Close();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to close TouchChanX window: {ex}");
        }
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
