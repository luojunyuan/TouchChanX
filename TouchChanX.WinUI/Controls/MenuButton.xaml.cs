using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using R3;

namespace TouchChanX.WinUI.Controls;

public sealed partial class MenuButton : UserControl
{
    public static readonly DependencyProperty GlyphProperty =
        DependencyProperty.Register(
            nameof(Glyph),
            typeof(string),
            typeof(MenuButton),
            new PropertyMetadata("\uE11D"));

    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(
            nameof(Text),
            typeof(string),
            typeof(MenuButton),
            new PropertyMetadata(string.Empty, OnTextChanged));

    public static readonly DependencyProperty IsToggleProperty =
        DependencyProperty.Register(
            nameof(IsToggle),
            typeof(bool),
            typeof(MenuButton),
            new PropertyMetadata(false, OnVisualStatePropertyChanged));

    public static readonly DependencyProperty IsOnProperty =
        DependencyProperty.Register(
            nameof(IsOn),
            typeof(bool),
            typeof(MenuButton),
            new PropertyMetadata(false, OnVisualStatePropertyChanged));

    private readonly Subject<Unit> _clicked = new();
    private bool _isPointerDown;

    public string Glyph
    {
        get => (string)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public bool IsToggle
    {
        get => (bool)GetValue(IsToggleProperty);
        set => SetValue(IsToggleProperty, value);
    }

    public bool IsOn
    {
        get => (bool)GetValue(IsOnProperty);
        set => SetValue(IsOnProperty, value);
    }

    public Observable<Unit> Clicked => _clicked;

    public MenuButton()
    {
        InitializeComponent();
        RegisterPropertyChangedCallback(IsEnabledProperty, (_, _) => RefreshVisualState());
        RefreshTextVisibility();
        RefreshVisualState();
    }

    protected override void OnPointerPressed(PointerRoutedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (!IsEnabled)
            return;

        _isPointerDown = true;
        RefreshVisualState();
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerRoutedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (!IsEnabled || !_isPointerDown)
            return;

        _isPointerDown = false;
        if (IsToggle)
            IsOn = !IsOn;

        RefreshVisualState();
        _clicked.OnNext(Unit.Default);
        e.Handled = true;
    }

    protected override void OnPointerExited(PointerRoutedEventArgs e)
    {
        base.OnPointerExited(e);

        _isPointerDown = false;
        RefreshVisualState();
    }

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((MenuButton)d).RefreshTextVisibility();

    private static void OnVisualStatePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((MenuButton)d).RefreshVisualState();

    private void RefreshTextVisibility()
    {
        if (ItemText is null)
            return;

        ItemText.Visibility = string.IsNullOrWhiteSpace(Text)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void RefreshVisualState()
    {
        if (ItemIcon is null || ItemText is null)
            return;

        var brush = SelectForegroundBrush();
        Foreground = brush;
        ItemIcon.Foreground = brush;
        ItemText.Foreground = brush;
    }

    private Brush SelectForegroundBrush()
    {
        if (!IsEnabled)
            return (Brush)Resources["DisabledBrush"];

        if (_isPointerDown)
            return (Brush)Resources["PressedBrush"];

        if (IsToggle && IsOn)
            return (Brush)Resources["AccentBrush"];

        return (Brush)Resources["NormalBrush"];
    }
}
