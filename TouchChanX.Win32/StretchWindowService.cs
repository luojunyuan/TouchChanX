using TouchChanX.Win32.Interop;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace TouchChanX.Win32;

/// <summary>
/// 按游戏客户区比例把窗口铺到主屏幕，并支持恢复原始窗口状态。
/// </summary>
public static partial class StretchWindowService
{
    private const int OverlappedWindowStyle = 0x00CF0000;
    private const int CaptionStyle = 0x00C00000;

    private static nint _stretchedWindow;
    private static WindowState? _originalState;

    public static void Toggle(nint windowHandle)
    {
        if (_stretchedWindow == windowHandle)
        {
            Restore(windowHandle);
            return;
        }

        if (!HasTitleBar(windowHandle))
            return;

        Stretch(windowHandle);
    }

    public static void Stretch(nint windowHandle)
    {
        if (!HasTitleBar(windowHandle))
            return;

        if (_stretchedWindow == windowHandle)
            return;

        if (_stretchedWindow != nint.Zero)
            Restore(_stretchedWindow);

        if (!PInvoke.GetWindowRect(new(windowHandle), out var windowRect) ||
            !PInvoke.GetClientRect(new(windowHandle), out var clientRect))
        {
            return;
        }

        if (clientRect.Width <= 0 || clientRect.Height <= 0)
            return;

        var contentSize = FindContentSize(clientRect.Width, clientRect.Height);
        if (!TryGetStretchRectangle(contentSize.Width, contentSize.Height, out var stretchRect))
            return;

        _originalState = new(
            Style: PInvoke.GetWindowLong(new(windowHandle), WINDOW_LONG_PTR_INDEX.GWL_STYLE),
            Menu: PInvoke.GetMenu(new(windowHandle)),
            Rectangle: windowRect);
        _stretchedWindow = windowHandle;

        var borderlessStyle = _originalState.Value.Style & ~OverlappedWindowStyle;
        if (borderlessStyle != _originalState.Value.Style)
            _ = PInvoke.SetWindowLong(new(windowHandle), WINDOW_LONG_PTR_INDEX.GWL_STYLE, borderlessStyle);

        if (_originalState.Value.Menu != nint.Zero)
            _ = PInvoke.SetMenu(new(windowHandle), (HMENU)nint.Zero);

        if (!SetWindowPosition(
                windowHandle,
                stretchRect.Left,
                stretchRect.Top,
                stretchRect.Width,
                stretchRect.Height))
        {
            Restore(windowHandle);
        }
    }

    public static void Restore(nint windowHandle)
    {
        if (_stretchedWindow != windowHandle || _originalState is not { } originalState)
            return;

        _ = PInvoke.SetWindowLong(
            new(windowHandle),
            WINDOW_LONG_PTR_INDEX.GWL_STYLE,
            originalState.Style);
        _ = PInvoke.SetMenu(new(windowHandle), (HMENU)originalState.Menu);
        _ = SetWindowPosition(
            windowHandle,
            originalState.Rectangle.left,
            originalState.Rectangle.top,
            originalState.Rectangle.Width,
            originalState.Rectangle.Height);

        _stretchedWindow = nint.Zero;
        _originalState = null;
    }

    private static System.Drawing.Size FindContentSize(int width, int height)
    {
        var realAspect = (double)width / height;
        var targetAspect = Math.Abs(realAspect - 4.0 / 3) <= Math.Abs(realAspect - 16.0 / 9)
            ? 4.0 / 3
            : 16.0 / 9;

        return realAspect > targetAspect
            ? new((int)(height * targetAspect), height)
            : new(width, (int)(width / targetAspect));
    }

    private static bool HasTitleBar(nint windowHandle) =>
        windowHandle != nint.Zero &&
        OsPlatformApi.IsWindow(windowHandle) &&
        (PInvoke.GetWindowLong(new(windowHandle), WINDOW_LONG_PTR_INDEX.GWL_STYLE) & CaptionStyle) != 0;

    private static bool TryGetStretchRectangle(
        int contentWidth,
        int contentHeight,
        out StretchRectangle rectangle)
    {
        rectangle = default;
        if (contentWidth <= 0 || contentHeight <= 0)
            return false;

        var monitorWidth = Math.Max(1, PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CXSCREEN));
        var monitorHeight = Math.Max(1, PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CYSCREEN));

        int width;
        int height;
        if ((long)monitorWidth * contentHeight > (long)contentWidth * monitorHeight)
        {
            height = monitorHeight;
            width = (int)((long)monitorHeight * contentWidth / contentHeight);
        }
        else
        {
            width = monitorWidth;
            height = (int)((long)monitorWidth * contentHeight / contentWidth);
        }

        rectangle = new(
            Left: (monitorWidth - width) / 2,
            Top: (monitorHeight - height) / 2,
            Width: width,
            Height: height);
        return true;
    }

    private static bool SetWindowPosition(nint windowHandle, int x, int y, int width, int height) =>
        PInvoke.SetWindowPos(
            new(windowHandle),
            HWND.Null,
            x,
            y,
            width,
            height,
            SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_FRAMECHANGED) != 0;

    private readonly record struct WindowState(int Style, nint Menu, Windows.Win32.Foundation.RECT Rectangle);

    private readonly record struct StretchRectangle(int Left, int Top, int Width, int Height);
}
