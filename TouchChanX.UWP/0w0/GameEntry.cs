using R3;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media;

namespace TouchChanX.UWP;

public sealed partial class GameEntry(Observable<GameEntry?> selectedGame) : IDisposable
{
    public BindableReactiveProperty<string> Name { get; } = new(string.Empty);

    public string Path { get; set; } = string.Empty;

    public long LastLaunchedTicks { get; set; }

    public BindableReactiveProperty<ImageSource?> Icon { get; } = new(null);

    public BindableReactiveProperty<Visibility> IconVisibility => field ??= 
        Icon
        .Select(icon => icon is null ? Visibility.Collapsed : Visibility.Visible)
        .ToBindableReactiveProperty(Visibility.Collapsed);

    public BindableReactiveProperty<Visibility> FallbackIconVisibility => field ??=
        Icon
        .Select(icon => icon is null ? Visibility.Visible : Visibility.Collapsed)
        .ToBindableReactiveProperty(Visibility.Visible);

    public BindableReactiveProperty<Visibility> SelectedVisualVisibility => field ??=
        selectedGame
        .Select(game => ReferenceEquals(game, this) ? Visibility.Visible : Visibility.Collapsed)
        .ToBindableReactiveProperty(Visibility.Collapsed);

    public void Dispose() => SelectedVisualVisibility.Dispose();
}

public sealed class StoredGameEntry
{
    public string Name { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;

    public long LastLaunchedTicks { get; set; }
}
