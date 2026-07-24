using Microsoft.UI.Xaml;

namespace TouchChanX.WinUI;

/// <summary>
/// Non-interactive overlay for battery HUD and message flyout.
/// Shares the same lifecycle as <see cref="MainWindow"/>.
/// </summary>
public sealed partial class HudWindow : Window
{
    private const int MaxBrightnessLevel = 8;
    private int _brightnessLevel;

    public HudWindow()
    {
        InitializeComponent();
    }

    public void ShowMessage(string message) => MessageFlyout.ShowMessage(message);

    public void SetBatteryVisible(bool isVisible) => BatteryHud.SetVisible(isVisible);

    public void ApplyBatteryState(Controls.BatteryHudState state) => BatteryHud.Apply(state);

    public void Dim()
    {
        if (_brightnessLevel == MaxBrightnessLevel)
            return;

        _brightnessLevel++;
        DimMask.Opacity = _brightnessLevel / 10.0;
        DimMask.Visibility = Visibility.Visible;
    }

    public void RestoreBrightness()
    {
        _brightnessLevel = 0;
        DimMask.Opacity = 0;
        DimMask.Visibility = Visibility.Collapsed;
    }
}
