using ObservableCollections;
using R3;
using System.Collections.Specialized;
using System.Diagnostics;
using TouchChanX.Persistence;
using Windows.Storage.Streams;
using Windows.System;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;

namespace TouchChanX.UWP;

public sealed class HomePageViewModel
{
    private static readonly TimeSpan LaunchCooldown = TimeSpan.FromSeconds(3);

    private readonly AppSettings _settings;
    private readonly Subject<Unit> _launchRequested = new();

    private readonly ObservableList<GameEntry> _games = [];

    public HomePageViewModel(AppSettings settings)
    {
        _settings = settings;
        _games.ObserveChanged()
            .Do(DisposeRemovedGames)
            .Subscribe(_ => EnsureSelection());

        LoadGames();
    }

    public Interaction<Unit, string?> PickGamePathInteraction { get; } = new();

    public Interaction<Unit, Unit> ShowLaunchFailedInteraction { get; } = new();

    public Interaction<GameEntry, string?> RenameGameInteraction { get; } = new();

    public ObservableListBindableView<GameEntry> GameItems => field ??= _games.ToBindableView();

    public BindableReactiveProperty<GameEntry?> SelectedGame { get; } = new(null);

    public BindableReactiveProperty<bool> HasGames => field ??=
        _games.ObserveChanged()
            .Select(_ => _games.Count > 0)
            .Prepend(_games.Count > 0)
            .ToBindableReactiveProperty(_games.Count > 0);

    public BindableReactiveProperty<string> SelectedGameName => field ??=
        SelectedGame
            .Select(game => game is null ? Observable.Return(string.Empty) : game.Name)
            .Switch()
            .ToBindableReactiveProperty(string.Empty);

    public ReactiveCommand AddGameCommand => field ??= new(async (_, _) => await AddGameFromPickerAsync());

    private Observable<bool> CanUseSelectedGame => HasGames;

    private Observable<bool> CanLaunchSelectedGame => Observable.CombineLatest(
        CanUseSelectedGame,
        _launchRequested
            .SelectMany(_ => Observable.Concat(
                Observable.Return(false),
                Observable.Timer(LaunchCooldown).Select(_ => true)))
            .Prepend(true),
        (hasGame, isReady) => hasGame && isReady);

    public ReactiveCommand<Unit> LaunchSelectedGameCommand => field ??=
        CanLaunchSelectedGame.ToReactiveCommand<Unit>(
            async (_, _) => await LaunchSelectedGameAsync(),
            initialCanExecute: false,
            awaitOperation: AwaitOperation.Drop);

    public ReactiveCommand<Unit> RenameSelectedGameCommand => field ??=
        CanUseSelectedGame.ToReactiveCommand<Unit>(
            async (_, _) => await RenameSelectedGameAsync(),
            initialCanExecute: false,
            awaitOperation: AwaitOperation.Drop);

    public ReactiveCommand<Unit> RemoveSelectedGameCommand => field ??=
        CanUseSelectedGame.ToReactiveCommand<Unit>(
            _ => RemoveSelectedGame(),
            initialCanExecute: false);

    public void AddGame(string path, string? name = null, long lastLaunchedTicks = 0, bool save = true)
    {
        if (!TryResolveGamePath(path, out var gamePath) ||
            _games.Any(g => string.Equals(g.Path, gamePath, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var game = CreateGameEntry(gamePath, name, lastLaunchedTicks);
        _games.Add(game);
        _ = LoadGameIconAsync(game);

        if (save)
            SaveGames();
    }

    private async Task AddGameFromPickerAsync()
    {
        var path = await PickGamePathInteraction.Handle(Unit.Default).FirstAsync();
        if (path is not null)
            AddGame(path);
    }

    private async Task LaunchSelectedGameAsync()
    {
        if (SelectedGame.Value is not { } game)
            return;

        _launchRequested.OnNext(Unit.Default);
        if (!await LaunchGameAsync(game))
        {
            await ShowLaunchFailedInteraction.Handle(Unit.Default).FirstAsync();
            return;
        }

        game.LastLaunchedTicks = DateTimeOffset.UtcNow.UtcTicks;
        MoveGameToFront(game);
        SaveGames();
    }

    private async Task RenameSelectedGameAsync()
    {
        if (SelectedGame.Value is not { } game)
            return;

        var newName = (await RenameGameInteraction.Handle(game).FirstAsync())?.Trim();
        if (string.IsNullOrWhiteSpace(newName))
            return;

        game.Name.Value = newName;
        SaveGames();
    }

    private void RemoveSelectedGame()
    {
        if (SelectedGame.Value is not { } game)
            return;

        _games.Remove(game);
        SaveGames();
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

    private GameEntry CreateGameEntry(string path, string? name = null, long lastLaunchedTicks = 0)
    {
        var game = new GameEntry(SelectedGame)
        {
            Path = path,
            LastLaunchedTicks = lastLaunchedTicks,
        };
        game.Name.Value = string.IsNullOrWhiteSpace(name) ? Path.GetFileNameWithoutExtension(path) : name;
        return game;
    }

    private void LoadGames()
    {
        foreach (var game in _settings.Games.ToStoredGames().OrderByDescending(game => game.LastLaunchedTicks))
        {
            AddGame(game.Path, game.Name, game.LastLaunchedTicks, save: false);
        }
    }

    private void SaveGames() =>
        _settings.Games = _games.ToSerializeString();

    private async Task LoadGameIconAsync(GameEntry game)
    {
        game.Icon.Value = await TryLoadIconAsync(game.Path);
    }

    private static async Task<bool> LaunchGameAsync(GameEntry game)
    {
        var uri = new Uri($"touchchanx://launch/?path={Uri.EscapeDataString(game.Path)}");
        return await Launcher.LaunchUriAsync(uri);
    }

    private static async Task<ImageSource?> TryLoadIconAsync(string path)
    {
        try
        {
            var iconBytes = await Task.Run(() => GameIconExtractor.TryExtractBestPng(path));
            if (iconBytes is null || iconBytes.Length == 0)
                return null;

            using var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
            {
                writer.WriteBytes(iconBytes);
                await writer.StoreAsync();
                await writer.FlushAsync();
            }

            stream.Seek(0);
            var image = new BitmapImage();
            await image.SetSourceAsync(stream);
            return image;
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            return null;
        }
    }

    private void MoveGameToFront(GameEntry game)
    {
        var currentIndex = _games.IndexOf(game);
        if (currentIndex > 0)
        {
            _games.Move(currentIndex, 0);
        }
    }

    private void EnsureSelection()
    {
        if (_games.Count == 0)
        {
            SelectedGame.Value = null;
            return;
        }

        if (SelectedGame.Value is not { } selectedGame ||
            !_games.Contains(selectedGame))
        {
            SelectedGame.Value = _games[0];
        }
    }

    private static void DisposeRemovedGames(CollectionChangedEvent<GameEntry> e)
    {
        if (e.Action is NotifyCollectionChangedAction.Remove 
            or NotifyCollectionChangedAction.Replace 
            or NotifyCollectionChangedAction.Reset)
        {
            e.OldItem.Dispose();
        }
    }
}
