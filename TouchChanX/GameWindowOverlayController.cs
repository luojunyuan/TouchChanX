using Microsoft.UI.Xaml;
using R3;
using R3.ObservableEvents;
using System.Diagnostics;
using TouchChanX.Persistence;
using TouchChanX.Win32;
using TouchChanX.Win32.Interop;

namespace TouchChanX;

internal sealed partial class GameWindowOverlayController : IDisposable
{
    private readonly Action<string> _showMessage;
    private readonly Action _dim;
    private readonly Action _restoreBrightness;
    private nint _gameWindowHandle;
    private WinUI.LockWindow? _lockWindow;
    private nint _lockWindowHandle;
    private IDisposable? _lockWindowInputSubscription;
    private GameProcessSuspension? _gameProcessSuspension;
    private nint _lockedGameWindowHandle;
    private bool _isDisposed;

    public GameWindowOverlayController(
        nint gameWindowHandle,
        Action<string> showMessage,
        Action dim,
        Action restoreBrightness)
    {
        _gameWindowHandle = gameWindowHandle;
        _showMessage = showMessage;
        _dim = dim;
        _restoreBrightness = restoreBrightness;
    }

    public void Dim()
    {
        if (_isDisposed)
            return;

        _dim();
    }

    public void RestoreBrightness()
    {
        if (_isDisposed)
            return;

        _restoreBrightness();
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
            WindowConfiguration.ConfigureOwnedWindow(candidateHwnd, gameWindowHandle);
            if (!OsPlatformApi.TryGetWindowRectangle(gameWindowHandle, out var gameWindowRectangle) ||
                !OsPlatformApi.PositionWindow(candidateHwnd, gameWindowRectangle))
            {
                throw new InvalidOperationException("Unable to position the lock window.");
            }

            _lockWindow = candidate;
            _lockWindowHandle = candidateHwnd;
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

            _restoreBrightness();
            candidate.Activate();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to lock game: {ex}");
            if (ReferenceEquals(_lockWindow, candidate))
                CloseLockWindow(activateGame: false);
            else
                candidate.Close();

            _showMessage(LocalizedStrings.Current.ErrorUnableToLockGame);
        }
    }

    public void HandleGameWindowDestroyed()
    {
        _isDisposed = true;
        CloseLockWindow(activateGame: false);
        _gameWindowHandle = nint.Zero;
        _restoreBrightness();
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        CloseLockWindow(activateGame: false);
        _restoreBrightness();
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
            _showMessage(LocalizedStrings.Current.ErrorUnableToFreezeGame);
            return false;
        }

        var lockWindowHandle = _lockWindowHandle;
        if (lockWindowHandle == nint.Zero || !OsPlatformApi.IsWindow(lockWindowHandle))
        {
            _showMessage(LocalizedStrings.Current.ErrorUnableToFreezeGame);
            return false;
        }

        // An owned window follows the suspended game window. Detach it before suspending
        // the game so the lock controls remain responsive while the game is frozen.
        lockWindow.AppWindow.IsShownInSwitchers = false;
        WindowConfiguration.ConfigureStandaloneWindow(lockWindowHandle);

        if (!GameProcessSuspension.TrySuspendForWindow(gameWindowHandle, out var suspension))
        {
            WindowConfiguration.ConfigureOwnedWindow(lockWindowHandle, gameWindowHandle);
            _showMessage(LocalizedStrings.Current.ErrorUnableToFreezeGame);
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
        if (_lockWindow is not null)
        {
            WindowConfiguration.ConfigureOwnedWindow(_lockWindowHandle, gameWindowHandle);
            _lockWindow.SetFrozenState(false);
        }

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
        _lockWindowHandle = nint.Zero;

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

    public static void ConfigureOwnedWindow(nint hwnd, nint owner)
    {
        // Keep LockWindow top-level, then assign the game as its owner. Owned windows stay
        // above their owner and follow its minimize/destroy lifetime without being children.
        OsPlatformApi.ToggleWindowStyle(hwnd, WindowStyles.Child, false);
        OsPlatformApi.ToggleWindowStyle(hwnd, WindowStyles.TiledWindow, false);
        OsPlatformApi.ToggleWindowStyle(hwnd, WindowStyles.Popup, true);
        OsPlatformApi.ToggleWindowExStyle(hwnd, ExtendedWindowStyles.Layered, true);
        OsPlatformApi.ToggleWindowExStyle(hwnd, ExtendedWindowStyles.AppWindow, false);
        OsPlatformApi.SetOwnerWindow(hwnd, owner);
    }

    public static void ConfigureStandaloneWindow(nint hwnd)
    {
        // Remove the owner while the game is suspended so this window remains responsive.
        OsPlatformApi.ToggleWindowStyle(hwnd, WindowStyles.Child, false);
        OsPlatformApi.ToggleWindowStyle(hwnd, WindowStyles.TiledWindow, false);
        OsPlatformApi.ToggleWindowStyle(hwnd, WindowStyles.Popup, true);
        OsPlatformApi.ToggleWindowExStyle(hwnd, ExtendedWindowStyles.Layered, true);
        OsPlatformApi.ToggleWindowExStyle(hwnd, ExtendedWindowStyles.AppWindow, true);
        OsPlatformApi.SetOwnerWindow(hwnd, nint.Zero);
    }
}

internal partial class TransparentBackdrop : Microsoft.UI.Xaml.Media.SystemBackdrop { }
