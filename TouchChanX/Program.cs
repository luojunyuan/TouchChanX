using TouchChanX.SplashScreenGdiPlus;
using TouchChanX.Win32;
using TouchChanX.Win32.Interop;
using TouchChanX.Persistence;

if (args.Length == 0)
{
    OsPlatformApi.MessageBox.Show("invalid game path");
    return;
}

var gamePathResult = GameStartup.PrepareValidGamePath(args[0]);
if (gamePathResult.IsFailure(out var pathError, out var gamePath))
{
    OsPlatformApi.MessageBox.Show(pathError.Message);
    return;
}

await using var fileStream = TouchChanX.AssetLoader.AppSplashIcon;

// 创建并显示启动画面，持续到 WinUI 窗口完成初始化
var splash = SplashScreen.Create(fileStream);
splash.Show();

var settings = new AppSettings();
var launcherPath = settings.ExternalLauncherEnabled ? settings.ExternalLauncherPath : null;
var launcherArgs = launcherPath is null ? null : settings.ExternalLauncherArgs;
var processResult = GameStartup.GetOrLaunchGameAsync(new GameLaunchOptions
    {
        GamePath = gamePath,
        LauncherPath = launcherPath,
        LauncherArguments = launcherArgs,
    })
    .GetAwaiter().GetResult();
if (processResult.IsFailure(out var processError, out var process))
{
    splash.Dispose();
    OsPlatformApi.MessageBox.Show(processError.Message);
    return;
}

// NOTE: 无论是 WPF 的 Owned 还是 WinUI 的 Child 窗口都跟随父进程结束而结束
process.EnableRaisingEvents = true;
process.Exited += (_, _) => Environment.Exit(0);

var handleResult = GameStartup.FindGoodWindowHandleAsync(process).GetAwaiter().GetResult();
if (handleResult.IsFailure(out var error, out var gameWindowHandle))
{
    splash.Dispose();
    switch (error)
    {
        case WindowHandleNotFoundError:
            OsPlatformApi.MessageBox.Show("Timeout! Failed to find a valid window of game");
            return;
        case ProcessExitedError:
        case ProcessPendingExitedError:
            return;
    }
}

OsPlatformApi.ActivateWindow(gameWindowHandle);

if (GameStartup.HasAttachedCurrentTouchChanX(gameWindowHandle))
{
    splash.Dispose();
    return;
}

if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) && OsPlatformApi.IsDpiUnaware(process))
    TouchChanX.WinUIApplication.ShowUnknownGameDpiNotification();

// NOTE: splash 由 WinUIApp.OnLaunched 在 window.Activate() 之后释放
TouchChanX.WinUIApplication.RunWithGameWindow(gameWindowHandle, splash);
