using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TouchChanX.WinUI.Controls;
using TouchChanX.Persistence;

namespace TouchChanX.WinUI;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Application
{
    private Window? _window;
    private GamepadWindow? _gamepadWindow;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        var mainWindow = new MainWindow();
        AttachSandboxOverlays(mainWindow);
        _window = mainWindow;
        _window.Activate();
    }

    /// <summary>
    /// Entry sandbox only: visual previews without Win32 battery business logic.
    /// </summary>
    private void AttachSandboxOverlays(MainWindow window)
    {
        if (window.Content is not Grid root)
            return;

        var flyout = new MessageFlyoutControl();
        root.Children.Add(flyout);

        var batteryHud = new BatteryHudControl
        {
            Margin = new Thickness(0, 12, 12, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
        };
        batteryHud.Apply(BatteryHudControl.CreateSampleState());
        root.Children.Add(batteryHud);

        var button = new Button
        {
            Content = LocalizedStrings.Current.SandboxSendMessage,
            MinWidth = 120,
            MinHeight = 44,
            Margin = new Thickness(0, 0, 24, 24),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
        };

        button.PointerPressed += (_, e) => e.Handled = true;
        button.Click += (_, _) => flyout.ShowMessage(LocalizedStrings.Current.MouseClick);
        root.Children.Add(button);

        var gamepadButton = new Button
        {
            Content = LocalizedStrings.Current.SandboxGamepadMapping,
            MinWidth = 120,
            MinHeight = 44,
            Margin = new Thickness(0, 0, 156, 24),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
        };

        gamepadButton.PointerPressed += (_, e) => e.Handled = true;
        gamepadButton.Click += (_, _) => ShowGamepadWindow();
        root.Children.Add(gamepadButton);
    }

    private void ShowGamepadWindow()
    {
        if (_gamepadWindow is { } existingWindow)
        {
            existingWindow.Activate();
            return;
        }

        var candidate = new GamepadWindow();
        candidate.Closed += (_, _) =>
        {
            if (ReferenceEquals(_gamepadWindow, candidate))
                _gamepadWindow = null;
        };
        _gamepadWindow = candidate;
        candidate.ShowWithoutActivation();
    }
}
