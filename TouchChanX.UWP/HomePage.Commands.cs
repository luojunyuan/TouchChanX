using R3;
using Windows.Storage.Pickers;
using Windows.System;
using Windows.UI.Xaml.Controls;

namespace TouchChanX.UWP;

public sealed partial class HomePage
{
    public ReactiveCommand AddGameCommand => field ??= new ReactiveCommand(AddGameFromPickerAsync);

    public ReactiveCommand LaunchSelectedGameCommand => field ??= new ReactiveCommand(async (_, token) =>
    {
        if (GetSelectedGame() is { } game)
            await TryLaunchGameAsync(game, token);
    });

    public ReactiveCommand RenameSelectedGameCommand => field ??= new ReactiveCommand(async (_, token) =>
    {
        if (GetSelectedGame() is { } game)
            await RenameGameAsync(game, token);
    });

    public ReactiveCommand RemoveSelectedGameCommand => field ??= new ReactiveCommand(_ =>
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

    private async ValueTask TryLaunchGameAsync(GameEntry game, CancellationToken token)
    {
        if (IsLaunchCooldownActive.Value)
            return;

        StartLaunchCooldown();
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
