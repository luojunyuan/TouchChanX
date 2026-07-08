using System.Diagnostics;
using System.Text;
using Windows.Storage;

namespace TouchChanX.UWP;

public sealed class GameStorageService
{
    private const string GamesSettingKey = "Games";
    private const string GameEntrySeparator = "\n";
    private const string GameFieldSeparator = "\t";

    public IEnumerable<StoredGameEntry> Load()
    {
        if (ApplicationData.Current.LocalSettings.Values[GamesSettingKey] is not string value)
            return [];

        return value
            .Split(GameEntrySeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(ReadStoredGame)
            .Where(game => !string.IsNullOrWhiteSpace(game.Path));
    }

    public void Save(IEnumerable<GameEntry> games)
    {
        ApplicationData.Current.LocalSettings.Values[GamesSettingKey] =
            string.Join(
                GameEntrySeparator,
                games.Select(g =>
                    $"{EncodeSettingValue(g.Name.Value)}{GameFieldSeparator}{EncodeSettingValue(g.Path)}{GameFieldSeparator}{g.LastLaunchedTicks}"));
    }

    private static StoredGameEntry ReadStoredGame(string value)
    {
        var fields = value.Split(GameFieldSeparator);
        if (fields.Length != 3)
            return new();

        return new()
        {
            Name = DecodeSettingValue(fields[0]),
            Path = DecodeSettingValue(fields[1]),
            LastLaunchedTicks = long.TryParse(fields[2], out var ticks) ? ticks : 0,
        };
    }

    private static string EncodeSettingValue(string value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(value));

    private static string DecodeSettingValue(string value)
    {
        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(value));
        }
        catch (FormatException ex)
        {
            Debug.WriteLine(ex);
            return string.Empty;
        }
    }
}
