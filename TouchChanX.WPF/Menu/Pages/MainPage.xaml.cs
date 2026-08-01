using System.Windows.Controls;
using TouchChanX.Persistence;
using TouchChanX.WPF.Controls;

namespace TouchChanX.WPF.Menu.Pages;

/// <summary>
/// MainPage.xaml 的交互逻辑
/// </summary>
public partial class MainPage : UserControl
{
    public LocalizedStrings Strings { get; } = LocalizedStrings.Current;
    public IEnumerable<MenuButton> MenuButtons =>
        MainPageGrid.Children.OfType<MenuButton>();

    public MainPage()
    {
        InitializeComponent();
    }
}
