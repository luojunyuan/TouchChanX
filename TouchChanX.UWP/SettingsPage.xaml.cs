using R3;
using R3.ObservableEvents;
using TouchChanX.Persistence;
using Windows.Storage.Pickers;
using Windows.System;
using Windows.UI.Popups;
using Windows.UI.Xaml.Controls;

namespace TouchChanX.UWP;

public sealed partial class SettingsPage : Page
{
    private readonly AppSettings _settings = new();

    public BindableReactiveProperty<bool> ExternalLauncherEnabled { get; }
    public BindableReactiveProperty<string> ExternalLauncherPath { get; }
    public BindableReactiveProperty<string> ExternalLauncherArgs { get; }
    public BindableReactiveProperty<bool> ExternalLauncherConfigurationValid { get; }
    public BindableReactiveProperty<string> LaunchCommandPreview { get; }

    public SettingsPage()
    {
        ExternalLauncherPath = new(_settings.ExternalLauncherPath);
        ExternalLauncherArgs = new(_settings.ExternalLauncherArgs);
        ExternalLauncherEnabled = new(
            _settings.ExternalLauncherEnabled &&
            ExternalLauncherConfiguration.IsValid(
                ExternalLauncherPath.Value,
                ExternalLauncherArgs.Value));
        ExternalLauncherConfigurationValid = Observable
            .CombineLatest(
                ExternalLauncherPath,
                ExternalLauncherArgs,
                ExternalLauncherConfiguration.IsValid)
            .ToBindableReactiveProperty(false);
        LaunchCommandPreview = Observable
            .CombineLatest(
                ExternalLauncherPath,
                ExternalLauncherArgs,
                static (path, arguments) => $"{path} {arguments}".Trim())
            .ToBindableReactiveProperty(string.Empty);

        InitializeComponent();

        ExternalLauncherEnabled.Subscribe(value => _settings.ExternalLauncherEnabled = value);
        ExternalLauncherPath.Subscribe(value => _settings.ExternalLauncherPath = value);
        ExternalLauncherArgs.Subscribe(value => _settings.ExternalLauncherArgs = value);
        ExternalLauncherConfigurationValid
            .Where(static isValid => !isValid)
            .Subscribe(_ => ExternalLauncherEnabled.Value = false);

        BrowseExternalLauncherButton.Events().Click.SubscribeAwait(
            async (_, _) =>
            {
                var picker = new FileOpenPicker();
                picker.FileTypeFilter.Add(".exe");
                var file = await picker.PickSingleFileAsync();
                if (file is not null)
                    ExternalLauncherPath.Value = file.Path;
            },
            AwaitOperation.Drop);

        InsertGamePathButton.Events().Click.Subscribe(_ =>
        {
            if (!ExternalLauncherConfiguration.HasGamePathPlaceholder(ExternalLauncherArgs.Value))
            {
                ExternalLauncherArgs.Value =
                    $"{ExternalLauncherArgs.Value} {ExternalLauncherConfiguration.GamePathPlaceholder}".Trim();
            }
        });

        TestExternalLauncherButton.Events().Click.SubscribeAwait(
            async (_, _) => await ShowExternalLauncherTestDialogAsync(),
            AwaitOperation.Drop);
    }

    private async Task ShowExternalLauncherTestDialogAsync()
    {
        if (!ExternalLauncherConfigurationValid.Value)
            return;

        var dialog = new ExternalLauncherTestDialog(
            _settings.Games
                .ToStoredGames()
                .OrderByDescending(static game => game.LastLaunchedTicks)
                .ToArray());
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;

        var uri = new Uri(
            $"touchchanx://test-external-launcher/?path={Uri.EscapeDataString(dialog.SelectedGamePath)}");
        if (!await Launcher.LaunchUriAsync(uri))
        {
            await new MessageDialog("无法调用 touchchanx 协议。", "无法启动测试").ShowAsync();
        }
    }
}
