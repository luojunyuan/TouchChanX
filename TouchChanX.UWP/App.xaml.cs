using Windows.ApplicationModel.Activation;
using Windows.Storage;
using Windows.UI.Xaml;
using R3;
using R3.ObservableEvents;
using Windows.UI.Xaml.Controls;

namespace TouchChanX.UWP;

/// <summary>
/// Provides application-specific behavior to supplement the default <see cref="Application"/> class.
/// </summary>
public sealed partial class App : Application
{
    private const string DevelopmentNoticeShownKey = "DevelopmentNoticeShown";

    /// <summary>
    /// Initializes the singleton application object. This is the first line of authored code
    /// executed, and as such is the logical equivalent of main() or WinMain().
    /// </summary>
    public App()
    {
        InitializeComponent();
    }

    /// <inheritdoc/>
    protected override async void OnLaunched(LaunchActivatedEventArgs e)
    {
        // Do not repeat app initialization when the Window already has content,
        // just ensure that the window is active.
        if (Window.Current.Content is not Frame rootFrame)
        {
            // Create a Frame to act as the navigation context and navigate to the first page
            rootFrame = new Frame();
            rootFrame.Events().NavigationFailed
                .Subscribe(e => throw new Exception($"Failed to load page '{e.SourcePageType.FullName}'."));

            if (e.PreviousExecutionState == ApplicationExecutionState.Terminated)
            {
                // TODO: Load state from previously suspended application
            }

            // Place the frame in the current Window
            Window.Current.Content = rootFrame;
        }

        if (e.PrelaunchActivated == false)
        {
            var showDevelopmentNotice = false;

            if (rootFrame.Content == null)
            {
                // When the navigation stack isn't restored navigate to the first page, configuring
                // the new page by passing required information as a navigation parameter.
                rootFrame.Navigate(typeof(MainPage), e.Arguments);
                showDevelopmentNotice = true;
            }

            // Ensure the current window is active
            Window.Current.Activate();

            if (showDevelopmentNotice)
            {
                var localSettings = ApplicationData.Current.LocalSettings;
                if (localSettings.Values[DevelopmentNoticeShownKey] is not true)
                {
                    localSettings.Values[DevelopmentNoticeShownKey] = true;

                    await new ContentDialog
                    {
                        Title = "提示",
                        Content = "触控酱v3 arm64 仍处于开发中，欢迎加入QQ群 942698378 交流反馈",
                        CloseButtonText = "确定",
                    }.ShowAsync();
                }
            }
        }
    }
}
