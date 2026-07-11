using R3;
using R3.ObservableEvents;
using System.Windows.Input;
using TouchChanX.Persistence;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI.Xaml.Controls;

namespace TouchChanX.UWP;

public sealed partial class HomePage : Page
{
    public HomePageViewModel ViewModel { get; }

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
            var gameName = context.Input;

            var nameBox = new TextBox
            {
                Header = "显示名称",
                MaxLength = 80,
                Text = gameName,
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
            .Select(items => items.OfType<StorageFile>().Select(file => file.Path).ToArray())
            .InvokeCommand(ViewModel.AddBunchGamesCommand);

        // 首次 SelectedIndex = 0 必须在 Loaded 之后才生效
        this.Events().Loaded
            .AsUnitObservable()
            .Merge(GameList.Items.Events().VectorChanged.AsUnitObservable())
            .Where(_ => GameList.Items.Count > 0 && GameList.SelectedIndex == -1)
            .Subscribe(_ => GameList.SelectedIndex = 0);

        GameList.Events().SelectionChanged
            .Select(e => (
                OldGame: e.RemovedItems.OfType<GameEntryViewModel>().FirstOrDefault(),
                NewGame: e.AddedItems.OfType<GameEntryViewModel>().FirstOrDefault()))
            .InvokeCommand(ViewModel.SelectionChangedCommand);
    }
}

public static class R3CommandExtensions
{
    public static IDisposable InvokeCommand<T>(this Observable<T> source, ICommand command) => 
        source
            .Where(command, static (v, cmd) => cmd.CanExecute(v))
            .Subscribe(command, static (v, cmd) => cmd.Execute(v));
}
