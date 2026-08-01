using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TouchChanX.Persistence;

namespace TouchChanX.WinUI.Controls;

/// <summary>
/// Presentation model for <see cref="BatteryHudControl"/>. UI-only; no battery IO.
/// </summary>
public readonly record struct BatteryHudState(
    string StatusText,
    string PercentText,
    string TimeLeftText,
    string PowerDrawText,
    string CapacityText,
    double PercentFraction,
    bool HasBattery,
    bool IsCharging);

/// <summary>
/// Compact battery HUD view (Figma Compact HUD · labeled). Display only.
/// </summary>
public sealed partial class BatteryHudControl : UserControl
{
    public LocalizedStrings Strings { get; } = LocalizedStrings.Current;

    private const double ProgressRailWidth = 250.0;
    private const string UnknownBatteryGlyph = "\uF608";

    private static readonly string[] BatteryGlyphs =
    [
        "\uF5F2", "\uF5F3", "\uF5F4", "\uF5F5", "\uF5F6", "\uF5F7",
        "\uF5F8", "\uF5F9", "\uF5FA", "\uF5FB", "\uF5FC",
    ];

    private static readonly string[] ChargingBatteryGlyphs =
    [
        "\uF5FD", "\uF5FE", "\uF5FF", "\uF600", "\uF601", "\uF602",
        "\uF603", "\uF604", "\uF605", "\uF606", "\uF607",
    ];

    public BatteryHudControl()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Sample state for design-time / Entry sandbox preview (no real battery IO).
    /// </summary>
    public static BatteryHudState CreateSampleState() =>
        CreateSampleState(LocalizedStrings.Current);

    private static BatteryHudState CreateSampleState(LocalizedStrings strings) =>
        new(
            StatusText: strings.BatteryOnBattery,
            PercentText: strings.Format(nameof(LocalizedStrings.BatteryPercentFormat), 68),
            TimeLeftText: strings.Format(nameof(LocalizedStrings.BatteryTimeHoursFormat), 5, 42),
            PowerDrawText: strings.Format(nameof(LocalizedStrings.BatteryPowerDrawFormat), 6.4, 5),
            CapacityText: strings.Format(nameof(LocalizedStrings.BatteryCapacityFormat), 45.2),
            PercentFraction: 0.68,
            HasBattery: true,
            IsCharging: false);

    public void SetVisible(bool isVisible) =>
        Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;

    public void Apply(BatteryHudState state)
    {
        BatteryIcon.Glyph = GetBatteryGlyph(state);
        StatusText.Text = state.StatusText;
        PercentText.Text = state.PercentText;
        TimeLeftValue.Text = state.TimeLeftText;
        PowerDrawValue.Text = state.PowerDrawText;
        CapacityValue.Text = state.CapacityText;
        ProgressFill.Width = ProgressRailWidth * Math.Clamp(state.PercentFraction, 0, 1);
    }

    private static string GetBatteryGlyph(BatteryHudState state)
    {
        if (!state.HasBattery)
            return UnknownBatteryGlyph;

        var level = Math.Clamp(
            (int)Math.Round(state.PercentFraction * 10, MidpointRounding.AwayFromZero),
            0,
            10);
        return (state.IsCharging ? ChargingBatteryGlyphs : BatteryGlyphs)[level];
    }
}
