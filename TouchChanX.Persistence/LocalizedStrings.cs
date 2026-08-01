using System.Globalization;
using Windows.ApplicationModel.Resources;

namespace TouchChanX.Persistence;

/// <summary>
/// Provides the current package language resources to XAML and shared UI code.
/// </summary>
public sealed class LocalizedStrings
{
    private static readonly ResourceLoader Loader = ResourceLoader.GetForViewIndependentUse();

    public static LocalizedStrings Current { get; } = new();

    public string Description => Get(nameof(Description));
    public string DisplayName => Get(nameof(DisplayName));
    public string Confirm => Get(nameof(Confirm));
    public string Cancel => Get(nameof(Cancel));
    public string Save => Get(nameof(Save));
    public string Close => Get(nameof(Close));
    public string Browse => Get(nameof(Browse));
    public string Copy => Get(nameof(Copy));
    public string Done => Get(nameof(Done));
    public string On => Get(nameof(On));
    public string Off => Get(nameof(Off));
    public string MainNavHome => Get(nameof(MainNavHome));
    public string MainNavSettings => Get(nameof(MainNavSettings));
    public string MainNavAbout => Get(nameof(MainNavAbout));
    public string HomeTitle => Get(nameof(HomeTitle));
    public string HomeDescription => Get(nameof(HomeDescription));
    public string HomeAddGame => Get(nameof(HomeAddGame));
    public string HomeDropHint => Get(nameof(HomeDropHint));
    public string HomeListHint => Get(nameof(HomeListHint));
    public string HomeLaunch => Get(nameof(HomeLaunch));
    public string HomeRename => Get(nameof(HomeRename));
    public string HomeRemove => Get(nameof(HomeRemove));
    public string HomeLaunchFailedTitle => Get(nameof(HomeLaunchFailedTitle));
    public string ProtocolLaunchFailed => Get(nameof(ProtocolLaunchFailed));
    public string HomeDisplayNameHeader => Get(nameof(HomeDisplayNameHeader));
    public string HomeRenameTitle => Get(nameof(HomeRenameTitle));
    public string HomeDragCaption => Get(nameof(HomeDragCaption));
    public string AboutBuildLabel => Get(nameof(AboutBuildLabel));
    public string AboutTouchMenuHint => Get(nameof(AboutTouchMenuHint));
    public string AboutRateHeader => Get(nameof(AboutRateHeader));
    public string AboutRateDescription => Get(nameof(AboutRateDescription));
    public string AboutOpenSourceHeader => Get(nameof(AboutOpenSourceHeader));
    public string AboutOpenSourceDescription => Get(nameof(AboutOpenSourceDescription));
    public string AboutQqHeader => Get(nameof(AboutQqHeader));
    public string AboutQqDescription => Get(nameof(AboutQqDescription));
    public string QqGroupNumber => Get(nameof(QqGroupNumber));
    public string AboutLicenseNotice => Get(nameof(AboutLicenseNotice));
    public string SettingsTitle => Get(nameof(SettingsTitle));
    public string SettingsExternalLauncherHeader => Get(nameof(SettingsExternalLauncherHeader));
    public string SettingsExternalLauncherDescription => Get(nameof(SettingsExternalLauncherDescription));
    public string LauncherPathHeader => Get(nameof(LauncherPathHeader));
    public string LauncherPathPlaceholder => Get(nameof(LauncherPathPlaceholder));
    public string LauncherArgumentsHeader => Get(nameof(LauncherArgumentsHeader));
    public string InsertGamePath => Get(nameof(InsertGamePath));
    public string CommandPreviewHeader => Get(nameof(CommandPreviewHeader));
    public string TestLaunch => Get(nameof(TestLaunch));
    public string SettingsPlaceholderShellMenuHeader => Get(nameof(SettingsPlaceholderShellMenuHeader));
    public string SettingsPlaceholderShellMenuDescription => Get(nameof(SettingsPlaceholderShellMenuDescription));
    public string SettingsPlaceholderEdgeHeader => Get(nameof(SettingsPlaceholderEdgeHeader));
    public string SettingsPlaceholderEdgeDescription => Get(nameof(SettingsPlaceholderEdgeDescription));
    public string SettingsAutoSaveMessage => Get(nameof(SettingsAutoSaveMessage));
    public string LauncherPathInvalid => Get(nameof(LauncherPathInvalid));
    public string LauncherArgumentsHint => Get(nameof(LauncherArgumentsHint));
    public string LauncherArgumentsInvalid => Get(nameof(LauncherArgumentsInvalid));
    public string TestLauncherTitle => Get(nameof(TestLauncherTitle));
    public string ExternalDialogStartTest => Get(nameof(ExternalDialogStartTest));
    public string ExternalDialogExistingGames => Get(nameof(ExternalDialogExistingGames));
    public string ExternalDialogSelectOther => Get(nameof(ExternalDialogSelectOther));
    public string ExternalDialogPathHeader => Get(nameof(ExternalDialogPathHeader));
    public string ExternalDialogPathPlaceholder => Get(nameof(ExternalDialogPathPlaceholder));
    public string ExternalDialogWarning => Get(nameof(ExternalDialogWarning));
    public string MenuDevice => Get(nameof(MenuDevice));
    public string MenuGame => Get(nameof(MenuGame));
    public string MenuFunction => Get(nameof(MenuFunction));
    public string MenuVolumeDown => Get(nameof(MenuVolumeDown));
    public string MenuVolumeUp => Get(nameof(MenuVolumeUp));
    public string MenuScreenshot => Get(nameof(MenuScreenshot));
    public string MenuTaskView => Get(nameof(MenuTaskView));
    public string MenuActionCenter => Get(nameof(MenuActionCenter));
    public string MenuTouchpad => Get(nameof(MenuTouchpad));
    public string MenuDesktop => Get(nameof(MenuDesktop));
    public string MenuFullscreen => Get(nameof(MenuFullscreen));
    public string MenuMove => Get(nameof(MenuMove));
    public string MenuStretch => Get(nameof(MenuStretch));
    public string MenuClose => Get(nameof(MenuClose));
    public string MenuDim => Get(nameof(MenuDim));
    public string MenuRestore => Get(nameof(MenuRestore));
    public string MenuLock => Get(nameof(MenuLock));
    public string MenuTouchBar => Get(nameof(MenuTouchBar));
    public string MenuKeyboard => Get(nameof(MenuKeyboard));
    public string MenuTapClick => Get(nameof(MenuTapClick));
    public string MenuTapClickTooltip => Get(nameof(MenuTapClickTooltip));
    public string MenuBattery => Get(nameof(MenuBattery));
    public string MenuTextMagnifier => Get(nameof(MenuTextMagnifier));
    public string MenuGesture => Get(nameof(MenuGesture));
    public string MenuGamepad => Get(nameof(MenuGamepad));
    public string LockWindowTitle => Get(nameof(LockWindowTitle));
    public string Freeze => Get(nameof(Freeze));
    public string Unlock => Get(nameof(Unlock));
    public string GamepadMappingTitle => Get(nameof(GamepadMappingTitle));
    public string GamepadButtonHeader => Get(nameof(GamepadButtonHeader));
    public string GamepadKeyboardKeyHeader => Get(nameof(GamepadKeyboardKeyHeader));
    public string GamepadDPadLeft => Get(nameof(GamepadDPadLeft));
    public string GamepadDPadUp => Get(nameof(GamepadDPadUp));
    public string GamepadDPadRight => Get(nameof(GamepadDPadRight));
    public string GamepadDPadDown => Get(nameof(GamepadDPadDown));
    public string GamepadA => Get(nameof(GamepadA));
    public string GamepadB => Get(nameof(GamepadB));
    public string GamepadX => Get(nameof(GamepadX));
    public string GamepadY => Get(nameof(GamepadY));
    public string GamepadLB => Get(nameof(GamepadLB));
    public string GamepadRB => Get(nameof(GamepadRB));
    public string GamepadStart => Get(nameof(GamepadStart));
    public string GamepadBack => Get(nameof(GamepadBack));
    public string GamepadLeftStick => Get(nameof(GamepadLeftStick));
    public string GamepadRightStick => Get(nameof(GamepadRightStick));
    public string GamepadKeyLeft => Get(nameof(GamepadKeyLeft));
    public string GamepadKeyUp => Get(nameof(GamepadKeyUp));
    public string GamepadKeyRight => Get(nameof(GamepadKeyRight));
    public string GamepadKeyDown => Get(nameof(GamepadKeyDown));
    public string GamepadKeyEnter => Get(nameof(GamepadKeyEnter));
    public string GamepadKeySpace => Get(nameof(GamepadKeySpace));
    public string GamepadKeyNone => Get(nameof(GamepadKeyNone));
    public string GamepadKeyCtrl => Get(nameof(GamepadKeyCtrl));
    public string GamepadKeyShowMapping => Get(nameof(GamepadKeyShowMapping));
    public string GamepadClose => Get(nameof(GamepadClose));
    public string BatteryOnBattery => Get(nameof(BatteryOnBattery));
    public string BatteryCharging => Get(nameof(BatteryCharging));
    public string BatteryRecovering => Get(nameof(BatteryRecovering));
    public string BatteryTimeLeft => Get(nameof(BatteryTimeLeft));
    public string BatteryPowerDraw => Get(nameof(BatteryPowerDraw));
    public string BatteryCapacity => Get(nameof(BatteryCapacity));
    public string Unavailable => Get(nameof(Unavailable));
    public string BatteryTimeHoursFormat => Get(nameof(BatteryTimeHoursFormat));
    public string BatteryTimeMinutesFormat => Get(nameof(BatteryTimeMinutesFormat));
    public string BatteryPowerDrawFormat => Get(nameof(BatteryPowerDrawFormat));
    public string BatteryCapacityFormat => Get(nameof(BatteryCapacityFormat));
    public string BatteryPercentFormat => Get(nameof(BatteryPercentFormat));
    public string MouseClick => Get(nameof(MouseClick));
    public string GestureSpace => Get(nameof(GestureSpace));
    public string GestureRightClick => Get(nameof(GestureRightClick));
    public string GestureScrollUp => Get(nameof(GestureScrollUp));
    public string GestureScrollDown => Get(nameof(GestureScrollDown));
    public string SandboxSendMessage => Get(nameof(SandboxSendMessage));
    public string SandboxGamepadMapping => Get(nameof(SandboxGamepadMapping));
    public string UnknownDpiTitle => Get(nameof(UnknownDpiTitle));
    public string UnknownDpiContent => Get(nameof(UnknownDpiContent));
    public string ErrorInvalidGamePath => Get(nameof(ErrorInvalidGamePath));
    public string ErrorGamePathRequired => Get(nameof(ErrorGamePathRequired));
    public string ErrorShortcutResolve => Get(nameof(ErrorShortcutResolve));
    public string ErrorResolvedLinkPath => Get(nameof(ErrorResolvedLinkPath));
    public string ErrorExternalLauncherNotFound => Get(nameof(ErrorExternalLauncherNotFound));
    public string ErrorExternalLauncherInvalid => Get(nameof(ErrorExternalLauncherInvalid));
    public string ErrorExternalLauncherStart => Get(nameof(ErrorExternalLauncherStart));
    public string ErrorExternalLauncherStartDetails => Get(nameof(ErrorExternalLauncherStartDetails));
    public string ErrorGameStartTimeout => Get(nameof(ErrorGameStartTimeout));
    public string ErrorWindowNotFound => Get(nameof(ErrorWindowNotFound));
    public string ErrorUnableToLockGame => Get(nameof(ErrorUnableToLockGame));
    public string ErrorUnableToFreezeGame => Get(nameof(ErrorUnableToFreezeGame));

    public string Get(string key) => Loader.GetString(key);

    public string Format(string key, params object?[] arguments) =>
        string.Format(CultureInfo.InvariantCulture, Get(key), arguments);

    public IReadOnlyList<(string Button, string Key)> CreateGamepadMappings() =>
    [
        (GamepadDPadLeft, GamepadKeyLeft),
        (GamepadDPadUp, GamepadKeyUp),
        (GamepadDPadRight, GamepadKeyRight),
        (GamepadDPadDown, GamepadKeyDown),
        (GamepadA, GamepadKeyEnter),
        (GamepadB, GamepadKeySpace),
        (GamepadX, GamepadKeyNone),
        (GamepadY, GamepadKeyNone),
        (GamepadLB, GamepadKeyNone),
        (GamepadRB, GamepadKeyCtrl),
        (GamepadStart, GamepadKeyNone),
        (GamepadBack, GamepadKeyShowMapping),
        (GamepadLeftStick, GamepadKeyNone),
        (GamepadRightStick, GamepadKeyNone),
    ];
}
