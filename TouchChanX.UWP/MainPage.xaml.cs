using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using Windows.ApplicationModel.Core;
using Windows.ApplicationModel.DataTransfer;
using Windows.Services.Store;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.System;
using Windows.UI;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using WinRT;
using WinRT.Interop;

namespace TouchChanX.UWP;

/// <summary>
/// Main preference window for adding games and launching TouchChanX.
/// </summary>
public sealed partial class MainPage : Page
{
    private const string GamesSettingKey = "Games";
    private const string GamesSettingVersion3Prefix = "v3\n";
    private const string GamesSettingVersion2Prefix = "v2\n";
    private const char GameEntrySeparator = '\u001e';
    private const char GamePathSeparator = '\u001f';
    private const string OpenSourceUrl = "https://github.com/luojunyuan/TachiChanX";

    private List<GameEntry> Games { get; } = [];

    public MainPage()
    {
        InitializeComponent();
        SetupTitlebar();
        LoadGames();
        UpdateGameListState();
    }

    private static void SetupTitlebar()
    {
        var coreTitleBar = CoreApplication.GetCurrentView().TitleBar;
        coreTitleBar.ExtendViewIntoTitleBar = true;

        var titleBar = ApplicationView.GetForCurrentView().TitleBar;
        titleBar.ButtonBackgroundColor = Colors.Transparent;
        titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
    }

    private void AppNav_SelectionChanged(
        Microsoft.UI.Xaml.Controls.NavigationView sender,
        Microsoft.UI.Xaml.Controls.NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer?.Tag is not string tag)
            return;

        HomePage.Visibility = tag == "home" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = tag == "settings" ? Visibility.Visible : Visibility.Collapsed;
        AboutPage.Visibility = tag == "about" ? Visibility.Visible : Visibility.Collapsed;

        sender.Header = tag switch
        {
            "settings" => "设置",
            "about" => "关于",
            _ => "主页",
        };
    }

    private async void AddGame_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".exe");

        var file = await picker.PickSingleFileAsync();
        if (file is not null)
        {
            AddGame(file.Path);
        }
    }

    private void DropArea_DragOver(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
            return;

        e.AcceptedOperation = DataPackageOperation.Copy;
        e.DragUIOverride.Caption = "添加到 TouchChanX";
        e.DragUIOverride.IsCaptionVisible = true;
    }

    private async void DropArea_Drop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
            return;

        var items = await e.DataView.GetStorageItemsAsync();
        foreach (var file in items.OfType<StorageFile>())
        {
            AddGame(file.Path);
        }
    }

    private void GameList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateSelectedGameState();
    }

    private async void GameItem_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not GameEntry game)
            return;

        GameList.SelectedItem = game;
        await LaunchGameAsync(game);
    }

    private async void LaunchGame_Click(object sender, RoutedEventArgs e)
    {
        if (GetGameFromSender(sender) is { } game)
            await LaunchGameAsync(game);
    }

    private async void LaunchSelectedGame_Click(object sender, RoutedEventArgs e)
    {
        if (GetSelectedGame() is { } game)
            await LaunchGameAsync(game);
    }

    private async void RenameGame_Click(object sender, RoutedEventArgs e)
    {
        if (GetGameFromSender(sender) is { } game)
            await RenameGameAsync(game);
    }

    private async void RenameSelectedGame_Click(object sender, RoutedEventArgs e)
    {
        if (GetSelectedGame() is { } game)
            await RenameGameAsync(game);
    }

    private void RemoveGame_Click(object sender, RoutedEventArgs e)
    {
        if (GetGameFromSender(sender) is { } game)
            RemoveGame(game);
    }

    private void RemoveSelectedGame_Click(object sender, RoutedEventArgs e)
    {
        if (GetSelectedGame() is { } game)
            RemoveGame(game);
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

    private StoreContext StoreContext => field ??= new Func<StoreContext>(() =>
    {
        var coreWindow = CoreApplication.GetCurrentView().CoreWindow;
        coreWindow.As<ICoreWindowInterop>().GetWindowHandle(out nint hwnd);

        var storeContext = StoreContext.GetDefault();
        InitializeWithWindow.Initialize(storeContext, hwnd);

        return storeContext;
    })();

    private async void RateInStore_Click(object sender, RoutedEventArgs e)
    {
        var result = await StoreContext.RequestRateAndReviewAppAsync();

        if (result.ExtendedError is not null)
        {
            var pfn = Windows.ApplicationModel.Package.Current.Id.FamilyName;
            var storeUri = new Uri($"ms-windows-store://review/?PFN={pfn}");
            await Launcher.LaunchUriAsync(storeUri);
        }
    }

    private async void OpenSource_Click(object sender, RoutedEventArgs e)
    {
        await Launcher.LaunchUriAsync(new Uri(OpenSourceUrl));
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

    private static GameEntry? GetGameFromSender(object sender) =>
        (sender as FrameworkElement)?.Tag as GameEntry;
}

public sealed partial class GameEntry : INotifyPropertyChanged
{
    private ImageSource? icon;
    private bool isSelected;
    private string name = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

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
