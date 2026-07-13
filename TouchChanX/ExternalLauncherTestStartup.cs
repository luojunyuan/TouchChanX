using TouchChanX.Persistence;
using TouchChanX.Win32;
using TouchChanX.Win32.Interop;

namespace TouchChanX;

internal static class ExternalLauncherTestStartup
{
    public static bool TryHandle(string? argument)
    {
        if (argument?.Contains("test-external-launcher", StringComparison.OrdinalIgnoreCase) is not true)
            return false;

        var settings = new AppSettings();
        var gamePathResult = GameStartup.PrepareValidGamePath(argument);
        if (gamePathResult.IsFailure(out var pathError, out var gamePath))
        {
            OsPlatformApi.MessageBox.Show(pathError.Message);
            return true;
        }

        if (!ExternalLauncherConfiguration.IsValid(
            settings.ExternalLauncherPath,
            settings.ExternalLauncherArgs))
        {
            OsPlatformApi.MessageBox.Show("Invalid external launcher configuration.");
            return true;
        }

        var result = GameStartup.TestExternalLauncher(
            gamePath,
            settings.ExternalLauncherPath,
            settings.ExternalLauncherArgs);
        if (result.IsFailure(out var error))
            OsPlatformApi.MessageBox.Show(error.Message);

        return true;
    }
}
