using Microsoft.UI.Xaml;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using System.Diagnostics;
using TouchChanX.Win32.Interop;
using Windows.ApplicationModel;
using Windows.System;
using WinRT;

namespace TouchChanX;

public static class WinUIApplication
{
    public static void ShowUnknownGameDpiNotification()
    {
        var toast = new AppNotificationBuilder()
            .AddText("触控酱无法确定当前游戏的 DPI 缩放")
            .AddText("请考虑从触控酱重新启动游戏")
            .BuildNotification();

        // not work
        toast.Expiration = DateTimeOffset.Now.AddSeconds(10);

        AppNotificationManager.Default.Show(toast);
    }

    private static bool PrepareMsixDependency()
    {
        Package package;
        try
        {
            package = Package.Current;
        }
        catch (InvalidOperationException ex)
        {
            // 临时跳过调试时非打包项目直接运行
            Debug.WriteLine(ex);
            return true;
        }

        // 使用 MSIX 动态依赖包 API，强行修改静态包图的依赖顺序，注册 WindowsAppRuntime 依赖包到当前进程中
        var dependencyPackageList = package.Dependencies;
        // Microsoft.UI.Xaml.2.8
        // WindowsAppRuntime.2
        // Microsoft Visual C++ 2015 UWP Desktop Runtime Package
        // Microsoft Visual C++ 2015 UWP Runtime Package

        var windowsAppRuntimePackage = dependencyPackageList
            .FirstOrDefault(p => p.DisplayName.Contains("WindowsAppRuntime"));

        return windowsAppRuntimePackage is not null &&
            OsPlatformApi.TryRegisterDependency(
                windowsAppRuntimePackage.Id.FamilyName,
                package.Id.Architecture switch
                {
                    ProcessorArchitecture.Arm64 => PackageDependencyProcessorArchitectures.Arm64,
                    ProcessorArchitecture.X64 => PackageDependencyProcessorArchitectures.X64,
                    _ => throw new NotSupportedException("Unsupported architecture")
                });
    }

    public static void RunWithGameWindow(nint gameWindowHandle, IDisposable? splash = null)
    {
        bool succeed = PrepareMsixDependency();
        if (!succeed)
        {
            splash?.Dispose();
            return;
        }

        ComWrappersSupport.InitializeComWrappers();

        Application.Start(p =>
        {
            var app = new WinUIApp(gameWindowHandle, splash);
            // NOTE: 在 TouchChanX.UWP App.xaml 中引用 RailNavigationViewResources 后
            // 我们这里必须要在 OnLaunched 前调用 InitializeComponent()，否则会报错
            app.InitializeComponent();
        });
    }
}

public partial class WinUIApp(nint gameWindowHandle, IDisposable? splash = null)
{
    private WinUIAppController? _controller;

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _controller = new WinUIAppController(gameWindowHandle, splash);
        _controller.Start();
    }
}
