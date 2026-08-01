using R3;
using R3.ObservableEvents;
using Windows.Storage.Pickers;
using Windows.UI.Xaml.Controls;
using TouchChanX.Persistence;

namespace TouchChanX.UWP;

public sealed partial class ExternalLauncherTestDialog : ContentDialog
{
    public LocalizedStrings Strings { get; } = LocalizedStrings.Current;

    public List<GameEntry> Games { get; }
    public BindableReactiveProperty<GameEntry?> SelectedGame { get; }
    public BindableReactiveProperty<string> TestGamePath { get; }
    public BindableReactiveProperty<bool> CanStart { get; }

    public string SelectedGamePath => TestGamePath.Value;

    public ExternalLauncherTestDialog(IReadOnlyList<GameEntry> games)
    {
        Games = [.. games.Where(static game => IsSupportedGamePath(game.Path))];
        SelectedGame = new(Games.FirstOrDefault());
        TestGamePath = new(SelectedGame.Value?.Path ?? string.Empty);
        CanStart = TestGamePath
            .Select(static path => IsSupportedGamePath(path))
            .ToBindableReactiveProperty(false);

        InitializeComponent();

        SelectedGame
            .WhereNotNull()
            .Subscribe(game => TestGamePath.Value = game.Path);
        BrowseTestGameButton.Events().Click.SubscribeAwait(
            async (_, _) =>
            {
                var picker = new FileOpenPicker();
                picker.FileTypeFilter.Add(".exe");
                var file = await picker.PickSingleFileAsync();
                if (file is null)
                    return;

                SelectedGame.Value = null;
                TestGamePath.Value = file.Path;
            },
            AwaitOperation.Drop);
    }

    private static bool IsSupportedGamePath(string? path) =>
        !string.IsNullOrWhiteSpace(path) &&
        File.Exists(path) &&
        Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase);
}
