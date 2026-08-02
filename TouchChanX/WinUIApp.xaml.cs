using Microsoft.UI.Xaml;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using System.Diagnostics;
using TouchChanX.Persistence;
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
            .AddText(LocalizedStrings.Current.UnknownDpiTitle)
            .AddText(LocalizedStrings.Current.UnknownDpiContent)
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

    public static void RunWithGameProcess(Process process)
    {
        // WinUI owns one process-scoped dispatcher; game windows rotate inside it.
        if (!MsixDependencyPrepared.Value)
        {
            SignalStartupCompleted();
            return;
        }

        _ = ComWrappersInitialized.Value;

        try
        {
            Application.Start(_initializationParams =>
            {
                new WinUIApp(process);
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WinUI application failed: {ex}");
            SignalStartupCompleted();
        }

    }
}

public partial class WinUIApp : Application
{
    private readonly Process _process;
    private WinUIAppController? _controller;

    public WinUIApp(Process process)
    {
        _process = process;
        // NOTE: 在 TouchChanX.UWP App.xaml 中引用 RailNavigationViewResources 后
        // 我们这里必须要在 OnLaunched 前调用 InitializeComponent()，否则会报错
        InitializeComponent();
    }

    // Required by the generated XAML entry point. The packaged entry path
    // supplies the game process through the constructor above.
    public WinUIApp()
        : this(Process.GetCurrentProcess())
    {
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;
        _controller = new WinUIAppController(_process);
        _controller.Start();
    }
}
