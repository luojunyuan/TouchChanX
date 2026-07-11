using System.Collections.ObjectModel;
using R3;
using TouchChanX.Persistence;
using Windows.System;
using Windows.UI.Xaml;

namespace TouchChanX.UWP;

public sealed class HomePageViewModel(AppSettings settings)
{
    private readonly HomePageGameStore _store = new(settings);

    public Interaction<Unit, string?> PickGamePathInteraction { get; } = new();

    public Interaction<Unit, Unit> ShowLaunchFailedInteraction { get; } = new();

    public Interaction<string, string?> RenameGameInteraction { get; } = new();

    public ObservableCollection<GameEntryViewModel> GameItems => _store.Games;

    public BindableReactiveProperty<GameEntryViewModel?> SelectedGame { get; } = new(null);

    public BindableReactiveProperty<Visibility> EmptyStateVisibility => field ??=
        _store.HasGames
            .Select(hasGames => hasGames ? Visibility.Collapsed : Visibility.Visible)
            .ToBindableReactiveProperty(Visibility.Visible);

    public BindableReactiveProperty<Visibility> GamesVisibility => field ??=
        _store.HasGames
            .Select(hasGames => hasGames ? Visibility.Visible : Visibility.Collapsed)
            .ToBindableReactiveProperty(Visibility.Collapsed);

    public BindableReactiveProperty<string> SelectedGameName => field ??=
        SelectedGame
            .Select(game => game is null ? Observable.Return(string.Empty) : game.Name)
            .Switch()
            .ToBindableReactiveProperty(string.Empty);

    public ReactiveCommand<string[]> AddBunchGamesCommand => field ??= 
        new(paths => _store.Dispatch(new GameCommand.AddRange(paths)));

    public ReactiveCommand AddGameCommand => field ??= new(
        async (_, _) =>
        {
            var path = await PickGamePathInteraction
                .Handle(Unit.Default)
                .FirstAsync(CancellationToken.None);

            if (path is not null)
                _store.Dispatch(new GameCommand.Add(path));
        },
        awaitOperation: AwaitOperation.Drop);

    public ReactiveCommand<GameEntryViewModel> LaunchSelectedGameCommand => field ??=
        ReactiveCommandFactory.CreateStatusReactiveCommand<GameEntryViewModel>(
            async (game, _) =>
            {
                await LaunchSelectedGameAsync(game);
                await Task.Delay(TimeSpan.FromSeconds(3), CancellationToken.None);
            });

    public ReactiveCommand<GameEntryViewModel> RenameSelectedGameCommand => field ??=
        new(async (game, _) => 
        {
            var newName = await RenameGameInteraction
                .Handle(game.Name.Value)
                .FirstAsync(CancellationToken.None);

            if (string.IsNullOrWhiteSpace(newName))
                return;

            _store.Dispatch(new GameCommand.Rename(game.Path, newName.Trim()));
        }, awaitOperation: AwaitOperation.Drop);

    public ReactiveCommand<GameEntryViewModel> RemoveSelectedGameCommand => field ??= new(game =>
        _store.Dispatch(new GameCommand.Remove(game.Path)));

    public ReactiveCommand<(GameEntryViewModel? OldGame, GameEntryViewModel? NewGame)> 
        SelectionChangedCommand => field ??=
        new(pair =>
        {
            pair.OldGame?.SetSelected(false);
            pair.NewGame?.SetSelected(true);
        });

    private async Task LaunchSelectedGameAsync(GameEntryViewModel game)
    {
        var uri = new Uri($"touchchanx://launch/?path={Uri.EscapeDataString(game.Path)}");

        if (!await Launcher.LaunchUriAsync(uri))
        {
            await ShowLaunchFailedInteraction.Handle(Unit.Default).FirstAsync();
            return;
        }

        _store.Dispatch(new GameCommand.MarkLaunched(game.Path, DateTimeOffset.UtcNow.UtcTicks));
    }
}
