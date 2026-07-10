using R3;
using System.Diagnostics;
using Windows.Storage.Streams;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;

namespace TouchChanX.UWP;

public sealed partial class GameEntryViewModel
{
    public GameEntryViewModel(GameEntry game)
    {
        Path = game.Path;
        LastLaunchedTicks = game.LastLaunchedTicks;
        Name.Value = game.Name;
        _ = LoadIconAsync();
    }

    public BindableReactiveProperty<string> Name { get; } = new(string.Empty);

    public string Path { get; }

    public long LastLaunchedTicks { get; }

    public BindableReactiveProperty<ImageSource?> Icon { get; } = new(null);

    public BindableReactiveProperty<Visibility> IconVisibility => field ??=
        Icon
        .Select(icon => icon is null ? Visibility.Collapsed : Visibility.Visible)
        .ToBindableReactiveProperty(Visibility.Collapsed);

    public BindableReactiveProperty<Visibility> FallbackIconVisibility => field ??=
        Icon
        .Select(icon => icon is null ? Visibility.Visible : Visibility.Collapsed)
        .ToBindableReactiveProperty(Visibility.Visible);

    public BindableReactiveProperty<Visibility> SelectedVisualVisibility { get; } = new(Visibility.Collapsed);

    public void SetSelected(bool isSelected) => 
        SelectedVisualVisibility.Value = isSelected ? Visibility.Visible : Visibility.Collapsed;

    private async Task LoadIconAsync()
    {
        var icon = await TryLoadIconAsync(Path);
        Icon.Value = icon;
    }

    private static async Task<ImageSource?> TryLoadIconAsync(string path)
    {
        try
        {
            var iconBytes = await Task.Run(() => GameIconExtractor.TryExtractBestPng(path));
            if (iconBytes is null || iconBytes.Length == 0)
                return null;

            using var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
            {
                writer.WriteBytes(iconBytes);
                await writer.StoreAsync();
                await writer.FlushAsync();
            }

            stream.Seek(0);
            var image = new BitmapImage();
            await image.SetSourceAsync(stream);
            return image;
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            return null;
        }
    }
}
