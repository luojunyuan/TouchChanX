using R3;
using R3.ObservableEvents;
using Windows.ApplicationModel.Core;
using Windows.UI;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml.Controls;

namespace TouchChanX.UWP;

/// <summary>
/// Main preference window shell.
/// </summary>
public sealed partial class MainPage : Page
{
    public MainPage()
    {
        InitializeComponent();
        SetupTitlebar();
        BindReactiveInteractions();
        Navigate("home");
    }

    private static void SetupTitlebar()
    {
        var coreTitleBar = CoreApplication.GetCurrentView().TitleBar;
        coreTitleBar.ExtendViewIntoTitleBar = true;

        var titleBar = ApplicationView.GetForCurrentView().TitleBar;
        titleBar.ButtonBackgroundColor = Colors.Transparent;
        titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
    }

    private void BindReactiveInteractions()
    {
        AppNav.Events().SelectionChanged
            .Select(e => e.args.SelectedItemContainer?.Tag as string)
            .WhereNotNull()
            .Subscribe(Navigate);
    }

    private void Navigate(string tag)
    {
        var pageType = tag switch
        {
            "settings" => typeof(SettingsPage),
            "about" => typeof(AboutPage),
            _ => typeof(HomePage),
        };

        if (ContentFrame.SourcePageType != pageType)
            ContentFrame.Navigate(pageType);

        AppNav.Header = tag switch
        {
            "settings" => "设置",
            "about" => "关于",
            _ => "主页",
        };
    }
}
