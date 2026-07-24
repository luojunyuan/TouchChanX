using Microsoft.UI.Xaml;

namespace TouchChanX.WinUI;

/// <summary>
/// Non-interactive overlay for battery HUD and message flyout.
/// Shares the same lifecycle as <see cref="MainWindow"/>.
/// </summary>
public sealed partial class HudWindow : Window
{
    public HudWindow()
    {
        InitializeComponent();
    }

    public void ShowMessage(string message) => MessageFlyout.ShowMessage(message);

    public void SetBatteryVisible(bool isVisible) => BatteryHud.SetVisible(isVisible);

    public void ApplyBatteryState(Controls.BatteryHudState state) => BatteryHud.Apply(state);
}
