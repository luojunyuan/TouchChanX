using System.Diagnostics;
using System.Text;
using R3;
using R3.ObservableEvents;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;

namespace TouchChanX.UWP;

public sealed partial class HomePage : Page
{
    private const string GamesSettingKey = "Games";
    private const string GamesSettingVersion3Prefix = "v3\n";
    private const string GamesSettingVersion2Prefix = "v2\n";
    private const char GameEntrySeparator = '\u001e';
    private const char GamePathSeparator = '\u001f';
    private const int LaunchCooldownMilliseconds = 3000;

    private List<GameEntry> Games { get; } = [];

    public BindableReactiveProperty<Visibility> EmptyStateVisibility { get; } = new(Visibility.Visible);

    public BindableReactiveProperty<Visibility> GameListVisibility { get; } = new(Visibility.Collapsed);

    public BindableReactiveProperty<Visibility> SelectedGameActionsVisibility { get; } = new(Visibility.Collapsed);

    public BindableReactiveProperty<string> SelectedGameName { get; } = new(string.Empty);

    public BindableReactiveProperty<bool> CanLaunchSelectedGame { get; } = new(false);

    private ReactiveProperty<bool> IsLaunchCooldownActive { get; } = new(false);

    private BindableReactiveProperty<GameEntry?> SelectedGame { get; } = new(null);

    public HomePage()
    {
        InitializeComponent();
        BindReactiveInteractions();
        LoadGames();
        UpdateGameListState();
    }

    private void BindReactiveInteractions()
    {
        DropArea.Events().DragOver
            .Where(e => e.DataView.Contains(StandardDataFormats.StorageItems))
            .Subscribe(e =>
            {
                e.AcceptedOperation = DataPackageOperation.Copy;
                e.DragUIOverride.Caption = "添加到 TouchChanX";
                e.DragUIOverride.IsCaptionVisible = true;
            });

        DropArea.Events().Drop
            .Where(e => e.DataView.Contains(StandardDataFormats.StorageItems))
            .SubscribeAwait(AddDroppedGamesAsync);

        GameList.Events().SelectionChanged
            .Select(_ => GetSelectedGame())
            .Subscribe(game =>
            {
                SelectedGame.Value = game;
                UpdateSelectedGameState();
            });

        GameList.Events().DoubleTapped
            .Select(GetGameFromDoubleTap)
            .WhereNotNull()
            .SubscribeAwait(TryLaunchGameAsync);

        IsLaunchCooldownActive.Subscribe(_ => UpdateSelectedGameState());
    }

    private async ValueTask AddDroppedGamesAsync(DragEventArgs e, CancellationToken token)
    {
        var items = await e.DataView.GetStorageItemsAsync();
        foreach (var file in items.OfType<StorageFile>())
        {
            AddGame(file.Path);
        }
    }

    private void StartLaunchCooldown()
    {
        IsLaunchCooldownActive.Value = true;
        _ = CompleteLaunchCooldownAsync();
    }

    private async Task CompleteLaunchCooldownAsync()
    {
        await Task.Delay(LaunchCooldownMilliseconds);

        IsLaunchCooldownActive.Value = false;
    }

    private void AddGame(string path, string? name = null, long lastLaunchedTicks = 0, bool save = true)
    {
        if (!TryResolveGamePath(path, out var gamePath) ||
            Games.Any(g => string.Equals(g.Path, gamePath, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        AddGameEntry(CreateGameEntry(gamePath, name, lastLaunchedTicks));

        if (save)
            SaveGames();

        UpdateGameListState();
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

    private static GameEntry CreateGameEntry(string path, string? name = null, long lastLaunchedTicks = 0)
    {
        var game = new GameEntry
        {
            Path = path,
            LastLaunchedTicks = lastLaunchedTicks,
        };
        game.Name.Value = string.IsNullOrWhiteSpace(name) ? Path.GetFileNameWithoutExtension(path) : name;
        return game;
    }

    private void LoadGames()
    {
        if (ApplicationData.Current.LocalSettings.Values[GamesSettingKey] is not string value)
            return;

        var storedGames = ReadStoredGames(value)
            .OrderByDescending(game => game.LastLaunchedTicks);

        foreach (var game in storedGames)
        {
            AddGame(game.Path, game.Name, game.LastLaunchedTicks, save: false);
        }

        SaveGames();
        UpdateGameListState();
    }

    private static IEnumerable<StoredGameEntry> ReadStoredGames(string value)
    {
        if (value.StartsWith(GamesSettingVersion3Prefix, StringComparison.Ordinal))
        {
            return value[GamesSettingVersion3Prefix.Length..]
                .Split(GameEntrySeparator, StringSplitOptions.RemoveEmptyEntries)
                .Select(ReadStoredGame)
                .Where(game => !string.IsNullOrWhiteSpace(game.Path));
        }

        if (value.StartsWith(GamesSettingVersion2Prefix, StringComparison.Ordinal))
        {
            return value[GamesSettingVersion2Prefix.Length..]
                .Split(GameEntrySeparator, StringSplitOptions.RemoveEmptyEntries)
                .Select(ReadStoredGame)
                .Where(game => !string.IsNullOrWhiteSpace(game.Path));
        }

        return value
            .Split(GamePathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(path => new StoredGameEntry { Path = path });
    }

    private static StoredGameEntry ReadStoredGame(string value)
    {
        var fields = value.Split(GamePathSeparator);
        if (fields.Length < 2)
            return new();

        var lastLaunchedTicks = 0L;
        if (fields.Length >= 3)
        {
            _ = long.TryParse(fields[2], out lastLaunchedTicks);
        }

        return new()
        {
            Name = DecodeSettingValue(fields[0]),
            Path = DecodeSettingValue(fields[1]),
            LastLaunchedTicks = lastLaunchedTicks,
        };
    }

    private void AddGameEntry(GameEntry game)
    {
        game.LaunchCommand = LaunchGameCommand;
        game.RenameCommand = RenameGameCommand;
        game.RemoveCommand = RemoveGameCommand;

        Games.Add(game);
        GameList.Items.Add(game);
        _ = LoadGameIconAsync(game);
    }

    private void SaveGames()
    {
        ApplicationData.Current.LocalSettings.Values[GamesSettingKey] =
            GamesSettingVersion3Prefix +
            string.Join(
                GameEntrySeparator,
                Games.Select(g =>
                    $"{EncodeSettingValue(g.Name.Value)}{GamePathSeparator}{EncodeSettingValue(g.Path)}{GamePathSeparator}{g.LastLaunchedTicks}"));
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

    private void UpdateGameListState()
    {
        EmptyStateVisibility.Value = Games.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        GameListVisibility.Value = Games.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        EnsureFirstGameSelected();
        UpdateSelectedGameState();
    }

    private void EnsureFirstGameSelected()
    {
        if (Games.Count == 0)
        {
            GameList.SelectedItem = null;
            SelectedGame.Value = null;
            return;
        }

        if (GetSelectedGame() is not { } selectedGame ||
            !Games.Contains(selectedGame))
        {
            GameList.SelectedItem = Games[0];
        }
    }

    private void UpdateSelectedGameState()
    {
        var selectedGame = SelectedGame.Value = GetSelectedGame();
        foreach (var gameEntry in Games)
        {
            gameEntry.IsSelected.Value = ReferenceEquals(gameEntry, selectedGame);
        }

        if (selectedGame is not { } game)
        {
            SelectedGameActionsVisibility.Value = Visibility.Collapsed;
            SelectedGameName.Value = string.Empty;
            CanLaunchSelectedGame.Value = false;
            return;
        }

        SelectedGameActionsVisibility.Value = Visibility.Visible;
        SelectedGameName.Value = game.Name.Value;
        CanLaunchSelectedGame.Value = !IsLaunchCooldownActive.Value;
    }

    private async Task LoadGameIconAsync(GameEntry game)
    {
        game.Icon.Value = await TryLoadIconAsync(game.Path);
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

    private void RemoveGame(GameEntry game)
    {
        if (GameList.SelectedItem == game)
            GameList.SelectedItem = null;

        Games.Remove(game);
        GameList.Items.Remove(game);
        SaveGames();
        UpdateGameListState();
    }

    private void SortGamesByLastLaunch()
    {
        var orderedGames = Games
            .OrderByDescending(game => game.LastLaunchedTicks)
            .ToList();

        if (orderedGames.SequenceEqual(Games))
            return;

        Games.Clear();
        Games.AddRange(orderedGames);
        GameList.Items.Clear();

        foreach (var game in Games)
        {
            GameList.Items.Add(game);
        }
    }

    private GameEntry? GetSelectedGame() => GameList.SelectedItem as GameEntry;

    private GameEntry? GetGameFromDoubleTap(DoubleTappedRoutedEventArgs e)
    {
        var element = e.OriginalSource as FrameworkElement;
        while (element is not null)
        {
            if (element.DataContext is GameEntry game)
            {
                GameList.SelectedItem = game;
                return game;
            }

            element = element.Parent as FrameworkElement;
        }

        return GetSelectedGame();
    }
}
