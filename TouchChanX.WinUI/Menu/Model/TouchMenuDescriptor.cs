using Microsoft.UI.Xaml.Controls;

namespace TouchChanX.WinUI.Menu.Model;

internal enum TouchMenuPageId
{
    Main,
    Device,
    Game,
    Function,
    WinMove,
}

internal enum TouchMenuItemKind
{
    Command,
    Navigation,
    Toggle,
}

internal readonly record struct MenuCell(int Row, int Column)
{
    public static MenuCell Center { get; } = new(1, 1);
}

internal sealed record TouchMenuItemDescriptor(
    string Id,
    string Text,
    Symbol Symbol,
    MenuCell Cell,
    TouchMenuItemKind Kind = TouchMenuItemKind.Command,
    TouchMenuPageId? TargetPage = null,
    bool IsEnabled = true,
    bool IsOn = false);

internal sealed record TouchMenuPageDescriptor(
    TouchMenuPageId Id,
    MenuCell DefaultOrigin,
    IReadOnlyList<TouchMenuItemDescriptor> Items);
