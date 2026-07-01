using TouchChanX.Win32;
using TouchChanX.Win32.Interop;

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

await using var fileStream = TouchChanX.AssetLoader.KleeHires;

var processResult = GameStartup.GetOrLaunchGameWithSplashAsync(gamePath, fileStream).GetAwaiter().GetResult();
if (processResult.IsFailure(out var processError, out var process))
{
    OsPlatformApi.MessageBox.Show(processError.Message);
    return;
}

// NOTE: 无论是 WPF 的 Owned 还是 WinUI 的 Child 窗口都跟随父进程结束而结束
process.EnableRaisingEvents = true;
process.Exited += (_, _) => Environment.Exit(0);

var handleResult = GameStartup.FindGoodWindowHandleAsync(process).GetAwaiter().GetResult();
if (handleResult.IsFailure(out var error, out var gameWindowHandle))
{
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

if (GameStartup.HasAttachedCurrentTouchChanX(gameWindowHandle))
{
    OsPlatformApi.ActivateWindow(gameWindowHandle);
    return;
}

TouchChanX.WinUIApplication.RunWithGameWindow(gameWindowHandle);
