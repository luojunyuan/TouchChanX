using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TouchChanX.UWP;

public static class GameEntrySerialization
{
    public static string ToSerializeString(this IEnumerable<GameEntry> games) =>
        JsonSerializer.Serialize(
            games.Select(game => new StoredGameEntry
            {
                Name = game.Name.Value,
                Path = game.Path,
                LastLaunchedTicks = game.LastLaunchedTicks,
            }).ToArray(),
            GameEntryJsonContext.Default.StoredGameEntryArray);

    public static IEnumerable<StoredGameEntry> ToStoredGames(this string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return [];

        try
        {
            return JsonSerializer.Deserialize(value, GameEntryJsonContext.Default.StoredGameEntryArray)?
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
[JsonSerializable(typeof(StoredGameEntry[]))]
internal sealed partial class GameEntryJsonContext : JsonSerializerContext;
