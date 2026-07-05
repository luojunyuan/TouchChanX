using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using R3;
using R3.ObservableEvents;

namespace TouchChanX.WinUI.Controls;

public sealed partial class MenuButton : UserControl
{
    public static readonly DependencyProperty SymbolProperty =
        DependencyProperty.Register(
            nameof(Symbol),
            typeof(Symbol),
            typeof(MenuButton),
            new PropertyMetadata(Symbol.Placeholder));

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

    public Symbol Symbol
    {
        get => (Symbol)GetValue(SymbolProperty);
        set => SetValue(SymbolProperty, value);
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

    public Observable<Unit> Clicked { get; }

    public MenuButton()
    {
        InitializeComponent();

        var pointerReleased = this.Events().PointerReleased.Share();
        var pointerExited = this.Events().PointerExited.Share();
        var pointerPressed =
            this.Events().PointerPressed
            .Where(_ => IsEnabled)
            .Merge(
                this.Events().PointerEntered
                .Where(e => IsEnabled && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed))
            .Share();

        Clicked =
            pointerPressed
            .SelectMany(_ =>
                pointerReleased
                .Take(1)
                .TakeUntil(pointerExited))
            .Where(_ => IsEnabled)
            .Do(_ => ToggleIfNeeded())
            .AsUnitObservable()
            .Share();

        pointerPressed
            .Subscribe(_ => RefreshVisualState(isPressed: true));
        pointerReleased
            .Merge(pointerExited)
            .Subscribe(_ => RefreshVisualState());

        RegisterPropertyChangedCallback(IsEnabledProperty, (_, _) => RefreshVisualState());
        RefreshTextVisibility();
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

    private void ToggleIfNeeded()
    {
        if (IsToggle)
            IsOn = !IsOn;
    }

    private void RefreshVisualState(bool isPressed = false)
    {
        if (ItemIcon is null || ItemText is null)
            return;

        var brush = SelectForegroundBrush(isPressed);
        Foreground = brush;
        ItemIcon.Foreground = brush;
        ItemText.Foreground = brush;
    }

    private Brush SelectForegroundBrush(bool isPressed)
    {
        if (!IsEnabled)
            return (Brush)Resources["DisabledBrush"];

        if (isPressed)
            return (Brush)Resources["PressedBrush"];

        if (IsToggle && IsOn)
            return (Brush)Resources["AccentBrush"];

        return (Brush)Resources["NormalBrush"];
    }
}
