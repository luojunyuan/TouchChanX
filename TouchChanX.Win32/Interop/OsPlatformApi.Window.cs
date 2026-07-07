using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace TouchChanX.Win32.Interop;

public static partial class OsPlatformApi // Window
{
    /// <summary>
    /// 激活窗口
    /// </summary>
    public static void ActivateWindow(nint hwnd)
    {
        PInvoke.ShowWindow(new(hwnd), SHOW_WINDOW_CMD.SW_RESTORE);
        PInvoke.SetForegroundWindow(new(hwnd));
    }

    /// <summary>
    /// 隐藏窗口
    /// </summary>
    public static bool HideWindow(nint hWnd) => PInvoke.ShowWindow(new(hWnd), SHOW_WINDOW_CMD.SW_HIDE);

    /// <summary>
    /// 在窗口处于最小化时恢复窗口
    /// </summary>
    public static void RestoreWindowQwQ(nint windowHandle)
    {
        if (PInvoke.IsIconic(new(windowHandle)))
            PInvoke.ShowWindow(new(windowHandle), SHOW_WINDOW_CMD.SW_RESTORE);
    }

    /// <summary>
    /// 获取客户区窗口大小
    /// </summary>
    public static Size GetWindowSize(nint hwnd)
    {
        PInvoke.GetClientRect(new(hwnd), out var initRect);
        return initRect.Size;
    }

    /// <summary>
    /// 判断屏幕坐标是否命中 Touch 窗口或其子窗口
    /// </summary>
    public static bool IsPointInsideWindowOrChild(Point point, nint windowHandle)
    {
        if (windowHandle == nint.Zero)
            return false;

        var hitWindow = PInvoke.WindowFromPoint(point);
        return hitWindow == new HWND(windowHandle) || PInvoke.IsChild(new HWND(windowHandle), hitWindow);
    }

    /// <summary>
    /// 判断屏幕坐标是否位于窗口客户区内
    /// </summary>
    public static bool IsPointInsideClientArea(Point screenPoint, nint windowHandle)
    {
        var clientOrigin = new Point();
        if (!PInvoke.ClientToScreen(new(windowHandle), ref clientOrigin) ||
            !PInvoke.GetClientRect(new(windowHandle), out var clientRect))
        {
            return false;
        }

        return screenPoint.X >= clientOrigin.X &&
            screenPoint.X < clientOrigin.X + clientRect.Width &&
            screenPoint.Y >= clientOrigin.Y &&
            screenPoint.Y < clientOrigin.Y + clientRect.Height;
    }

    /// <summary>
    /// 调整客户区窗口大小
    /// </summary>
    /// <remarks>
    /// NOTE: 我没有观测到 Repaint 设置为 false 带来的任何负面影响
    /// </remarks>
    public static void ResizeWindow(nint hwnd, Size size) =>
        PInvoke.MoveWindow(new(hwnd), 0, 0, size.Width, size.Height, false);

    /// <summary>
    /// 设置窗口的父窗口
    /// </summary>
    public static bool SetParentWindowQwQ(nint child, nint parent) => 
        PInvoke.SetParent(new(child), new(parent)) != HWND.Null;

    /// <summary>
    /// 设置窗口的 Owner 窗口
    /// </summary>
    public static void SetOwnerWindow(nint child, nint parent)
    {
        Marshal.SetLastPInvokeError(0);

        if (PInvoke.SetWindowLongPtr(new(child), WINDOW_LONG_PTR_INDEX.GWL_HWNDPARENT, parent) == 0 &&
            Marshal.GetLastPInvokeError() != 0)
            throw new Win32Exception();
    }

    /// <summary>
    /// 设置窗口的 WindowStyle
    /// </summary>
    public static void ToggleWindowStyle(nint hwnd, WindowStyles style, bool enable)
    {
        var oldStyle = (WindowStyles)PInvoke.GetWindowLong(new HWND(hwnd), WINDOW_LONG_PTR_INDEX.GWL_STYLE);
        var newStyle = enable ? oldStyle | style : oldStyle & ~style;
        if (PInvoke.SetWindowLong(new HWND(hwnd), WINDOW_LONG_PTR_INDEX.GWL_STYLE, (int)newStyle) != (int)oldStyle)
            throw new Win32Exception();
    }

    /// <summary>
    /// 设置窗口的 ExtendedWindowStyle
    /// </summary>
    public static void ToggleWindowExStyle(nint hwnd, ExtendedWindowStyles style, bool enable)
    {
        var oldStyle = (ExtendedWindowStyles)PInvoke.GetWindowLong(new HWND(hwnd), WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        var newStyle = enable ? oldStyle | style : oldStyle & ~style;
        if (PInvoke.SetWindowLong(new HWND(hwnd), WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, (int)newStyle) != (int)oldStyle)
            throw new Win32Exception();
    }

    /// <summary>
    /// 恢复窗口原始可观测区域
    /// </summary>
    public static void ResetWindowOriginalObservableRegion(nint hwnd)
    {
        PInvoke.GetClientRect(new(hwnd), out var initRect);
        SetWindowObservableRegion(hwnd, new(Point.Empty, initRect.Size));
    }

    /// <summary>
    /// 设置窗口可以被观测和点击的区域
    /// </summary>
    public static void SetWindowObservableRegion(nint hwnd, Rectangle rect) =>
        SetWindowObservableRegions(hwnd, [rect]);

    /// <summary>
    /// 设置窗口可以被观测和点击的区域
    /// </summary>
    public static void SetWindowObservableRegions(nint hwnd, IReadOnlyCollection<Rectangle> rects)
    {
        var combinedRegion = PInvoke.CreateRectRgn(0, 0, 0, 0);

        foreach (var rect in rects.Where(static r => r.Width > 0 && r.Height > 0))
        {
            var rectRegion = PInvoke.CreateRectRgn(rect.X, rect.Y, rect.Right, rect.Bottom);
            _ = PInvoke.CombineRgn(combinedRegion, combinedRegion, rectRegion, RGN_COMBINE_MODE.RGN_OR);
            _ = PInvoke.DeleteObject((HGDIOBJ)rectRegion);
        }

        if (PInvoke.SetWindowRgn(new(hwnd), combinedRegion, true) == 0)
            _ = PInvoke.DeleteObject((HGDIOBJ)combinedRegion);
    }
}

[Flags]
public enum WindowStyles : uint
{
    ClipChildren = WINDOW_STYLE.WS_CLIPCHILDREN,
    TiledWindow = WINDOW_STYLE.WS_TILEDWINDOW,
    Popup = WINDOW_STYLE.WS_POPUP,
    Child = WINDOW_STYLE.WS_CHILD,
    MinimizeBox = WINDOW_STYLE.WS_MINIMIZEBOX,
    MaximizeBox = WINDOW_STYLE.WS_MAXIMIZEBOX,
}

[Flags]
public enum ExtendedWindowStyles : uint
{
    Layered = WINDOW_EX_STYLE.WS_EX_LAYERED,
    AppWindow = WINDOW_EX_STYLE.WS_EX_APPWINDOW,
}
