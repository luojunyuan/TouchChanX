using System.Globalization;
using TouchChanX.WinUI.Menu;
using Windows.Storage;

namespace TouchChanX.WinUI;

public static class TouchChanXSettings
{
    private const string TouchDockAnchorKey = "TouchDockAnchor";
    private const string TogglePrefix = "Toggle.";

    public static TouchDockAnchor LoadTouchDockAnchor()
    {
        var value = ReadString(TouchDockAnchorKey);
        return TryParseTouchDockAnchor(value, out var anchor)
            ? anchor
            : TouchDockAnchor.Default;
    }

    public static void SaveTouchDockAnchor(TouchDockAnchor anchor) =>
        WriteValue(TouchDockAnchorKey, FormatTouchDockAnchor(anchor));

    public static bool LoadToggleState(string id, bool defaultValue = false)
    {
        return ReadValue(TogglePrefix + id) switch
        {
            bool isOn => isOn,
            string value when bool.TryParse(value, out var isOn) => isOn,
            _ => defaultValue,
        };
    }

    public static void SaveToggleState(string id, bool isOn) =>
        WriteValue(TogglePrefix + id, isOn);

    private static object? ReadValue(string key) =>
        TryGetLocalSettings()?.Values[key];

    private static void WriteValue(string key, object value)
    {
        if (TryGetLocalSettings() is { } localSettings)
            localSettings.Values[key] = value;
    }

    private static ApplicationDataContainer? TryGetLocalSettings()
    {
        try
        {
            return ApplicationData.Current.LocalSettings;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static string? ReadString(string key) =>
        ReadValue(key) as string;

    private static string FormatTouchDockAnchor(TouchDockAnchor anchor) =>
        anchor switch
        {
            TouchDockAnchor.TopLeft => "TopLeft",
            TouchDockAnchor.TopRight => "TopRight",
            TouchDockAnchor.BottomLeft => "BottomLeft",
            TouchDockAnchor.BottomRight => "BottomRight",
            TouchDockAnchor.Left x => FormattableString.Invariant($"Left:{x.Scale}"),
            TouchDockAnchor.Top x => FormattableString.Invariant($"Top:{x.Scale}"),
            TouchDockAnchor.Right x => FormattableString.Invariant($"Right:{x.Scale}"),
            TouchDockAnchor.Bottom x => FormattableString.Invariant($"Bottom:{x.Scale}"),
            _ => FormatTouchDockAnchor(TouchDockAnchor.Default),
        };

    private static bool TryParseTouchDockAnchor(string? value, out TouchDockAnchor anchor)
    {
        anchor = TouchDockAnchor.Default;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        anchor = value switch
        {
            "TopLeft" => new TouchDockAnchor.TopLeft(),
            "TopRight" => new TouchDockAnchor.TopRight(),
            "BottomLeft" => new TouchDockAnchor.BottomLeft(),
            "BottomRight" => new TouchDockAnchor.BottomRight(),
            _ => anchor,
        };

        if (anchor != TouchDockAnchor.Default)
            return true;

        var parts = value.Split(':', 2);
        if (parts.Length != 2 ||
            !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var scale))
        {
            return false;
        }

        scale = Math.Clamp(scale, 0.0, 1.0);
        anchor = parts[0] switch
        {
            "Left" => new TouchDockAnchor.Left(scale),
            "Top" => new TouchDockAnchor.Top(scale),
            "Right" => new TouchDockAnchor.Right(scale),
            "Bottom" => new TouchDockAnchor.Bottom(scale),
            _ => TouchDockAnchor.Default,
        };

        return parts[0] is "Left" or "Top" or "Right" or "Bottom";
    }
}
