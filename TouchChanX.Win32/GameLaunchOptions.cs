namespace TouchChanX.Win32;

public sealed record GameLaunchOptions
{
    public required string GamePath { get; init; }

    public string? LauncherPath { get; init; }

    public string? LauncherArguments { get; init; }
}
