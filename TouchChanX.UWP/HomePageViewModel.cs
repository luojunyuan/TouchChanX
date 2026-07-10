using R3;
using R3.ObservableEvents;
using TouchChanX.Persistence;
using Windows.System;
using Windows.UI.Xaml;

namespace TouchChanX.UWP;

public sealed class HomePageViewModel
{
    private static readonly TimeSpan LaunchCooldown = TimeSpan.FromSeconds(3);

    private readonly HomePageGameStore _store;
    private readonly Subject<Unit> _launchRequested = new();

    public HomePageViewModel(AppSettings settings)
    {
        _store = new HomePageGameStore(settings);
    }

    public Interaction<Unit, string?> PickGamePathInteraction { get; } = new();

    public Interaction<Unit, Unit> ShowLaunchFailedInteraction { get; } = new();

    public Interaction<string, string?> RenameGameInteraction { get; } = new();

    public ObservableListBindableView<GameEntryViewModel> GameItems => field ??=
        _store.Games.ToBindableView(game => new GameEntryViewModel(game));

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

    public ReactiveCommand AddGameCommand => field ??= new(async (_, _) => await AddGameFromPickerAsync());

    private Observable<bool> CanLaunchSelectedGame => Observable.CombineLatest(
        _store.HasGames,
        _launchRequested
            .SelectMany(_ => Observable.Concat(
                Observable.Return(false),
                Observable.Timer(LaunchCooldown).Select(_ => true)))
            .Prepend(true),
        (hasGame, isReady) => hasGame && isReady);

    public ReactiveCommand<Unit> LaunchSelectedGameCommand => field ??=
        CanLaunchSelectedGame.ToReactiveCommand<Unit>(
            async (_, _) => await LaunchSelectedGameAsync(),
            initialCanExecute: false,
            awaitOperation: AwaitOperation.Drop);

    public ReactiveCommand RenameSelectedGameCommand => field ??=
        new(async (_, _) => await RenameSelectedGameAsync(), awaitOperation: AwaitOperation.Drop);

    public ReactiveCommand RemoveSelectedGameCommand => field ??= new(_ =>
        _store.Dispatch(new GameCommand.Remove(SelectedGame.Value!.Path)));

    public void Dispatch(GameCommand command) => _store.Dispatch(command);

    private async Task AddGameFromPickerAsync()
    {
        var path = await PickGamePathInteraction.Handle(Unit.Default).FirstAsync();
        if (path is not null)
            _store.Dispatch(new GameCommand.Add(path));
    }

    private async Task LaunchSelectedGameAsync()
    {
        var game = SelectedGame.Value!;

        _launchRequested.OnNext(Unit.Default);
        if (!await LaunchGameAsync(game))
        {
            await ShowLaunchFailedInteraction.Handle(Unit.Default).FirstAsync();
            return;
        }

        _store.Dispatch(new GameCommand.MarkLaunched(game.Path, DateTimeOffset.UtcNow.UtcTicks));
    }

    private async Task RenameSelectedGameAsync()
    {
        var game = SelectedGame.Value!;

        var newName = (await RenameGameInteraction.Handle(game.Name.Value).FirstAsync())?.Trim();
        if (string.IsNullOrWhiteSpace(newName))
            return;

        _store.Dispatch(new GameCommand.Rename(game.Path, newName));
    }

    private static async Task<bool> LaunchGameAsync(GameEntryViewModel game)
    {
        var uri = new Uri($"touchchanx://launch/?path={Uri.EscapeDataString(game.Path)}");
        return await Launcher.LaunchUriAsync(uri);
    }
}
