using System.Diagnostics;
using System.Globalization;
using Windows.Storage;

namespace TouchChanX.Persistence;

public sealed class AppSettings
{
    private const string GamesKey = "Games";
    private const string TouchDockAnchorKey = "TouchDockAnchor";
    private const string TogglePrefix = "Toggle.";
    private static ApplicationDataContainer? CachedLocalSettings;
    private static bool LocalSettingsResolved;

    private readonly ApplicationDataContainer? _localSettings = GetLocalSettings();

    public string Games
    {
        get => ReadString(GamesKey) ?? string.Empty;
        set => WriteString(GamesKey, value);
    }

    public string TouchDockAnchor
    {
        get => ReadString(TouchDockAnchorKey) ?? string.Empty;
        set => WriteString(TouchDockAnchorKey, value);
    }

    public bool Stretch
    {
        get => ReadBool(TogglePrefix + "stretch");
        set => WriteBool(TogglePrefix + "stretch", value);
    }

    public bool TouchBar
    {
        get => ReadBool(TogglePrefix + "touch-bar");
        set => WriteBool(TogglePrefix + "touch-bar", value);
    }

    public bool Keyboard
    {
        get => ReadBool(TogglePrefix + "keyboard");
        set => WriteBool(TogglePrefix + "keyboard", value);
    }

    public bool TouchToMouse
    {
        get => ReadBool(TogglePrefix + "touch-to-mouse");
        set => WriteBool(TogglePrefix + "touch-to-mouse", value);
    }

    public bool Battery
    {
        get => ReadBool(TogglePrefix + "battery");
        set => WriteBool(TogglePrefix + "battery", value);
    }

    public bool Gesture
    {
        get => ReadBool(TogglePrefix + "gesture");
        set => WriteBool(TogglePrefix + "gesture", value);
    }

    private string? ReadString(string key) =>
        _localSettings?.Values[key] as string;

    private void WriteString(string key, string value)
    {
        if (_localSettings is not null)
            _localSettings.Values[key] = value;
    }

    private bool ReadBool(string key) =>
        bool.TryParse(ReadString(key), out var value) && value;

    private void WriteBool(string key, bool value) =>
        WriteString(key, value.ToString(CultureInfo.InvariantCulture));

    private static ApplicationDataContainer? GetLocalSettings()
    {
        if (LocalSettingsResolved)
            return CachedLocalSettings;

        try
        {
            CachedLocalSettings = ApplicationData.Current.LocalSettings;
        }
        catch (InvalidOperationException ex)
        {
            Debug.WriteLine(ex);
        }

        LocalSettingsResolved = true;
        return CachedLocalSettings;
    }
}
