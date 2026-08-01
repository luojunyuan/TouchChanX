using Microsoft.UI.Xaml.Controls;

namespace TouchChanX.WinUI.Menu.Model;

internal static class TouchMenuCatalog
{
    public static TouchMenuPageDescriptor Main { get; } = new(
        TouchMenuPageId.Main,
        MenuCell.Center,
        [
            Navigate("device", "MenuDevice", ExtendedSymbol.Tablet, new(0, 1), TouchMenuPageId.Device),
            Navigate("game", "MenuGame", ExtendedSymbol.Favicon, new(1, 0), TouchMenuPageId.Game),
            Navigate("function", "MenuFunction", Symbol.Repair, new(1, 2), TouchMenuPageId.Function),
        ]);

    public static TouchMenuPageDescriptor Device { get; } = new(
        TouchMenuPageId.Device,
        new(0, 1),
        [
            Command("volume-down", "MenuVolumeDown", ExtendedSymbol.Volume1, new(0, 0)),
            Command("volume-up", "MenuVolumeUp", Symbol.Volume, new(0, 1)),
            Command("screenshot", "MenuScreenshot", ExtendedSymbol.ClippingTool, new(0, 2)),
            Command("task-view", "MenuTaskView", ExtendedSymbol.TaskView, new(1, 0)),
            Navigate("device-back", string.Empty, Symbol.Back, new(1, 1), TouchMenuPageId.Main),
            Command("action-center", "MenuActionCenter", Symbol.DockRight, new(1, 2)),
            Command("virtual-touchpad", "MenuTouchpad", ExtendedSymbol.Touchpad, new(2, 0)),
            Command("desktop", "MenuDesktop", ExtendedSymbol.StaplingLandscapeBottomRight, new(2, 1)),
        ]);

    public static TouchMenuPageDescriptor Game { get; } = new(
        TouchMenuPageId.Game,
        new(1, 0),
        [
            Command("fullscreen", "MenuFullscreen", Symbol.FullScreen, new(0, 1)),
            Navigate("move-game", "MenuMove", Symbol.Trim, new(0, 2), TouchMenuPageId.WinMove),
            Command("stretch", "MenuStretch", ExtendedSymbol.AspectRatio, new(1, 0)),
            Navigate("game-back", string.Empty, Symbol.Back, new(1, 1), TouchMenuPageId.Main),
            Command("close-game", "MenuClose", ExtendedSymbol.ChromeClose, new(1, 2)),
            Command("brightness-down", "MenuDim", ExtendedSymbol.KeyboardLowerBrightness, new(2, 0)),
            Command("brightness-up", "MenuRestore", ExtendedSymbol.KeyboardBrightness, new(2, 1), isEnabled: false),
            Command("lock-game", "MenuLock", ExtendedSymbol.Lock, new(2, 2)),
        ]);

    public static TouchMenuPageDescriptor Function { get; } = new(
        TouchMenuPageId.Function,
        new(1, 2),
        [
            Toggle("touch-bar", "MenuTouchBar", Symbol.SwitchApps, new(0, 1), isEnabled: false),
            Toggle("keyboard", "MenuKeyboard", ExtendedSymbol.KeyboardClassic, new(0, 2), isEnabled: false),
            Toggle("touch-to-mouse", "MenuTapClick", Symbol.TouchPointer, new(1, 0), toolTipKey: "MenuTapClickTooltip"),
            Navigate("function-back", string.Empty, Symbol.Back, new(1, 1), TouchMenuPageId.Main),
            Toggle("battery", "MenuBattery", ExtendedSymbol.VerticalBattery3, new(1, 2)),
            Command("text-magnifier", "MenuTextMagnifier", ExtendedSymbol.TextSelect, new(2, 0), isEnabled: false),
            Toggle("gesture", "MenuGesture", ExtendedSymbol.FingerInking, new(2, 1)),
            Toggle("game-handler", "MenuGamepad", ExtendedSymbol.Game, new(2, 2)),
        ]);

    public static TouchMenuPageDescriptor WinMove { get; } = new(
        TouchMenuPageId.WinMove,
        new(0, 2),
        [
            Command("move-up", string.Empty, Symbol.Up, new(0, 1)),
            Command("move-left", string.Empty, Symbol.Back, new(1, 0)),
            Command("move-right", string.Empty, Symbol.Forward, new(1, 2)),
            Command("move-down", string.Empty, Symbol.Download, new(2, 1)),
        ]);

    public static TouchMenuPageDescriptor GetPage(TouchMenuPageId pageId) =>
        pageId switch
        {
            TouchMenuPageId.Main => Main,
            TouchMenuPageId.Device => Device,
            TouchMenuPageId.Game => Game,
            TouchMenuPageId.Function => Function,
            TouchMenuPageId.WinMove => WinMove,
            _ => throw new ArgumentOutOfRangeException(nameof(pageId), pageId, null),
        };

    private static TouchMenuItemDescriptor Command(
        string id,
        string textKey,
        Symbol symbol,
        MenuCell cell,
        bool isEnabled = true) =>
        new(id, textKey, symbol, cell, IsEnabled: isEnabled);

    private static TouchMenuItemDescriptor Navigate(
        string id,
        string textKey,
        Symbol symbol,
        MenuCell cell,
        TouchMenuPageId targetPage) =>
        new(id, textKey, symbol, cell, TouchMenuItemKind.Navigation, targetPage);

    private static TouchMenuItemDescriptor Toggle(
        string id,
        string textKey,
        Symbol symbol,
        MenuCell cell,
        bool isEnabled = true,
        string? toolTipKey = null) =>
        new(id, textKey, symbol, cell, TouchMenuItemKind.Toggle, IsEnabled: isEnabled, ToolTipKey: toolTipKey);
}

internal static class ExtendedSymbol
{
    public const Symbol ClippingTool = (Symbol)0xF406;
    public const Symbol Tablet = (Symbol)0xE70A;
    public const Symbol Favicon = (Symbol)0xE737;
    public const Symbol KeyboardClassic = (Symbol)0xE765;
    public const Symbol AspectRatio = (Symbol)0xE799;
    public const Symbol TaskView = (Symbol)0xE7C4;
    public const Symbol Game = (Symbol)0xE7FC;
    public const Symbol ChromeClose = (Symbol)0xE8BB;
    public const Symbol Volume1 = (Symbol)0xE993;
    public const Symbol KeyboardBrightness = (Symbol)0xED39;
    public const Symbol KeyboardLowerBrightness = (Symbol)0xED3A;
    public const Symbol FingerInking = (Symbol)0xED5F;
    public const Symbol Touchpad = (Symbol)0xEFA5;
    public const Symbol StaplingLandscapeBottomRight = (Symbol)0xF5A4;
    public const Symbol VerticalBattery3 = (Symbol)0xF5F5;
    public const Symbol TextSelect = (Symbol)0xF683;
    public const Symbol Lock = (Symbol)0xE72E;
}
