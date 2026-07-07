using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using R3;

namespace TouchChanX.WinUI.Controls;

public sealed partial class MenuButton : UserControl
{
    private uint? _activePointerId;
    private readonly Subject<Unit> _clickedSubject = new();

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

        Clicked =
            _clickedSubject
            .Where(_ => IsEnabled)
            .Do(_ => ToggleIfNeeded())
            .AsUnitObservable()
            .Share();

        AddHandler(PointerPressedEvent, new PointerEventHandler(OnPointerPressed), true);
        AddHandler(PointerEnteredEvent, new PointerEventHandler(OnPointerEntered), true);
        AddHandler(PointerExitedEvent, new PointerEventHandler(OnPointerExited), true);
        AddHandler(PointerReleasedEvent, new PointerEventHandler(OnPointerReleased), true);
        AddHandler(PointerCanceledEvent, new PointerEventHandler(OnPointerCanceled), true);

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

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!IsEnabled)
            return;

        StartPointerPress(e);
    }

    private void OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (!IsEnabled || !IsPointerInContact(e))
            return;

        StartPointerPress(e);
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (_activePointerId != e.Pointer.PointerId)
            return;

        EndPointerPress(e);
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_activePointerId != e.Pointer.PointerId)
            return;

        EndPointerPress(e);

        if (IsEnabled && IsPointerInside(e))
            _clickedSubject.OnNext(Unit.Default);
    }

    private void OnPointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        if (_activePointerId != e.Pointer.PointerId)
            return;

        EndPointerPress(e);
    }

    private void StartPointerPress(PointerRoutedEventArgs e)
    {
        _activePointerId = e.Pointer.PointerId;
        RefreshVisualState(isPressed: true);
        e.Handled = true;
    }

    private void EndPointerPress(PointerRoutedEventArgs e)
    {
        _activePointerId = null;
        RefreshVisualState();
        e.Handled = true;
    }

    private bool IsPointerInContact(PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        return point.IsInContact || point.Properties.IsLeftButtonPressed;
    }

    private bool IsPointerInside(PointerRoutedEventArgs e)
    {
        var position = e.GetCurrentPoint(this).Position;
        return position.X >= 0
            && position.Y >= 0
            && position.X <= ActualWidth
            && position.Y <= ActualHeight;
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
