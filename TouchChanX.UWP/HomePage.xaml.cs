using R3;
using R3.ObservableEvents;
using TouchChanX.Persistence;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace TouchChanX.UWP;

public sealed partial class HomePage : Page
{
    public HomePageViewModel ViewModel { get; }

    public BindableReactiveProperty<Visibility> EmptyStateVisibility => field ??=
        ViewModel.HasGames
            .Select(hasGames => hasGames ? Visibility.Collapsed : Visibility.Visible)
            .ToBindableReactiveProperty(Visibility.Visible);

    public BindableReactiveProperty<Visibility> GamesVisibility => field ??=
        ViewModel.HasGames
            .Select(hasGames => hasGames ? Visibility.Visible : Visibility.Collapsed)
            .ToBindableReactiveProperty(Visibility.Collapsed);

    public HomePage()
    {
        ViewModel = new HomePageViewModel(new AppSettings());

        InitializeComponent();
        RegisterInteractions();
        BindReactiveEvents();
    }

    private void RegisterInteractions()
    {
        ViewModel.PickGamePathInteraction.RegisterHandler(async context =>
        {
            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add(".exe");

            var file = await picker.PickSingleFileAsync();

            context.SetOutput(file?.Path);
        });

        ViewModel.ShowLaunchFailedInteraction.RegisterHandler(async context =>
        {
            await new ContentDialog
            {
                Title = "启动失败",
                Content = "无法调用 touchchanx 协议。",
                CloseButtonText = "确定",
            }.ShowAsync();

            context.SetOutput(Unit.Default);
        });

        ViewModel.RenameGameInteraction.RegisterHandler(async context =>
        {
            var game = context.Input;

            var nameBox = new TextBox
            {
                Header = "显示名称",
                MaxLength = 80,
                Text = game.Name.Value,
            };

            var result = await new ContentDialog
            {
                Title = "重命名游戏",
                Content = nameBox,
                PrimaryButtonText = "保存",
                SecondaryButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
            }.ShowAsync();

            var output = result == ContentDialogResult.Primary
                ? nameBox.Text
                : null;

            context.SetOutput(output);
        });
    }

    private void BindReactiveEvents()
    {
        DropArea.Events().DragOver
            .Where(e => e.DataView.Contains(StandardDataFormats.StorageItems))
            .Do(e =>
            {
                e.AcceptedOperation = DataPackageOperation.Copy;
                e.DragUIOverride.Caption = "添加到 TouchChanX";
                e.DragUIOverride.IsCaptionVisible = true;
            })
            .Subscribe();

        DropArea.Events().Drop
            .Where(e => e.DataView.Contains(StandardDataFormats.StorageItems))
            .SelectAwait(async (e, _) => await e.DataView.GetStorageItemsAsync())
            .Subscribe(items =>
            {
                foreach (var file in items.OfType<StorageFile>())
                {
                    ViewModel.AddGame(file.Path);
                }
            });
    }
}
