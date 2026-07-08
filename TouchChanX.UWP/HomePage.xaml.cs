using R3;
using R3.ObservableEvents;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace TouchChanX.UWP;

public sealed partial class HomePage : Page
{
    public HomePageViewModel ViewModel { get; }

    public HomePage()
    {
        ViewModel = new(new GameStorageService());

        InitializeComponent();
        RegisterInteractions();
        BindReactiveInteractions();
    }

    private void RegisterInteractions()
    {
        ViewModel.PickGamePathInteraction.RegisterHandler(async context =>
        {
            context.SetOutput(await PickGamePathAsync());
        });

        ViewModel.ShowLaunchFailedInteraction.RegisterHandler(async context =>
        {
            await ShowLaunchFailedAsync();
            context.SetOutput(Unit.Default);
        });

        ViewModel.RenameGameInteraction.RegisterHandler(async context =>
        {
            context.SetOutput(await RenameGameAsync(context.Input));
        });
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
            .SubscribeAwait((e, _) => new ValueTask(AddDroppedGamesAsync(e)));
    }

    private async Task AddDroppedGamesAsync(DragEventArgs e)
    {
        var items = await e.DataView.GetStorageItemsAsync();
        foreach (var file in items.OfType<StorageFile>())
        {
            ViewModel.AddGame(file.Path);
        }
    }

    private static async Task<string?> PickGamePathAsync()
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".exe");

        var file = await picker.PickSingleFileAsync();
        return file?.Path;
    }

    private static async Task ShowLaunchFailedAsync()
    {
        await new ContentDialog
        {
            Title = "启动失败",
            Content = "无法调用 touchchanx 协议。",
            CloseButtonText = "确定",
        }.ShowAsync();
    }

    private static async Task<string?> RenameGameAsync(GameEntry game)
    {
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

        return result == ContentDialogResult.Primary
            ? nameBox.Text
            : null;
    }
}
