using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using R3;
using R3.ObservableEvents;
using TouchChanX.Persistence;
using TouchChanX.WinUI.Controls;
using TouchChanX.WinUI.Menu.Model;
using Windows.UI;

namespace TouchChanX.WinUI.Menu;

public partial class MenuControl
{
    private static readonly Subject<string> CommandRequestedSubject = new();
    private static readonly Subject<TouchMenuToggleChanged> ToggleChangedSubject = new();

    public static Observable<Unit> ObservableRegionResetRequested { get; private set; } = Observable.Empty<Unit>();

    public static Observable<string> ObservableCommandRequested => CommandRequestedSubject;

    public static Observable<TouchMenuToggleChanged> ObservableToggleChanged => ToggleChangedSubject;
}

public sealed record TouchMenuToggleChanged(string Id, bool IsOn);

public sealed partial class MenuControl : UserControl
{
    /// <summary>Set by host when battery hardware is present.</summary>
    public static bool IsBatteryFeatureAvailable { get; set; }

    private const double MenuPadding = 24.0;
    private const int MaxBrightnessLevel = 8;

    private readonly AppSettings _settings = new();
    private readonly Dictionary<string, bool> _toggleStates = [];
    private Grid _activePageHost = null!;
    private Grid _inactivePageHost = null!;
    private Brush _menuBackgroundBrush = null!;
    private RenderedMenuPage? _activePage;
    private TouchMenuPageId _currentPageId = TouchMenuPageId.Main;
    private bool _isTransitioning;
    private bool _isGamepadFeatureAvailable;
    private int _brightnessLevel;

    private float CellDistance
    {
        get
        {
            var width = MenuBorder.ActualWidth > 0 ? MenuBorder.ActualWidth : Shared.MenuSize;
            return (float)((width - MenuPadding) / 3.0);
        }
    }

    public void ShowAt(TouchDockAnchor touchDock)
    {
        if (Visibility == Visibility.Visible)
            return;

        _lastTouchDockAnchor = touchDock;
        ResetToMainPage();
        if (_activePage is not null)
            PageAnimator.Reset(_activePage.Items);

        // 必须在 Visibility=Visible 之前隐藏 MenuBorder：
        // Visible 之后 XAML 会立即 layout 并渲染，而 OpenMenuAsync 要等 LayoutUpdated 才触发，
        // 两者之间有一帧间隙，会导致菜单项在屏幕正中心闪现。
        ElementCompositionPreview.GetElementVisual(MenuBorder).Opacity = 0f;
        Visibility = Visibility.Visible;
    }

    public MenuControl()
    {
        InitializeComponent();
        _menuBackgroundBrush = MenuBorder.Background ?? new SolidColorBrush(Color.FromArgb(0xFF, 0x2A, 0x2A, 0x2A));

        _activePageHost = CurrentPageHost;
        _inactivePageHost = NextPageHost;

        InitializeCompositionVisuals();
        TransitionPresentationVisible(false);
        ResetToMainPage();

        this.IsVisibleChanged
            .Where(isVisible => isVisible)
            .SelectMany(_ => this.Events().LayoutUpdated.Take(1).AsUnitObservable())
            .SubscribeAwait(async (_, _) => await OpenMenuAsync());

        this.Events().PointerReleased
            .Where(e => ShouldCloseFromPointerSource(e.OriginalSource))
            .SubscribeAwait(async (_, _) => await CloseMenuAsync());

        ObservableRegionResetRequested = this.Events().SizeChanged.AsUnitObservable();
    }

    public void SetGamepadFeatureAvailable(bool isAvailable)
    {
        if (!isAvailable)
            _toggleStates["game-handler"] = false;

        if (_isGamepadFeatureAvailable == isAvailable)
            return;

        _isGamepadFeatureAvailable = isAvailable;

        if (_activePage?.Items.FirstOrDefault(item => item.Descriptor.Id == "game-handler")?.Element is not MenuButton button)
            return;

        button.IsEnabled = isAvailable;
        if (isAvailable || !button.IsOn)
            return;

        button.IsOn = false;
        ToggleChangedSubject.OnNext(new("game-handler", false));
    }

    private async Task OpenMenuAsync()
    {
        if (_activePage is null)
            ResetToMainPage();

        _isTransitioning = true;
        IsHitTestVisible = false;

        // Items 随壳整体缩放，不做独立淡入，先确保它们处于正常可见状态
        PageAnimator.Reset(_activePage!.Items);
        // 预先隐藏 MenuBorder，防止首次展开时菜单项在正中心闪现一帧
        ElementCompositionPreview.GetElementVisual(MenuBorder).Opacity = 0f;
        TransitionPresentationVisible(true);
        await Task.WhenAll(
            PlayMenuTransitionAnimationAsync(),
            PlayMenuContentScaleTranslationAnimationAsync());
        TransitionPresentationVisible(false);
        // 展开后归还 MenuBorder 给 XAML 布局管理（确保窗口 resize 时位置正确）
        ResetMenuContentVisual();

        IsHitTestVisible = true;
        _isTransitioning = false;
    }

    private async Task CloseMenuAsync()
    {
        if (_isTransitioning || Visibility != Visibility.Visible)
            return;

        _isTransitioning = true;
        IsHitTestVisible = false;

        TransitionPresentationVisible(true);
        await Task.WhenAll(
            PlayMenuTransitionAnimationAsync(showing: false),
            _activePage is not null
                ? PlayMenuContentScaleTranslationAnimationAsync(showing: false)
                : Task.CompletedTask);

        Visibility = Visibility.Collapsed;
        TransitionPresentationVisible(false);
        // 重置 Composition 变换，防止下次展开前短暂显示缩放状态
        ResetMenuContentVisual();

        IsHitTestVisible = true;
        _isTransitioning = false;
    }

    private async Task HandleMenuItemClickedAsync(TouchMenuItemDescriptor item, MenuButton button)
    {
        if (_isTransitioning)
            return;

        if (item.Kind == TouchMenuItemKind.Toggle)
        {
            _toggleStates[item.Id] = button.IsOn;
            SaveToggleState(item.Id, button.IsOn);
            ToggleChangedSubject.OnNext(new(item.Id, button.IsOn));
        }

        if (item.Kind == TouchMenuItemKind.Navigation && item.TargetPage is { } targetPage)
            await SwitchPageAsync(targetPage, item.Cell);

        if (item.Kind == TouchMenuItemKind.Command)
            await HandleCommandAsync(item.Id);
    }

    private async Task HandleCommandAsync(string commandId)
    {
        if (ShouldCloseBeforeCommand(commandId))
            await CloseMenuAsync();

        UpdateBrightnessState(commandId);
        CommandRequestedSubject.OnNext(commandId);
    }

    private void UpdateBrightnessState(string commandId)
    {
        switch (commandId)
        {
            case "brightness-down":
                if (_brightnessLevel < MaxBrightnessLevel)
                    _brightnessLevel++;
                break;
            case "brightness-up":
                _brightnessLevel = 0;
                break;
            default:
                return;
        }

        SetActiveCommandEnabled("brightness-down", _brightnessLevel < MaxBrightnessLevel);
        SetActiveCommandEnabled("brightness-up", _brightnessLevel > 0);
    }

    private void SetActiveCommandEnabled(string commandId, bool isEnabled)
    {
        if (_activePage?.Items.FirstOrDefault(item => item.Descriptor.Id == commandId)?.Element is MenuButton button)
            button.IsEnabled = isEnabled;
    }

    private static bool ShouldCloseBeforeCommand(string commandId) =>
        commandId is "screenshot" or "close-game" or "lock-game";

    private async Task SwitchPageAsync(TouchMenuPageId targetPageId, MenuCell origin)
    {
        if (_activePage is null || _currentPageId == targetPageId || _isTransitioning)
            return;

        _isTransitioning = true;
        IsHitTestVisible = false;

        var oldPage = _activePage;
        var oldHost = _activePageHost;
        var newHost = _inactivePageHost;
        var newPage = BuildPage(newHost, TouchMenuCatalog.GetPage(targetPageId));

        newHost.IsHitTestVisible = false;
        if (targetPageId == TouchMenuPageId.Main)
        {
            PageAnimator.PrepareHiddenInPlace(newPage.Items);
            await PageAnimator.PlayExitAsync(
                oldPage.Items,
                oldPage.Descriptor.DefaultOrigin,
                CellDistance,
                PageTransitionDuration);

            DisposePage(oldPage);

            newHost.Visibility = Visibility.Visible;
            await PageAnimator.PlayFadeInAsync(newPage.Items, PageTransitionDuration);
        }
        else
        {
            PageAnimator.PrepareHidden(newPage.Items, origin, CellDistance);
            newHost.Visibility = Visibility.Visible;

            await PageAnimator.PlaySwitchAsync(
                oldPage.Items,
                newPage.Items,
                origin,
                CellDistance,
                PageTransitionDuration);

            DisposePage(oldPage);
        }

        PageAnimator.Reset(newPage.Items);
        _activePage = newPage;
        _activePageHost = newHost;
        _activePageHost.IsHitTestVisible = true;
        _inactivePageHost = oldHost;
        _currentPageId = targetPageId;

        IsHitTestVisible = true;
        _isTransitioning = false;
    }

    private void ResetToMainPage()
    {
        DisposePage(_activePage);
        _activePageHost = CurrentPageHost;
        _inactivePageHost = NextPageHost;
        _inactivePageHost.Children.Clear();
        _inactivePageHost.Visibility = Visibility.Collapsed;
        _inactivePageHost.IsHitTestVisible = false;

        _currentPageId = TouchMenuPageId.Main;
        _activePage = BuildPage(_activePageHost, TouchMenuCatalog.Main);
        _activePageHost.Visibility = Visibility.Visible;
        _activePageHost.IsHitTestVisible = true;
    }

    private RenderedMenuPage BuildPage(Grid host, TouchMenuPageDescriptor descriptor)
    {
        host.Children.Clear();

        var items = new List<MenuItemView>();
        var subscriptions = new List<IDisposable>();

        foreach (var item in descriptor.Items)
        {
            var cellHost = new Grid
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
            };

            var isOn = _toggleStates.TryGetValue(item.Id, out var toggleState)
                ? toggleState
                : item.Kind == TouchMenuItemKind.Toggle
                    ? LoadToggleState(item.Id, item.IsOn)
                    : item.IsOn;

            var button = new MenuButton
            {
                Symbol = item.Symbol,
                Text = item.Text,
                IsToggle = item.Kind == TouchMenuItemKind.Toggle,
                IsOn = isOn,
                IsEnabled = IsCommandEnabled(item),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
            };

            if (!string.IsNullOrWhiteSpace(item.ToolTip))
                ToolTipService.SetToolTip(button, item.ToolTip);

            Grid.SetRow(cellHost, item.Cell.Row);
            Grid.SetColumn(cellHost, item.Cell.Column);

            cellHost.Children.Add(button);
            host.Children.Add(cellHost);
            items.Add(new MenuItemView(item, button));
            subscriptions.Add(button.Clicked.SubscribeAwait(async (_, _) =>
                await HandleMenuItemClickedAsync(item, button)));
        }

        return new RenderedMenuPage(descriptor, host, items, subscriptions);
    }

    private bool IsCommandEnabled(TouchMenuItemDescriptor item) =>
        item.Id switch
        {
            "brightness-down" => _brightnessLevel < MaxBrightnessLevel,
            "brightness-up" => _brightnessLevel > 0,
            "battery" => IsBatteryFeatureAvailable,
            "game-handler" => _isGamepadFeatureAvailable,
            _ => item.IsEnabled,
        };

    private static void DisposePage(RenderedMenuPage? page)
    {
        if (page is null)
            return;

        foreach (var subscription in page.Subscriptions)
            subscription.Dispose();

        page.Host.Children.Clear();
        page.Host.Visibility = Visibility.Collapsed;
        page.Host.IsHitTestVisible = false;
    }

    private bool ShouldCloseFromPointerSource(object? originalSource) =>
        ReferenceEquals(originalSource, MenuBorder) ||
        ReferenceEquals(originalSource, BackgroundLayer);

    private bool LoadToggleState(string id, bool defaultValue) =>
        id switch
        {
            "touch-bar" => _settings.TouchBar,
            "keyboard" => _settings.Keyboard,
            "touch-to-mouse" => _settings.TouchToMouse,
            "battery" => _settings.Battery,
            "gesture" => _settings.Gesture,
            "game-handler" => _isGamepadFeatureAvailable &&
                (!_settings.HasGamepadSetting || _settings.Gamepad),
            _ => defaultValue,
        };

    private void SaveToggleState(string id, bool isOn)
    {
        switch (id)
        {
            case "touch-bar":
                _settings.TouchBar = isOn;
                break;
            case "keyboard":
                _settings.Keyboard = isOn;
                break;
            case "touch-to-mouse":
                _settings.TouchToMouse = isOn;
                break;
            case "battery":
                _settings.Battery = isOn;
                break;
            case "gesture":
                _settings.Gesture = isOn;
                break;
            case "game-handler":
                _settings.Gamepad = isOn;
                break;
        }
    }

    /// <summary>
    /// 控制动画过渡层显隐，仅在动画前后调用。
    /// </summary>
    private void TransitionPresentationVisible(bool isVisible)
    {
        MenuBorder.Background = isVisible
            ? null
            : _menuBackgroundBrush;
        TransitionShellHost.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
        TransitionItemsHost.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    private sealed record RenderedMenuPage(
        TouchMenuPageDescriptor Descriptor,
        Grid Host,
        IReadOnlyList<MenuItemView> Items,
        IReadOnlyList<IDisposable> Subscriptions);
}
