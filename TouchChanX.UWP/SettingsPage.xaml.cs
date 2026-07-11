using Windows.UI.Xaml.Controls;
using R3;
using TouchChanX.Persistence;
using Windows.Storage.Pickers;

namespace TouchChanX.UWP;

public sealed partial class SettingsPage : Page
{
    private readonly AppSettings _settings = new();

    public BindableReactiveProperty<bool> ExternalLauncherEnabled { get; }
    public BindableReactiveProperty<string> ExternalLauncherPath { get; }
    public BindableReactiveProperty<string> ExternalLauncherArgs { get; }
    public BindableReactiveProperty<string> LaunchCommandPreview { get; }

    public SettingsPage()
    {
        ExternalLauncherEnabled = new(_settings.ExternalLauncherEnabled);
        ExternalLauncherPath = new(_settings.ExternalLauncherPath);
        ExternalLauncherArgs = new(EnsureGamePathPlaceholder(_settings.ExternalLauncherArgs));
        LaunchCommandPreview = new(string.Empty);

        InitializeComponent();

        ExternalLauncherEnabled.Subscribe(value => _settings.ExternalLauncherEnabled = value);
        ExternalLauncherPath.Subscribe(value =>
        {
            _settings.ExternalLauncherPath = value;
            UpdateLaunchCommandPreview();
        });
        ExternalLauncherArgs.Subscribe(value =>
        {
            _settings.ExternalLauncherArgs = value;
            UpdateLaunchCommandPreview();
        });
    }

    private async void BrowseExternalLauncher_Click(object sender, Windows.UI.Xaml.RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".exe");
        var file = await picker.PickSingleFileAsync();
        if (file is not null)
            ExternalLauncherPath.Value = file.Path;
    }

    private void ExternalLauncherArgs_LostFocus(object sender, Windows.UI.Xaml.RoutedEventArgs e) =>
        ExternalLauncherArgs.Value = EnsureGamePathPlaceholder(ExternalLauncherArgs.Value);

    private void UpdateLaunchCommandPreview() =>
        LaunchCommandPreview.Value = $"{ExternalLauncherPath.Value} {ExternalLauncherArgs.Value}".Trim();

    private static string EnsureGamePathPlaceholder(string arguments) =>
        arguments.Contains("{GamePath}", StringComparison.Ordinal)
            ? arguments
            : $"{arguments} {{GamePath}}".Trim();
}
