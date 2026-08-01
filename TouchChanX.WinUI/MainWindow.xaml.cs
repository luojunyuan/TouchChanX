using Microsoft.UI.Xaml;
using R3;
using System.Numerics;
using TouchChanX.Persistence;
using Windows.Foundation;

namespace TouchChanX.WinUI;

/// <summary>
/// An empty window that can be used on its own or navigated to within a Frame.
/// </summary>
public sealed partial class MainWindow : Window
{
    public LocalizedStrings Strings { get; } = LocalizedStrings.Current;

    public MainWindow()
    {
        InitializeComponent();

        Touch.Clicked
            .Select(rect => Menu.TouchDockAnchor.SnapFromRect(this.Content.ActualSize.ToSize(), rect))
            .Subscribe(MenuTouch.ShowAt);
    }

    /// <summary>
    /// 手动激活 window 的 xbind，用于设置为子窗口后，Activated 事件不触发的情景
    /// </summary>
    public void InitializeBindings() => this.Bindings.Initialize();

    public void SetGamepadFeatureAvailable(bool isAvailable) =>
        MenuTouch.SetGamepadFeatureAvailable(isAvailable);
}

/// <summary>
/// 用于 x:Bind 的转换器。
/// </summary>
public static class Converters
{
    public static CornerRadius CircleCornerRadius(double width) => new(width / 2);

    public static Visibility InvertVisible(Visibility visible) =>
        visible == Visibility.Collapsed ? Visibility.Visible : Visibility.Collapsed;
}

public static class Shared
{
    public const int TouchSpacing = 2;

    public const double TouchSize = 80.0;
    public const double MenuSize = TouchSize * 4;

    extension(FrameworkElement element)
    {
        public Observable<bool> IsVisibleChanged => Observable.Create<bool>(observer =>
        {
            var token = element.RegisterPropertyChangedCallback(UIElement.VisibilityProperty, (_, _) =>
                observer.OnNext(element.Visibility == Visibility.Visible));

            return Disposable.Create(() => element.UnregisterPropertyChangedCallback(UIElement.VisibilityProperty, token));
        });
    }
}
