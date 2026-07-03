using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using R3;
using R3.ObservableEvents;
using TouchChanX.WinUI.Controls;
using TouchChanX.WinUI.Menu.Model;
using Windows.UI;

namespace TouchChanX.WinUI.Menu;

public partial class MenuControl
{
    public static Observable<Unit> ObservableRegionResetRequested { get; private set; } = Observable.Empty<Unit>();
}

public sealed partial class MenuControl : UserControl
{
    private const double MenuPadding = 24.0;

    private readonly Dictionary<string, bool> _toggleStates = [];
    private Grid _activePageHost = null!;
    private Grid _inactivePageHost = null!;
    private Brush _menuBackgroundBrush = null!;
    private RenderedMenuPage? _activePage;
    private TouchMenuPageId _currentPageId = TouchMenuPageId.Main;
    private bool _isTransitioning;

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
            PageAnimator.PrepareHiddenInPlace(_activePage.Items);

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

    private async Task OpenMenuAsync()
    {
        if (_activePage is null)
            ResetToMainPage();

        _isTransitioning = true;
        IsHitTestVisible = false;

        var activePage = _activePage!;
        PageAnimator.PrepareHiddenInPlace(activePage.Items);
        TransitionPresentationVisible(true);
        await Task.WhenAll(
            PlayMenuTransitionAnimationAsync(),
            PlayMenuContentTranslationAnimationAsync(),
            PageAnimator.PlayFadeInAsync(activePage.Items, MenuTransitionDuration));
        TransitionPresentationVisible(false);
        PageAnimator.Reset(activePage.Items);

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
                ? PlayMenuContentTranslationAnimationAsync(showing: false)
                : Task.CompletedTask,
            _activePage is not null
                ? PageAnimator.PlayFadeOutAsync(_activePage.Items, MenuTransitionDuration)
                : Task.CompletedTask);

        Visibility = Visibility.Collapsed;
        TransitionPresentationVisible(false);

        IsHitTestVisible = true;
        _isTransitioning = false;
    }

    private async Task HandleMenuItemClickedAsync(TouchMenuItemDescriptor item, MenuButton button)
    {
        if (_isTransitioning)
            return;

        if (item.Kind == TouchMenuItemKind.Toggle)
            _toggleStates[item.Id] = button.IsOn;

        if (item.Kind == TouchMenuItemKind.Navigation && item.TargetPage is { } targetPage)
            await SwitchPageAsync(targetPage, item.Cell);
    }

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

            var button = new MenuButton
            {
                Glyph = item.Glyph,
                Text = item.Text,
                IsToggle = item.Kind == TouchMenuItemKind.Toggle,
                IsOn = _toggleStates.TryGetValue(item.Id, out var isOn) ? isOn : item.IsOn,
                IsEnabled = item.IsEnabled,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
            };

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
