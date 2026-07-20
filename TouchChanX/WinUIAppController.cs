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

    private partial class TransparentBackdrop : Microsoft.UI.Xaml.Media.SystemBackdrop { }

    public void Start()
    {
        ObservableSystem.RegisterUnhandledExceptionHandler(ex => Debug.WriteLine(ex.ToString()));
        ObservableSystem.DefaultTimeProvider = WinUI3DispatcherTimeProvider.Default;

        var window = new WinUI.MainWindow()
        {
            SystemBackdrop = new TransparentBackdrop()
        };
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        ConfigureEmbeddedWindow(hwnd, _gameWindowHandle);
        WinUI.DimWindow? dimWindow = null;
        nint dimHwnd = nint.Zero;
        System.Drawing.Size? clientSize = null;

        // Keep both embedded windows aligned with the game client area.
        GameWindowService.ClientSizeChanged(_gameWindowHandle)
            .Subscribe(size =>
            {
                clientSize = size;
                OsPlatformApi.ResizeWindow(hwnd, size);
                if (dimHwnd != nint.Zero)
                    OsPlatformApi.ResizeWindow(dimHwnd, size);
            });

        void EnsureDimWindow()
        {
            if (dimWindow is not null)
                return;

            dimWindow = new WinUI.DimWindow()
            {
                SystemBackdrop = new TransparentBackdrop()
            };
            dimHwnd = WinRT.Interop.WindowNative.GetWindowHandle(dimWindow);
            ConfigureEmbeddedWindow(dimHwnd, _gameWindowHandle, clickThrough: true);
            if (clientSize is { } size)
                OsPlatformApi.ResizeWindow(dimHwnd, size);
            dimWindow.Activate();
        }

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
                        EnsureDimWindow();
                        dimWindow!.Dim();
                        return;
                    case "brightness-up":
                        dimWindow?.Close();
                        dimWindow = null;
                        dimHwnd = nint.Zero;
                        return;
                }

                await TouchMenuCommandService.ExecuteAsync(commandId, _gameWindowHandle);
            });

        WinUI.Menu.MenuControl.ObservableToggleChanged
            .TakeUntil(window.Events().Closed)
            .Subscribe(toggle =>
                TouchMenuCommandService.SetToggleState(toggle.Id, toggle.IsOn, _gameWindowHandle, hwnd));

        TouchMenuCommandService.ObservableGestureRecognized
            .TakeUntil(window.Events().Closed)
            .Select(GetGestureMessage)
            .Subscribe(window.ShowMessage);

        var settings = new AppSettings();
        if (settings.TouchToMouse)
            TouchMenuCommandService.SetToggleState("touch-to-mouse", true, _gameWindowHandle, hwnd);
        if (settings.Gesture)
            TouchMenuCommandService.SetToggleState("gesture", true, _gameWindowHandle, hwnd);

        // TODO: monitor parent destruction and attach to the next game window.
        GameWindowService.WindowDestroyed(_gameWindowHandle).Subscribe(_ =>
        {
            _gameWindowHandle = nint.Zero;

            OsPlatformApi.SetParentWindowQwQ(hwnd, _gameWindowHandle);
            if (dimHwnd != nint.Zero)
            {
                dimWindow?.Close();
                dimWindow = null;
                dimHwnd = nint.Zero;
            }
            GameWindowService.ClientSizeChanged(_gameWindowHandle)
                .Subscribe(size => OsPlatformApi.ResizeWindow(hwnd, size));
        });

        window.InitializeBindings();
        window.Events().Closed.Subscribe(_ => dimWindow?.Close());
        window.Activate();

        _splash?.Dispose();
    }

    private static void ConfigureEmbeddedWindow(nint hwnd, nint parent, bool clickThrough = false)
    {
        OsPlatformApi.ToggleWindowStyle(hwnd, WindowStyles.TiledWindow, false);
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
