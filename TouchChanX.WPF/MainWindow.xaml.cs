using R3;
using System.Windows;
using TouchChanX.Persistence;
using TouchChanX.WPF.Menu;
using TouchChanX.WPF.Touch;

namespace TouchChanX.WPF;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public LocalizedStrings Strings { get; } = LocalizedStrings.Current;

    public MainWindow()
    {
        InitializeComponent();

        Touch.Clicked.Subscribe(Menu.ShowAt);

        // 订阅一些动画期间都禁止整个页面再次交互
        Observable.Merge(TouchControl.AnimationRunning, MenuControl.AnimationRunning)
            .DistinctUntilChanged()
            .Subscribe(running => this.IsHitTestVisible = !running);
    }
}
