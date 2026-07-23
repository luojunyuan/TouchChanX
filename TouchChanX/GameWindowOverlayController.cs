using Microsoft.UI.Xaml;
using R3;
using R3.ObservableEvents;
using System.Diagnostics;
using TouchChanX.Win32;
using TouchChanX.Win32.Interop;

namespace TouchChanX;

internal sealed partial class GameWindowOverlayController : IDisposable
{
    private readonly Action<string> _showMessage;
    private nint _gameWindowHandle;
    private System.Drawing.Size? _clientSize;
    private WinUI.DimWindow? _dimWindow;
    private nint _dimWindowHandle;
    private WinUI.LockWindow? _lockWindow;
    private IDisposable? _lockWindowInputSubscription;
    private GameProcessSuspension? _gameProcessSuspension;
    private nint _lockedGameWindowHandle;
    private bool _isDisposed;

    public GameWindowOverlayController(
        nint gameWindowHandle,
        Action<string> showMessage)
    {
        _gameWindowHandle = gameWindowHandle;
        _showMessage = showMessage;
    }

    public void UpdateClientSize(System.Drawing.Size size)
    {
        _clientSize = size;
        if (_dimWindowHandle != nint.Zero)
            OsPlatformApi.ResizeWindow(_dimWindowHandle, size);
    }

    public void Dim()
    {
        if (_isDisposed)
            return;

        EnsureDimWindow();
        _dimWindow!.Dim();
    }

    public void RestoreBrightness()
    {
        if (_isDisposed)
            return;

        CloseDimWindow();
    }

    public void OpenLockWindow()
    {
        var gameWindowHandle = _gameWindowHandle;
        if (_isDisposed ||
            _lockWindow is not null ||
            gameWindowHandle == nint.Zero ||
            !OsPlatformApi.IsWindow(gameWindowHandle))
        {
            return;
        }

        var candidate = new WinUI.LockWindow
        {
            SystemBackdrop = new TransparentBackdrop()
        };

        try
        {
            var candidateHwnd = WinRT.Interop.WindowNative.GetWindowHandle(candidate);
            WindowConfiguration.ConfigureStandaloneWindow(candidateHwnd);
            if (!OsPlatformApi.TryGetWindowRectangle(gameWindowHandle, out var gameWindowRectangle) ||
                !OsPlatformApi.PositionWindow(candidateHwnd, gameWindowRectangle))
            {
                throw new InvalidOperationException("Unable to position the lock window.");
            }

            _lockWindow = candidate;
            _lockedGameWindowHandle = gameWindowHandle;

            var freezeSubscription = candidate.FreezeRequested
                .Subscribe(_ => FreezeGame(candidate, gameWindowHandle));
            var unlockSubscription = candidate.UnlockRequested
                .Subscribe(_ => ReleaseGameSuspension(activateGame: false));
            var closeSubscription = candidate.CloseRequested
                .Subscribe(_ => CloseLockWindow(activateGame: true));
            var closedSubscription = candidate.Events().Closed
                .Subscribe(_ => CloseLockWindow(activateGame: true));
            _lockWindowInputSubscription = Disposable.Create(() =>
            {
                freezeSubscription.Dispose();
                unlockSubscription.Dispose();
                closeSubscription.Dispose();
                closedSubscription.Dispose();
            });

            CloseDimWindow();
            candidate.Activate();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to lock game: {ex}");
            if (ReferenceEquals(_lockWindow, candidate))
                CloseLockWindow(activateGame: false);
            else
                candidate.Close();

            _showMessage("无法锁定游戏");
        }
    }

    public void HandleGameWindowDestroyed()
    {
        _isDisposed = true;
        CloseLockWindow(activateGame: false);
        _gameWindowHandle = nint.Zero;
        CloseDimWindow();
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        CloseLockWindow(activateGame: false);
        CloseDimWindow();
    }

    private void EnsureDimWindow()
    {
        if (_dimWindow is not null)
            return;

        _dimWindow = new WinUI.DimWindow
        {
            SystemBackdrop = new TransparentBackdrop()
        };
        _dimWindowHandle = WinRT.Interop.WindowNative.GetWindowHandle(_dimWindow);
        WindowConfiguration.ConfigureEmbeddedWindow(_dimWindowHandle, _gameWindowHandle, clickThrough: true);
        if (_clientSize is { } size)
            OsPlatformApi.ResizeWindow(_dimWindowHandle, size);
        _dimWindow.Activate();
    }

    private void CloseDimWindow()
    {
        _dimWindow?.Close();
        _dimWindow = null;
        _dimWindowHandle = nint.Zero;
    }

    private bool FreezeGame(WinUI.LockWindow lockWindow, nint gameWindowHandle)
    {
        if (!ReferenceEquals(_lockWindow, lockWindow) ||
            _gameProcessSuspension is not null)
        {
            return false;
        }

        if (_gameWindowHandle != gameWindowHandle || !OsPlatformApi.IsWindow(gameWindowHandle))
        {
            _showMessage("无法冻结游戏");
            return false;
        }

        if (!GameProcessSuspension.TrySuspendForWindow(gameWindowHandle, out var suspension))
        {
            _showMessage("无法冻结游戏");
            return false;
        }

        _gameProcessSuspension = suspension;
        lockWindow.SetFrozenState(true);
        return true;
    }

    private void ReleaseGameSuspension(bool activateGame)
    {
        var gameWindowHandle = _lockedGameWindowHandle;
        var suspension = _gameProcessSuspension;
        _gameProcessSuspension = null;

        suspension?.Dispose();
        _lockWindow?.SetFrozenState(false);

        if (activateGame && gameWindowHandle != nint.Zero && OsPlatformApi.IsWindow(gameWindowHandle))
            OsPlatformApi.ActivateWindow(gameWindowHandle);
    }

    private void CloseLockWindow(bool activateGame)
    {
        var gameWindowHandle = _lockedGameWindowHandle;
        _lockedGameWindowHandle = nint.Zero;

        var suspension = _gameProcessSuspension;
        _gameProcessSuspension = null;

        var overlay = _lockWindow;
        _lockWindow = null;

        var inputSubscription = _lockWindowInputSubscription;
        _lockWindowInputSubscription = null;

        inputSubscription?.Dispose();
        suspension?.Dispose();
        overlay?.SetFrozenState(false);
        overlay?.Close();

        if (activateGame && gameWindowHandle != nint.Zero && OsPlatformApi.IsWindow(gameWindowHandle))
            OsPlatformApi.ActivateWindow(gameWindowHandle);
    }
}

internal static class WindowConfiguration
{
    public static void ConfigureEmbeddedWindow(nint hwnd, nint parent, bool clickThrough = false)
    {
        OsPlatformApi.ToggleWindowStyle(hwnd, WindowStyles.TiledWindow, false);
        OsPlatformApi.ToggleWindowStyle(hwnd, WindowStyles.Popup, false);
        // SetParent requires the child window style so focus follows the game window.
        OsPlatformApi.ToggleWindowStyle(hwnd, WindowStyles.Child, true);
        // Layered rendering is required for a WinUI window embedded as a child.
        OsPlatformApi.ToggleWindowExStyle(hwnd, ExtendedWindowStyles.Layered, true);

        if (clickThrough)
        {
            OsPlatformApi.ToggleWindowExStyle(hwnd, ExtendedWindowStyles.Transparent, true);
            OsPlatformApi.ToggleWindowExStyle(hwnd, ExtendedWindowStyles.NoActivate, true);
        }

        OsPlatformApi.SetParentWindowQwQ(hwnd, parent);
    }

    public static void DetachEmbeddedWindow(nint hwnd)
    {
        if (hwnd == nint.Zero || !OsPlatformApi.IsWindow(hwnd))
            return;

        OsPlatformApi.HideWindow(hwnd);
        OsPlatformApi.SetParentWindowQwQ(hwnd, nint.Zero);
        OsPlatformApi.ToggleWindowStyle(hwnd, WindowStyles.Child, false);
        OsPlatformApi.ToggleWindowStyle(hwnd, WindowStyles.Popup, true);
    }

    public static void RestoreEmbeddedWindow(nint hwnd, nint parent)
    {
        if (hwnd == nint.Zero ||
            parent == nint.Zero ||
            !OsPlatformApi.IsWindow(hwnd) ||
            !OsPlatformApi.IsWindow(parent))
        {
            return;
        }

        ConfigureEmbeddedWindow(hwnd, parent);
        OsPlatformApi.ShowWindowNoActivate(hwnd);
    }

    public static void ConfigureStandaloneWindow(nint hwnd)
    {
        // LockWindow is a standalone top-level window. It is not a child or owned window
        // of the game, so moving or suspending the game cannot move this window.
        OsPlatformApi.ToggleWindowStyle(hwnd, WindowStyles.Child, false);
        OsPlatformApi.ToggleWindowStyle(hwnd, WindowStyles.TiledWindow, false);
        OsPlatformApi.ToggleWindowStyle(hwnd, WindowStyles.Popup, true);
        OsPlatformApi.ToggleWindowExStyle(hwnd, ExtendedWindowStyles.Layered, true);
        OsPlatformApi.ToggleWindowExStyle(hwnd, ExtendedWindowStyles.AppWindow, true);
    }
}

internal partial class TransparentBackdrop : Microsoft.UI.Xaml.Media.SystemBackdrop { }
