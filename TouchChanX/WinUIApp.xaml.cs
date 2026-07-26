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
    private static readonly Lazy<bool> MsixDependencyPrepared = new(PrepareMsixDependency);
    private static readonly Lazy<bool> ComWrappersInitialized = new(() =>
    {
        ComWrappersSupport.InitializeComWrappers();
        return true;
    });

    private static Action? _startupCompletedCallback;

    internal static void SetStartupCompletedCallback(Action callback) =>
        _startupCompletedCallback = callback;

    internal static void SignalStartupCompleted() =>
        Interlocked.Exchange(ref _startupCompletedCallback, null)?.Invoke();

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

    public static bool RunWithGameWindow(nint gameWindowHandle)
    {
        if (!MsixDependencyPrepared.Value)
        {
            SignalStartupCompleted();
            return false;
        }

        _ = ComWrappersInitialized.Value;

        Application.Start(p =>
        {
            var context = new Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(
                Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new WinUIApp(gameWindowHandle);
        });

        return true;
    }
}

public partial class WinUIApp : Application
{
    private readonly nint _gameWindowHandle;
    private WinUIAppController? _controller;

    public WinUIApp(nint gameWindowHandle)
    {
        _gameWindowHandle = gameWindowHandle;
        InitializeComponent();
    }

    public WinUIApp()
        : this(nint.Zero)
    {
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _controller = new WinUIAppController(_gameWindowHandle);
        _controller.Start();
    }
}
