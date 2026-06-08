namespace TouchChanX.WinUI.Menu.Model;

internal static class TouchMenuCatalog
{
    public static TouchMenuPageDescriptor Main { get; } = new(
        TouchMenuPageId.Main,
        MenuCell.Center,
        [
            Navigate("device", "Device", MenuGlyph.Tablet, new(0, 1), TouchMenuPageId.Device),
            Navigate("game", "Game", MenuGlyph.Favicon, new(1, 0), TouchMenuPageId.Game),
            Navigate("function", "Function", MenuGlyph.Repair, new(1, 2), TouchMenuPageId.Function),
        ]);

    public static TouchMenuPageDescriptor Device { get; } = new(
        TouchMenuPageId.Device,
        new(0, 1),
        [
            Command("volume-down", "Volume Down", MenuGlyph.Volume1, new(0, 0)),
            Command("volume-up", "Volume Up", MenuGlyph.Volume, new(0, 1)),
            Command("screenshot", "Screenshot", MenuGlyph.Picture, new(0, 2)),
            Command("task-view", "Task View", MenuGlyph.TaskView, new(1, 0)),
            Navigate("device-back", string.Empty, MenuGlyph.Back, new(1, 1), TouchMenuPageId.Main),
            Command("action-center", "Action Center", MenuGlyph.DockRight, new(1, 2)),
            Command("virtual-touchpad", "Touchpad", MenuGlyph.Touchpad, new(2, 0)),
            Command("desktop", "Desktop", MenuGlyph.StaplingLandscapeBottomRight, new(2, 1)),
        ]);

    public static TouchMenuPageDescriptor Game { get; } = new(
        TouchMenuPageId.Game,
        new(1, 0),
        [
            Command("fullscreen", "Fullscreen", MenuGlyph.FullScreen, new(0, 1)),
            Navigate("move-game", "Move", MenuGlyph.Trim, new(0, 2), TouchMenuPageId.WinMove),
            Navigate("game-back", string.Empty, MenuGlyph.Back, new(1, 1), TouchMenuPageId.Main),
            Command("close-game", "Close", MenuGlyph.ChromeClose, new(1, 2)),
            Command("brightness-down", "Dim", MenuGlyph.KeyboardLowerBrightness, new(2, 0)),
            Command("brightness-up", "Restore", MenuGlyph.KeyboardBrightness, new(2, 1)),
        ]);

    public static TouchMenuPageDescriptor Function { get; } = new(
        TouchMenuPageId.Function,
        new(1, 2),
        [
            Toggle("keyboard", "Keyboard", MenuGlyph.KeyboardClassic, new(0, 0)),
            Toggle("stretch", "Stretch", MenuGlyph.AspectRatio, new(0, 2)),
            Toggle("touch-to-mouse", "Tap Click", MenuGlyph.TouchPointer, new(1, 0)),
            Navigate("function-back", string.Empty, MenuGlyph.Back, new(1, 1), TouchMenuPageId.Main),
            Toggle("battery", "Battery", MenuGlyph.VerticalBattery3, new(1, 2)),
            Command("gesture", "Gesture", MenuGlyph.FingerInking, new(2, 1), isEnabled: false),
            Command("game-handler", "Handler", MenuGlyph.Game, new(2, 2), isEnabled: false),
        ]);

    public static TouchMenuPageDescriptor WinMove { get; } = new(
        TouchMenuPageId.WinMove,
        new(0, 2),
        [
            Command("move-up", string.Empty, MenuGlyph.ArrowUp8, new(0, 1)),
            Command("move-left", string.Empty, MenuGlyph.ArrowLeft8, new(1, 0)),
            Command("move-right", string.Empty, MenuGlyph.ArrowRight8, new(1, 2)),
            Command("move-down", string.Empty, MenuGlyph.ArrowDown8, new(2, 1)),
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
        string text,
        string glyph,
        MenuCell cell,
        bool isEnabled = true) =>
        new(id, text, glyph, cell, IsEnabled: isEnabled);

    private static TouchMenuItemDescriptor Navigate(
        string id,
        string text,
        string glyph,
        MenuCell cell,
        TouchMenuPageId targetPage) =>
        new(id, text, glyph, cell, TouchMenuItemKind.Navigation, targetPage);

    private static TouchMenuItemDescriptor Toggle(
        string id,
        string text,
        string glyph,
        MenuCell cell) =>
        new(id, text, glyph, cell, TouchMenuItemKind.Toggle);
}

internal static class MenuGlyph
{
    public const string Back = "\uE72B";
    public const string Favicon = "\uE737";
    public const string FullScreen = "\uE740";
    public const string KeyboardClassic = "\uE765";
    public const string Volume = "\uE767";
    public const string Trim = "\uE78A";
    public const string AspectRatio = "\uE799";
    public const string TaskView = "\uE7C4";
    public const string TouchPointer = "\uE7C9";
    public const string Game = "\uE7FC";
    public const string Picture = "\uE8B9";
    public const string ChromeClose = "\uE8BB";
    public const string DockRight = "\uE90D";
    public const string Repair = "\uE90F";
    public const string Volume1 = "\uE993";
    public const string Tablet = "\uE70A";
    public const string KeyboardBrightness = "\uED39";
    public const string KeyboardLowerBrightness = "\uED3A";
    public const string FingerInking = "\uED5F";
    public const string Touchpad = "\uEFA5";
    public const string ArrowUp8 = "\uF0AD";
    public const string ArrowDown8 = "\uF0AE";
    public const string ArrowRight8 = "\uF0AF";
    public const string ArrowLeft8 = "\uF0B0";
    public const string StaplingLandscapeBottomRight = "\uF5A4";
    public const string VerticalBattery3 = "\uF5F5";
}
