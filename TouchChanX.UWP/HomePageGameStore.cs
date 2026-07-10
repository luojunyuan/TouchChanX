using ObservableCollections;
using R3;
using TouchChanX.Persistence;

namespace TouchChanX.UWP;

public sealed class HomePageGameStore
{
    private readonly AppSettings _settings;
    private readonly ObservableList<GameEntry> _games = [];
    private bool _isLoadingGames;
    private bool _isDispatching;
    private bool _saveRequested;

    public HomePageGameStore(AppSettings settings)
    {
        _settings = settings;
        _games.ObserveChanged()
            .Do(_ => RequestSave())
            .Subscribe();

        LoadGames();
    }

    public ObservableList<GameEntry> Games => _games;

    public Observable<bool> HasGames => field ??=
        _games.ObserveChanged()
            .Select(_ => _games.Count > 0)
            .Prepend(_games.Count > 0);

    public void Dispatch(GameCommand command)
    {
        _isDispatching = true;
        try
        {
            Apply(command);
        }
        finally
        {
            _isDispatching = false;
            SaveIfRequested();
        }
    }

    private void Apply(GameCommand command)
    {
        switch (command)
        {
            case GameCommand.Add(var path):
                TryAddGame(path);
                break;
            case GameCommand.AddRange(var paths):
                foreach (var path in paths)
                {
                    TryAddGame(path);
                }
                break;
            case GameCommand.Rename(var path, var name):
                ReplaceGame(path, name: name);
                break;
            case GameCommand.Remove(var path):
                RemoveGame(path);
                break;
            case GameCommand.MarkLaunched(var path, var lastLaunchedTicks):
                MarkGameLaunched(path, lastLaunchedTicks);
                break;
        }
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

        var resolvedPath = ShellLinkResolver.ResolveIfShortcut(path);
        if (string.IsNullOrWhiteSpace(resolvedPath) ||
            !File.Exists(resolvedPath) ||
            !Path.GetExtension(resolvedPath).Equals(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        gamePath = resolvedPath;
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

        _games.Add(new GameEntry
        {
            Name = string.IsNullOrWhiteSpace(name) ? Path.GetFileNameWithoutExtension(gamePath) : name,
            Path = gamePath,
            LastLaunchedTicks = lastLaunchedTicks,
        });
        return true;
    }

    private GameEntry? ReplaceGame(string path, string? name = null, long? lastLaunchedTicks = null)
    {
        var index = IndexOfPath(path);
        if (index < 0)
            return null;

        var game = _games[index];
        var replacement = new GameEntry
        {
            Name = name ?? game.Name,
            Path = game.Path,
            LastLaunchedTicks = lastLaunchedTicks ?? game.LastLaunchedTicks,
        };
        _games[index] = replacement;
        return replacement;
    }

    private void RemoveGame(string path)
    {
        var index = IndexOfPath(path);
        if (index >= 0)
            _games.RemoveAt(index);
    }

    private void LoadGames()
    {
        _isLoadingGames = true;
        try
        {
            foreach (var game in _settings.Games.ToStoredGames().OrderByDescending(game => game.LastLaunchedTicks))
            {
                TryAddGame(game.Path, game.Name, game.LastLaunchedTicks);
            }
        }
        finally
        {
            _isLoadingGames = false;
        }
    }

    private void SaveGames() =>
        _settings.Games = _games.ToSerializeString();

    private void MoveGameToFront(string path)
    {
        var currentIndex = IndexOfPath(path);
        if (currentIndex > 0)
            _games.Move(currentIndex, 0);
    }

    private void MarkGameLaunched(string path, long lastLaunchedTicks)
    {
        if (ReplaceGame(path, lastLaunchedTicks: lastLaunchedTicks) is not null)
            MoveGameToFront(path);
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

    private void RequestSave()
    {
        if (_isLoadingGames)
            return;

        if (_isDispatching)
        {
            _saveRequested = true;
            return;
        }

        SaveGames();
    }

    private void SaveIfRequested()
    {
        if (!_saveRequested)
            return;

        _saveRequested = false;
        SaveGames();
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
