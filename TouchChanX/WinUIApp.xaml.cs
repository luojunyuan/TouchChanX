using System.Diagnostics;
using Microsoft.UI.Xaml;
using R3;
using TouchChanX.Win32;
using TouchChanX.Win32.Interop;
using Windows.ApplicationModel;
using Windows.System;
using WinRT;

namespace TouchChanX;

public static class WinUIApplication
{
    private static bool PrepareMsixDependency()
    {
        ComWrappersSupport.InitializeComWrappers();

        try
        {
            // 使用 MSIX 动态依赖包 API，强行修改静态包图的依赖顺序，注册 WindowsAppRuntime 依赖包到当前进程中
            var dependencyPackageList = Package.Current.Dependencies;
            // Microsoft.UI.Xaml.2.8
            // WindowsAppRuntime.2
            // Microsoft Visual C++ 2015 UWP Desktop Runtime Package
            // Microsoft Visual C++ 2015 UWP Runtime Package

            var windowsAppRuntimePackage = dependencyPackageList
                .FirstOrDefault(p => p.DisplayName.Contains("WindowsAppRuntime"));

            if (windowsAppRuntimePackage is null)
                return false;

            if (!OsPlatformApi.TryRegisterDependency(
                windowsAppRuntimePackage.Id.FamilyName,
                Package.Current.Id.Architecture switch
                {
                    ProcessorArchitecture.Arm64 => PackageDependencyProcessorArchitectures.Arm64,
                    ProcessorArchitecture.X64 => PackageDependencyProcessorArchitectures.X64,
                    _ => throw new NotSupportedException("Unsupported architecture")
                }))
            {
                return false;
            }
        }
        catch (InvalidOperationException ex)
        {
            // 临时跳过调试时非打包项目直接运行
            Debug.WriteLine(ex);
        }

        return true;
    }

    public static void RunWithGameWindow(nint gameWindowHandle)
    {
        bool succeed = PrepareMsixDependency();
        if (!succeed)
            return;

        Application.Start(p => _ = new WinUIApp(gameWindowHandle));
    }
}

public partial class WinUIApp(nint gameWindowHandle)
{
    private partial class TransparentBackdrop : Microsoft.UI.Xaml.Media.SystemBackdrop { }

    /// <summary>
    /// WinUI 程序窗口入口点事件函数
    /// </summary>
    /// <remarks>QwQ: 耗时方法</remarks>
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var window = new WinUI.MainWindow()
        {
            SystemBackdrop = new TransparentBackdrop()
        };
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        OsPlatformApi.ToggleWindowStyle(hwnd, WindowStyles.TiledWindow, false);
        // SetParent 的基础条件之一，可以让子窗口与父窗口统一焦点
        OsPlatformApi.ToggleWindowStyle(hwnd, WindowStyles.Child, true);
        // 必须给 WinUI 外层 Win32 窗口添加 Layered 样式，以使窗口作为子窗口时分层背景正常可见
        OsPlatformApi.ToggleWindowExStyle(hwnd, ExtendedWindowStyles.Layered, true);
        // NOTE: 设置为子窗口后，window.AppWindow 会返回 null、Activated 事件一次也不激活了
        OsPlatformApi.SetParentWindowQwQ(hwnd, gameWindowHandle);

        // 确保在设置为子窗口后，重定位对齐父窗口左上角
        GameWindowService.ClientSizeChanged(gameWindowHandle)
            .Subscribe(size => OsPlatformApi.ResizeWindow(hwnd, size));

        WinUI.Touch.TouchControl.ObservableRegionResetRequested
            .Merge(WinUI.Menu.MenuControl.ObservableRegionResetRequested)
            .Subscribe(_ => OsPlatformApi.ResetWindowOriginalObservableRegion(hwnd));
        WinUI.Touch.TouchControl.ObservableTouchRegionChanged
            .Select(touchRect => touchRect.Scale(window.Dpi).ToGdiRect())
            .Subscribe(rect => OsPlatformApi.SetWindowObservableRegion(hwnd, rect));

        // TODO: 监视父窗口销毁事件，把窗口设置到新的 gameWindowHandle 上
        GameWindowService.WindowDestroyed(gameWindowHandle).Subscribe(_ =>
        {
            // 会循环询找新的窗口，直到找到一个有效的窗口或者超时就直接退出程序
            gameWindowHandle = nint.Zero;
            System.Diagnostics.Debug.WriteLine("Game window destroyed");

            OsPlatformApi.SetParentWindowQwQ(hwnd, gameWindowHandle);
            GameWindowService.ClientSizeChanged(gameWindowHandle)
                .Subscribe(size => OsPlatformApi.ResizeWindow(hwnd, size));
        });

        window.InitializeBindings();
        window.Activate();
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