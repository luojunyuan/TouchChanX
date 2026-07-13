using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using R3;
using TouchChanX.Persistence;
using WindowsShortcutFactory;

namespace TouchChanX.UWP;

public sealed class HomePageGameStore
{
    private readonly AppSettings _settings;
    private readonly ObservableCollection<GameEntryViewModel> _games = [];
    private readonly ReactiveProperty<bool> _hasGames = new(false);

    public HomePageGameStore(AppSettings settings)
    {
        _settings = settings;
        LoadGames();
    }

    // ponytail: UWP needs the concrete ObservableCollection for its native ABI projection;
    // add a custom IBindableObservableVector only if runtime mutation enforcement becomes necessary.
    public ObservableCollection<GameEntryViewModel> Games => _games;

    public Observable<bool> HasGames => _hasGames;

    public void Dispatch(GameCommand command)
    {
        if (!Apply(command))
            return;

        SaveGames();
        _hasGames.Value = _games.Count > 0;
    }

    private bool Apply(GameCommand command)
    {
        return command switch
        {
            GameCommand.Add(var path) => TryAddGame(path),
            GameCommand.AddRange(var paths) => TryAddGames(paths),
            GameCommand.Rename(var path, var name) => RenameGame(path, name),
            GameCommand.Remove(var path) => RemoveGame(path),
            GameCommand.MarkLaunched(var path, var ticks) => MarkGameLaunched(path, ticks),
            _ => false,
        };
    }

    private static bool TryResolveGamePath(string path, out string gamePath)
    {
        gamePath = string.Empty;

        if (string.IsNullOrWhiteSpace(path) ||
            !File.Exists(path) ||
            !IsSupportedGamePath(path))
        {
            return false;
        }

        try
        {
            if (Path.GetExtension(path).Equals(".lnk", StringComparison.OrdinalIgnoreCase))
            {
                using var shortcut = WindowsShortcut.Load(path);
                path = Environment.ExpandEnvironmentVariables(shortcut.Path ?? string.Empty);
            }
        }
        catch (Exception ex) when (ex is FileNotFoundException or COMException)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(path) ||
            !File.Exists(path) ||
            !Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        gamePath = path;
        return true;
    }

    private static bool IsSupportedGamePath(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase);
    }

    private bool TryAddGame(string path, string? name = null, long lastLaunchedTicks = 0)
    {
        if (!TryResolveGamePath(path, out var gamePath) || IndexOfPath(gamePath) >= 0)
        {
            return false;
        }

        _games.Add(new GameEntryViewModel(new GameEntry
        {
            Name = string.IsNullOrWhiteSpace(name) ? Path.GetFileNameWithoutExtension(gamePath) : name,
            Path = gamePath,
            LastLaunchedTicks = lastLaunchedTicks,
        }));
        return true;
    }

    private bool TryAddGames(IReadOnlyList<string> paths)
    {
        var changed = false;
        foreach (var path in paths)
        {
            changed |= TryAddGame(path);
        }

        return changed;
    }

    private bool RenameGame(string path, string name)
    {
        var index = IndexOfPath(path);
        if (index < 0 || _games[index].Name.Value == name)
            return false;

        _games[index].Name.Value = name;
        return true;
    }

    private bool RemoveGame(string path)
    {
        var index = IndexOfPath(path);
        if (index < 0)
            return false;

        _games.RemoveAt(index);
        return true;
    }

    private void LoadGames()
    {
        foreach (var game in _settings.Games.ToStoredGames().OrderByDescending(game => game.LastLaunchedTicks))
        {
            TryAddGame(game.Path, game.Name, game.LastLaunchedTicks);
        }

        _hasGames.Value = _games.Count > 0;
    }

    private void SaveGames() =>
        _settings.Games = _games.Select(game => new GameEntry
        {
            Name = game.Name.Value,
            Path = game.Path,
            LastLaunchedTicks = game.LastLaunchedTicks.Value,
        }).ToSerializeString();

    private bool MarkGameLaunched(string path, long lastLaunchedTicks)
    {
        var index = IndexOfPath(path);
        if (index < 0)
            return false;

        _games[index].LastLaunchedTicks.Value = lastLaunchedTicks;
        if (index > 0)
            _games.Move(index, 0);

        return true;
    }

    private int IndexOfPath(string path)
    {
        for (var index = 0; index < _games.Count; index++)
        {
            if (StringComparer.OrdinalIgnoreCase.Equals(_games[index].Path, path))
                return index;
        }

        return -1;
    }
}

public sealed record GameEntry
{
    public string Name { get; init; } = string.Empty;

    public string Path { get; init; } = string.Empty;

    public long LastLaunchedTicks { get; init; }
}

public abstract record GameCommand
{
    public sealed record Add(string Path) : GameCommand;

    public sealed record AddRange(IReadOnlyList<string> Paths) : GameCommand;

    public sealed record Rename(string Path, string Name) : GameCommand;

    public sealed record Remove(string Path) : GameCommand;

    public sealed record MarkLaunched(string Path, long LastLaunchedTicks) : GameCommand;
}
