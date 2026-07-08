using R3;
using System.Windows.Input;
using Windows.Storage.Pickers;
using Windows.System;
using Windows.UI.Xaml.Controls;

namespace TouchChanX.UWP;

public sealed partial class HomePage
{
    private static readonly TimeSpan LaunchCooldownMilliseconds = TimeSpan.FromMilliseconds(3000);
    private readonly Subject<Unit> _launchRequested = new();

    public ReactiveCommand AddGameCommand => field ??= new(AddGameFromPickerAsync);

    private Observable<bool> CanLaunchSelectedGame => Observable.CombineLatest(
        SelectedGame.Select(game => game is not null),
        _launchRequested
            .SelectMany(_ => Observable.Concat(
                Observable.Return(false),
                Observable.Timer(LaunchCooldownMilliseconds).Select(_ => true)))
            .Prepend(true),
        (hasGame, isReady) => hasGame && isReady);

    public ReactiveCommand<Unit> LaunchSelectedGameCommand => field ??=
        CanLaunchSelectedGame
            .ToReactiveCommand<Unit>(LaunchSelectedGameAsync, awaitOperation: AwaitOperation.Drop);

    public ReactiveCommand RenameSelectedGameCommand => field ??= new(async (_, token) =>
    {
        if (GetSelectedGame() is { } game)
            await RenameGameAsync(game, token);
    });

    public ReactiveCommand RemoveSelectedGameCommand => field ??= new(_ =>
    {
        if (GetSelectedGame() is { } game)
            RemoveGame(game);
    });

    private async ValueTask AddGameFromPickerAsync(Unit unit, CancellationToken token)
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".exe");

        var file = await picker.PickSingleFileAsync();
        if (file is not null)
            AddGame(file.Path);
    }

    private async ValueTask LaunchSelectedGameAsync(Unit unit, CancellationToken token)
    {
        if (GetSelectedGame() is not { } game)
            return;

        _launchRequested.OnNext(Unit.Default);
        await LaunchGameAsync(game, token);
    }

    private async ValueTask LaunchGameAsync(GameEntry game, CancellationToken token)
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
        MoveGameToFront(game);
        SaveGames();
        UpdateGameListState();
    }

    private async ValueTask RenameGameAsync(GameEntry game, CancellationToken token)
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

        if (result != ContentDialogResult.Primary)
            return;

        var newName = nameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(newName))
            return;

        game.Name.Value = newName;
        SaveGames();
        UpdateSelectedGameState();
    }
}

public static class R3CommandExtensions
{
    public static IDisposable InvokeCommand<T>(this Observable<T> source, ICommand command)
    {
        return source
            .Where(command, static (v, cmd) => cmd.CanExecute(v))
            .Subscribe(command, static (v, cmd) => cmd.Execute(v));
    }
}