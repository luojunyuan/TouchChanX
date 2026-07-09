using CommunityToolkit.WinUI;
using CommunityToolkit.WinUI.Animations;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using R3;
using R3.ObservableEvents;
using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using TouchChanX.Persistence;
using TouchDockAnchor = TouchChanX.WinUI.Menu.TouchDockAnchor;
using Windows.Foundation;

namespace TouchChanX.WinUI.Touch;

public partial class TouchControl
{
    public static Observable<Unit> ObservableRegionResetRequested { get; private set; } = Observable.Empty<Unit>();

    public static Observable<Rect> ObservableTouchRegionChanged { get; private set; } = Observable.Empty<Rect>();
}

public sealed partial class TouchControl : UserControl
{
    private static readonly TimeSpan ReleaseToEdgeDuration = TimeSpan.FromMilliseconds(200);
    private readonly AppSettings _settings = new();

    public Observable<Rect> Clicked { get; }

    private Size ContainerSize => new(ActualWidth, ActualHeight);

    private Rect TouchRect => new(
        TouchBorder.Translation.X,
        TouchBorder.Translation.Y,
        TouchBorder.ActualWidth > 0 ? TouchBorder.ActualWidth : Shared.TouchSize,
        TouchBorder.ActualHeight > 0 ? TouchBorder.ActualHeight : Shared.TouchSize);

    public Rect CurrentRect => TouchRect;

    public TouchControl()
    {
        InitializeComponent();
        TouchBorder.Translation = new(Shared.TouchSpacing, Shared.TouchSpacing, 0);
        var initialDockAnchor = LoadTouchDockAnchor(_settings);

        var pressed = TouchBorder.Events().PointerPressed.Share();
        var dragStarted = TouchBorder.Events().ManipulationStarted.Share();
        var draggingStream = TouchBorder.Events().ManipulationDelta.Share();
        var dragEnded = TouchBorder.Events().ManipulationCompleted.Share();
        var containerSizeChanged = this.Events().SizeChanged.Share();
        var visibled = this.IsVisibleChanged.Where(visible => visible).AsUnitObservable().Share();
        var touchDocked = new Subject<Unit>();
        var touchPositionRestored = new Subject<Unit>();
        var initialPositionRestored = false;

        this.Events().Loaded.Subscribe(_ => TryRestoreInitialPosition());

        // 订阅拖动事件，更新位置
        draggingStream
            .Select(item => item.Delta.Translation)
            .Subscribe(delta =>
                TouchBorder.Translation += delta.ToVector3());

        // 订阅边界检查事件，超出边界则结束拖动
        draggingStream
            .Where(item => PositionCalculator.IsBeyondBoundary(
                ContainerSize, TouchRect))
            .Subscribe(e => e.Complete());

        // 订阅拖动结束事件，执行停靠动画
        dragEnded
            .Select(_ => PositionCalculator.CalculateTouchDockedPosition(
                ContainerSize, TouchRect, Shared.TouchSpacing))
            .SubscribeAwait(async (finalPos, _) =>
            {
                var startOffset = new Point(TouchBorder.Translation.X - finalPos.X, TouchBorder.Translation.Y - finalPos.Y);
                TouchBorder.Translation = finalPos.ToVector3();

                this.IsHitTestVisible = false;
                await AnimationBuilder.Create()
                    .Translation(from: startOffset.ToVector2(), to: Vector2.Zero, duration: ReleaseToEdgeDuration)
                    .StartAsync(TouchBorder, CancellationToken.None);
                this.IsHitTestVisible = true;

                SaveCurrentDockAnchor();
                touchDocked.OnNext(Unit.Default);
            });

        // 订阅容器大小变化事件，动态调整触控位置以保持相对位置不变
        containerSizeChanged
            .Subscribe(sizeEvent =>
            {
                if (!initialPositionRestored)
                {
                    TryRestoreInitialPosition();
                    return;
                }

                var rect = PositionCalculator.CalculateNewDockedPosition(
                    sizeEvent.PreviousSize,
                    TouchRect,
                    sizeEvent.NewSize,
                    Shared.TouchSpacing);
                TouchBorder.Translation = new Point(rect.X, rect.Y).ToVector3();
            });

        // 订阅透明度VSM状态变化事件
        this.Events().Loaded.AsUnitObservable()
            .Merge(visibled)
            .Merge(touchDocked)
            .Merge(touchPositionRestored)
            .Subscribe(_ => VisualStateManager.GoToState(this, "Faded", true));
        pressed
            .Subscribe(_ => VisualStateManager.GoToState(this, "Normal", true));

        // 定义对外暴露的 Clicked 流
        Clicked =
            pressed
            .Select(_ =>
                TouchBorder.Events().Tapped
                .TakeUntil(dragStarted))
            .Switch() // 处理拖动取消流和反复点击流
            .Select(_ => TouchRect)
            .Share();

        ObservableRegionResetRequested = pressed.AsUnitObservable();
        ObservableTouchRegionChanged =
            Observable.Merge(
                containerSizeChanged.AsUnitObservable(),
                touchDocked,
                visibled,
                touchPositionRestored)
            .Select(_ => TouchRect);

        void TryRestoreInitialPosition()
        {
            if (initialPositionRestored || !IsContainerReady())
                return;

            ApplyDockAnchor(initialDockAnchor);
            initialPositionRestored = true;
            touchPositionRestored.OnNext(Unit.Default);
        }
    }

    private void ApplyDockAnchor(TouchDockAnchor anchor)
    {
        var touchSize = new Size(Shared.TouchSize, Shared.TouchSize);
        var point = TouchDockAnchor.ToTouchPosition(anchor, ContainerSize, touchSize, Shared.TouchSpacing);
        TouchBorder.Translation = point.ToVector3();
    }

    private void SaveCurrentDockAnchor()
    {
        var anchor = TouchDockAnchor.SnapFromRect(ContainerSize, TouchRect);
        _settings.TouchDockAnchor = JsonSerializer.Serialize(anchor, TouchDockAnchorJsonContext.Default.TouchDockAnchor);
    }

    private static TouchDockAnchor LoadTouchDockAnchor(AppSettings settings) =>
        DeserializeTouchDockAnchor(settings.TouchDockAnchor) ?? TouchDockAnchor.Default;

    private static TouchDockAnchor? DeserializeTouchDockAnchor(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        try
        {
            return JsonSerializer.Deserialize(value, TouchDockAnchorJsonContext.Default.TouchDockAnchor);
        }
        catch (JsonException ex)
        {
            Debug.WriteLine(ex);
            return null;
        }
    }

    private bool IsContainerReady() =>
        ActualWidth >= Shared.TouchSize + Shared.TouchSpacing * 2 &&
        ActualHeight >= Shared.TouchSize + Shared.TouchSpacing * 2;
}

[JsonSerializable(typeof(TouchDockAnchor))]
[JsonSerializable(typeof(TouchDockAnchor.Left))]
[JsonSerializable(typeof(TouchDockAnchor.Top))]
[JsonSerializable(typeof(TouchDockAnchor.Right))]
[JsonSerializable(typeof(TouchDockAnchor.Bottom))]
[JsonSerializable(typeof(TouchDockAnchor.TopLeft))]
[JsonSerializable(typeof(TouchDockAnchor.TopRight))]
[JsonSerializable(typeof(TouchDockAnchor.BottomLeft))]
[JsonSerializable(typeof(TouchDockAnchor.BottomRight))]
internal sealed partial class TouchDockAnchorJsonContext : JsonSerializerContext;
