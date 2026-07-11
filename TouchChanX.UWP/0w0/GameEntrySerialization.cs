using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TouchChanX.UWP;

public static class GameEntrySerialization
{
    public static string ToSerializeString(this IEnumerable<GameEntry> games) =>
        JsonSerializer.Serialize([.. games], GameEntryJsonContext.Default.GameEntryArray);

    public static IEnumerable<GameEntry> ToStoredGames(this string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return [];

        try
        {
            return JsonSerializer.Deserialize(value, GameEntryJsonContext.Default.GameEntryArray)?
                .Where(game => !string.IsNullOrWhiteSpace(game.Path)) ?? [];
        }
        catch (JsonException ex)
        {
            Debug.WriteLine(ex);
            return [];
        }
    }
}

[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(GameEntry[]))]
internal sealed partial class GameEntryJsonContext : JsonSerializerContext;
