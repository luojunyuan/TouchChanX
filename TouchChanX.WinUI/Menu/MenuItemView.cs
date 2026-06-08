using Microsoft.UI.Xaml;
using TouchChanX.WinUI.Menu.Model;

namespace TouchChanX.WinUI.Menu;

internal sealed record MenuItemView(TouchMenuItemDescriptor Descriptor, FrameworkElement Element);
