using R3;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media;

namespace TouchChanX.UWP;

public sealed partial class GameEntry
{
    public ReactiveCommand<GameEntry> LaunchCommand { get; set; } = null!;

    public ReactiveCommand<GameEntry> RenameCommand { get; set; } = null!;

    public ReactiveCommand<GameEntry> RemoveCommand { get; set; } = null!;

    public BindableReactiveProperty<string> Name { get; } = new(string.Empty);

    public string Path { get; set; } = string.Empty;

    public long LastLaunchedTicks { get; set; }

    public BindableReactiveProperty<ImageSource?> Icon { get; } = new(null);

    public BindableReactiveProperty<Visibility> IconVisibility
    {
        get
        {
            field ??= Icon
                .Select(icon => icon is null ? Visibility.Collapsed : Visibility.Visible)
                .ToBindableReactiveProperty(Visibility.Collapsed);
            return field;
        }
    }

    public BindableReactiveProperty<Visibility> FallbackIconVisibility
    {
        get
        {
            field ??= Icon
                .Select(icon => icon is null ? Visibility.Visible : Visibility.Collapsed)
                .ToBindableReactiveProperty(Visibility.Visible);
            return field;
        }
    }

    public BindableReactiveProperty<bool> IsSelected { get; } = new(false);

    public BindableReactiveProperty<Visibility> SelectedVisualVisibility
    {
        get
        {
            field ??= IsSelected
                .Select(isSelected => isSelected ? Visibility.Visible : Visibility.Collapsed)
                .ToBindableReactiveProperty(Visibility.Collapsed);
            return field;
        }
    }
}

public sealed class StoredGameEntry
{
    public string Name { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;

    public long LastLaunchedTicks { get; set; }
}
