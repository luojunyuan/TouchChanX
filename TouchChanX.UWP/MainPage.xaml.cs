using Windows.ApplicationModel;
using Windows.ApplicationModel.DataTransfer;
using Windows.Services.Store;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace TouchChanX.UWP
{
    /// <summary>
    /// Main preference window for adding games and launching TouchChanX.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        private const string GamesSettingKey = "Games";
        private const char GamePathSeparator = '\u001f';
        private const string OpenSourceUrl = "https://github.com/luojunyuan/TouchChanX";

        private List<GameEntry> Games { get; } = [];

        public MainPage()
        {
            InitializeComponent();
            LoadGames();
            UpdateGameListState();
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

        private async void GameList_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is not GameEntry game)
                return;

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

        private void AddGame(string path)
        {
            if (string.IsNullOrWhiteSpace(path) ||
                !File.Exists(path) ||
                !IsSupportedGamePath(path) ||
                Games.Any(g => string.Equals(g.Path, path, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            AddGameEntry(CreateGameEntry(path));

            SaveGames();
            UpdateGameListState();
        }

        private static bool IsSupportedGamePath(string path)
        {
            var extension = Path.GetExtension(path);
            return extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase);
        }

        private static GameEntry CreateGameEntry(string path) => new()
        {
            Name = Path.GetFileNameWithoutExtension(path),
            Path = path,
        };

        private void LoadGames()
        {
            if (ApplicationData.Current.LocalSettings.Values[GamesSettingKey] is not string value)
                return;

            var paths = value.Split(GamePathSeparator, StringSplitOptions.RemoveEmptyEntries);
            foreach (var path in paths.Where(File.Exists))
            {
                AddGameEntry(CreateGameEntry(path));
            }
        }

        private void AddGameEntry(GameEntry game)
        {
            Games.Add(game);
            GameList.Items.Add(game);
        }

        private void SaveGames()
        {
            ApplicationData.Current.LocalSettings.Values[GamesSettingKey] =
                string.Join(GamePathSeparator, Games.Select(g => g.Path));
        }

        private void UpdateGameListState()
        {
            EmptyState.Visibility = Games.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            GameList.Visibility = Games.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    public sealed class GameEntry
    {
        public string Name { get; set; } = string.Empty;

        public string Path { get; set; } = string.Empty;
    }
}
