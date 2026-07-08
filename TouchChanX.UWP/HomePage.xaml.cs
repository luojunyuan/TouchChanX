using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using R3;
using R3.ObservableEvents;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.System;
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

    private bool _isLaunchCooldownActive;

    private List<GameEntry> Games { get; } = [];

    public ReactiveCommand AddGameCommand => field ??= new ReactiveCommand(
        async (_, _) => await AddGameFromPickerAsync());

    public ReactiveCommand LaunchSelectedGameCommand => field ??= new ReactiveCommand(async (_, _) =>
    {
        if (GetSelectedGame() is { } game)
            await TryLaunchGameAsync(game);
    });

    public ReactiveCommand RenameSelectedGameCommand => field ??= new ReactiveCommand(async (_, _) =>
    {
        if (GetSelectedGame() is { } game)
            await RenameGameAsync(game);
    });

    public ReactiveCommand RemoveSelectedGameCommand => field ??= new ReactiveCommand(_ =>
    {
        if (GetSelectedGame() is { } game)
            RemoveGame(game);
    });

    public ReactiveCommand<GameEntry> LaunchGameCommand => field ??= new ReactiveCommand<GameEntry>(
        async (game, _) => await TryLaunchGameAsync(game));

    public ReactiveCommand<GameEntry> RenameGameCommand => field ??= new ReactiveCommand<GameEntry>(
        async (game, _) => await RenameGameAsync(game));

    public ReactiveCommand<GameEntry> RemoveGameCommand => field ??= new ReactiveCommand<GameEntry>(RemoveGame);

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
            .SubscribeAwait(async (e, _) => await AddDroppedGamesAsync(e));

        GameList.Events().SelectionChanged
            .Subscribe(_ => UpdateSelectedGameState());

        GameList.Events().DoubleTapped
            .Select(GetGameFromDoubleTap)
            .WhereNotNull()
            .SubscribeAwait(async (game, _) => await TryLaunchGameAsync(game));
    }

    private async Task AddGameFromPickerAsync()
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".exe");

        var file = await picker.PickSingleFileAsync();
        if (file is not null)
            AddGame(file.Path);
    }

    private async Task AddDroppedGamesAsync(DragEventArgs e)
    {
        var items = await e.DataView.GetStorageItemsAsync();
        foreach (var file in items.OfType<StorageFile>())
        {
            AddGame(file.Path);
        }
    }

    private async Task TryLaunchGameAsync(GameEntry game)
    {
        if (_isLaunchCooldownActive)
            return;

        StartLaunchCooldown();
        await LaunchGameAsync(game);
    }

    private void StartLaunchCooldown()
    {
        _isLaunchCooldownActive = true;
        UpdateSelectedGameState();
        _ = CompleteLaunchCooldownAsync();
    }

    private async Task CompleteLaunchCooldownAsync()
    {
        await Task.Delay(LaunchCooldownMilliseconds);

        _isLaunchCooldownActive = false;
        UpdateSelectedGameState();
    }

    private async Task LaunchGameAsync(GameEntry game)
    {
        var uri = new Uri($"touchchanx://launch/?path={Uri.EscapeDataString(game.Path)}");
        var launched = await Launcher.LaunchUriAsync(uri);
        if (!launched)
        {
            await new ContentDialog
            {
                Title = "启动失败",
                Content = "无法调用 touchchanx 协议。",
                CloseButtonText = "确定",
            }.ShowAsync();
            return;
        }

        game.LastLaunchedTicks = DateTimeOffset.UtcNow.UtcTicks;
        SortGamesByLastLaunch();
        SaveGames();
        UpdateGameListState();
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

    private static GameEntry CreateGameEntry(string path, string? name = null, long lastLaunchedTicks = 0) => new()
    {
        Name = string.IsNullOrWhiteSpace(name) ? Path.GetFileNameWithoutExtension(path) : name,
        Path = path,
        LastLaunchedTicks = lastLaunchedTicks,
    };

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
                    $"{EncodeSettingValue(g.Name)}{GamePathSeparator}{EncodeSettingValue(g.Path)}{GamePathSeparator}{g.LastLaunchedTicks}"));
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
        EmptyState.Visibility = Games.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        GameList.Visibility = Games.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        EnsureFirstGameSelected();
        UpdateSelectedGameState();
    }

    private void EnsureFirstGameSelected()
    {
        if (Games.Count == 0)
        {
            GameList.SelectedItem = null;
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
        var selectedGame = GetSelectedGame();
        foreach (var gameEntry in Games)
        {
            gameEntry.IsSelected = ReferenceEquals(gameEntry, selectedGame);
        }

        if (selectedGame is not { } game)
        {
            SelectedGameActions.Visibility = Visibility.Collapsed;
            SelectedGameName.Text = string.Empty;
            return;
        }

        SelectedGameActions.Visibility = Visibility.Visible;
        SelectedGameName.Text = game.Name;
        LaunchSelectedGameButton.IsEnabled = !_isLaunchCooldownActive;
    }

    private async Task LoadGameIconAsync(GameEntry game)
    {
        game.Icon = await TryLoadIconAsync(game.Path);
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

    private async Task RenameGameAsync(GameEntry game)
    {
        var nameBox = new TextBox
        {
            Header = "显示名称",
            MaxLength = 80,
            Text = game.Name,
        };

        var result = await new ContentDialog
        {
            Title = "重命名游戏",
            Content = nameBox,
            PrimaryButtonText = "保存",
            SecondaryButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
        }.ShowAsync();

        if (result != ContentDialogResult.Primary)
            return;

        var newName = nameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(newName))
            return;

        game.Name = newName;
        SaveGames();
        UpdateSelectedGameState();
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

public sealed partial class GameEntry : INotifyPropertyChanged
{
    private ImageSource? icon;
    private bool isSelected;
    private string name = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ReactiveCommand<GameEntry> LaunchCommand { get; set; } = null!;

    public ReactiveCommand<GameEntry> RenameCommand { get; set; } = null!;

    public ReactiveCommand<GameEntry> RemoveCommand { get; set; } = null!;

    public string Name
    {
        get => name;
        set
        {
            if (name == value)
                return;

            name = value;
            OnPropertyChanged();
        }
    }

    public string Path { get; set; } = string.Empty;

    public long LastLaunchedTicks { get; set; }

    public ImageSource? Icon
    {
        get => icon;
        set
        {
            if (icon == value)
                return;

            icon = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IconVisibility));
            OnPropertyChanged(nameof(FallbackIconVisibility));
        }
    }

    public Visibility IconVisibility => Icon is null ? Visibility.Collapsed : Visibility.Visible;

    public bool IsSelected
    {
        get => isSelected;
        set
        {
            if (isSelected == value)
                return;

            isSelected = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedVisualVisibility));
        }
    }

    public Visibility SelectedVisualVisibility => IsSelected ? Visibility.Visible : Visibility.Collapsed;

    public Visibility FallbackIconVisibility => Icon is null ? Visibility.Visible : Visibility.Collapsed;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class StoredGameEntry
{
    public string Name { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;

    public long LastLaunchedTicks { get; set; }
}
