using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using TouchChanX.Win32;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Core;
using Windows.ApplicationModel.DataTransfer;
using Windows.Services.Store;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Pickers;
using Windows.System;
using Windows.UI;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;

namespace TouchChanX.UWP
{
    /// <summary>
    /// Main preference window for adding games and launching TouchChanX.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        private const string GamesSettingKey = "Games";
        private const string GamesSettingVersion2Prefix = "v2\n";
        private const char GameEntrySeparator = '\u001e';
        private const char GamePathSeparator = '\u001f';
        private const string OpenSourceUrl = "https://github.com/luojunyuan/TouchChanX";

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
            picker.FileTypeFilter.Add(".lnk");

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
            }
        }

        private async void RateInStore_Click(object sender, RoutedEventArgs e)
        {
            var result = await StoreContext.GetDefault().RequestRateAndReviewAppAsync();
            if (result.ExtendedError is not null)
            {
                var pfn = Package.Current.Id.FamilyName;
                await Launcher.LaunchUriAsync(new Uri($"ms-windows-store://review/?PFN={pfn}"));
            }
        }

        private async void OpenSource_Click(object sender, RoutedEventArgs e)
        {
            await Launcher.LaunchUriAsync(new Uri(OpenSourceUrl));
        }

        private void AddGame(string path, string? name = null, bool save = true)
        {
            if (!TryResolveGamePath(path, out var gamePath) ||
                Games.Any(g => string.Equals(g.Path, gamePath, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            AddGameEntry(CreateGameEntry(gamePath, name));

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

            var result = GameStartup.PrepareValidGamePath(path);
            if (result.IsFailure(out _, out var resolvedPath))
                return false;

            gamePath = resolvedPath;
            return true;
        }

        private static bool IsSupportedGamePath(string path)
        {
            var extension = Path.GetExtension(path);
            return extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase);
        }

        private static GameEntry CreateGameEntry(string path, string? name = null) => new()
        {
            Name = string.IsNullOrWhiteSpace(name) ? Path.GetFileNameWithoutExtension(path) : name,
            Path = path,
        };

        private void LoadGames()
        {
            if (ApplicationData.Current.LocalSettings.Values[GamesSettingKey] is not string value)
                return;

            var storedGames = ReadStoredGames(value);
            foreach (var game in storedGames)
            {
                AddGame(game.Path, game.Name, save: false);
            }

            SaveGames();
            UpdateGameListState();
        }

        private static IEnumerable<StoredGameEntry> ReadStoredGames(string value)
        {
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
            var fields = value.Split(GamePathSeparator, 2);
            if (fields.Length != 2)
                return new();

            return new()
            {
                Name = DecodeSettingValue(fields[0]),
                Path = DecodeSettingValue(fields[1]),
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
                GamesSettingVersion2Prefix +
                string.Join(
                    GameEntrySeparator,
                    Games.Select(g => $"{EncodeSettingValue(g.Name)}{GamePathSeparator}{EncodeSettingValue(g.Path)}"));
        }

        private static string EncodeSettingValue(string value) =>
            Convert.ToBase64String(Encoding.UTF8.GetBytes(value));

        private static string DecodeSettingValue(string value)
        {
            try
            {
                return Encoding.UTF8.GetString(Convert.FromBase64String(value));
            }
            catch (FormatException)
            {
                return string.Empty;
            }
        }

        private void UpdateGameListState()
        {
            EmptyState.Visibility = Games.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            GameList.Visibility = Games.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            UpdateSelectedGameState();
        }

        private void UpdateSelectedGameState()
        {
            if (GetSelectedGame() is not { } game)
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
                var file = await StorageFile.GetFileFromPathAsync(path);
                using var thumbnail = await file.GetThumbnailAsync(
                    ThumbnailMode.SingleItem,
                    96,
                    ThumbnailOptions.UseCurrentScale);

                if (thumbnail.Size == 0)
                    return null;

                var image = new BitmapImage();
                await image.SetSourceAsync(thumbnail);
                return image;
            }
            catch (Exception)
            {
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

        private GameEntry? GetSelectedGame() => GameList.SelectedItem as GameEntry;

        private static GameEntry? GetGameFromSender(object sender) =>
            (sender as FrameworkElement)?.Tag as GameEntry;
    }

    public sealed class GameEntry : INotifyPropertyChanged
    {
        private ImageSource? icon;
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

        public Visibility FallbackIconVisibility => Icon is null ? Visibility.Visible : Visibility.Collapsed;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public sealed class StoredGameEntry
    {
        public string Name { get; set; } = string.Empty;

        public string Path { get; set; } = string.Empty;
    }
}
