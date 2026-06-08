using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using System.Numerics;
using TouchChanX.WinUI.Controls;
using Windows.Foundation;
using Windows.UI;

namespace TouchChanX.WinUI.Menu;

public partial class MenuControl
{
    private static readonly TimeSpan MenuTransitionDuration = TimeSpan.FromMilliseconds(360);
    private static readonly TimeSpan PageTransitionDuration = TimeSpan.FromMilliseconds(200);

    private Compositor? _compositor;
    private ShapeVisual? _menuBackgroundVisual;
    private CompositionRoundedRectangleGeometry? _menuBackgroundCornerShape;
    private CompositionEasingFunction? _menuEasing;
    private CompositionMenuAnimator? _pageAnimator;

    private Compositor Compositor =>
        _compositor ??= ElementCompositionPreview.GetElementVisual(this).Compositor;

    private ShapeVisual MenuBackgroundVisual =>
        _menuBackgroundVisual ??= Compositor.CreateShapeVisual();

    private CompositionRoundedRectangleGeometry MenuBackgroundCornerShape =>
        _menuBackgroundCornerShape ??= Compositor.CreateRoundedRectangleGeometry();

    private CompositionEasingFunction MenuEasing =>
        _menuEasing ??= Compositor.CreateCubicBezierEasingFunction(
            new Vector2(0.22f, 0.61f),
            new Vector2(0.36f, 1.0f));

    private CompositionMenuAnimator PageAnimator =>
        _pageAnimator ??= new CompositionMenuAnimator(Compositor);

    private readonly TouchGlyph TouchGlyph = new()
    {
        Width = Shared.TouchSize,
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Top,
    };

    private void InitializeCompositionVisuals()
    {
        MenuBackgroundCornerShape.CornerRadius = new Vector2((float)(Shared.TouchSize / 2));
        var backgroundSpriteShape = Compositor.CreateSpriteShape(MenuBackgroundCornerShape);
        backgroundSpriteShape.FillBrush = Compositor.CreateColorBrush(Color.FromArgb(0xFF, 0x2A, 0x2A, 0x2A));
        MenuBackgroundVisual.Shapes.Add(backgroundSpriteShape);

        ElementCompositionPreview.SetElementChildVisual(TransitionShellHost, MenuBackgroundVisual);
        TransitionItemsHost.Children.Add(TouchGlyph);
    }

    private TouchDockAnchor _lastTouchDockAnchor = TouchDockAnchor.Default;

    private Size ContainerSize => new(ActualWidth, ActualHeight);

    /// <summary>
    /// 窗口坐标系转为中央坐标系。
    /// </summary>
    private Point CenterPosition =>
        new((ContainerSize.Width - Shared.MenuSize) / 2, (ContainerSize.Height - Shared.MenuSize) / 2);

    private Task PlayMenuTransitionAnimationAsync(bool showing = true)
    {
        var taskCompletionSource = new TaskCompletionSource();
        var batch = Compositor.CreateScopedBatch(CompositionBatchTypes.Animation);

        var anchorOffset = AnchorPoint(_lastTouchDockAnchor, ContainerSize).ToVector3();
        var centerOffset = CenterPosition.ToVector3();
        var touchSize = new Vector2((float)Shared.TouchSize, (float)Shared.TouchSize);
        var menuSize = new Vector2((float)Shared.MenuSize, (float)Shared.MenuSize);

        var fromOffset = showing ? anchorOffset : centerOffset;
        var toOffset = showing ? centerOffset : anchorOffset;
        var fromSize = showing ? touchSize : menuSize;
        var toSize = showing ? menuSize : touchSize;
        var fakeTouchFromOpacity = showing ? 1f : 0f;
        var fakeTouchToOpacity = showing ? 0f : 1f;
        var touchVisual = ElementCompositionPreview.GetElementVisual(TouchGlyph);

        MenuBackgroundVisual.Offset = toOffset;
        MenuBackgroundVisual.Size = toSize;
        MenuBackgroundCornerShape.Size = toSize;
        touchVisual.Offset = toOffset;
        touchVisual.Opacity = fakeTouchToOpacity;

        var offsetAnimation = Compositor.CreateVector3KeyFrameAnimation();
        offsetAnimation.Duration = MenuTransitionDuration;
        offsetAnimation.InsertKeyFrame(0f, fromOffset);
        offsetAnimation.InsertKeyFrame(1f, toOffset, MenuEasing);

        var visualSizeAnimation = Compositor.CreateVector2KeyFrameAnimation();
        visualSizeAnimation.Duration = MenuTransitionDuration;
        visualSizeAnimation.InsertKeyFrame(0f, fromSize);
        visualSizeAnimation.InsertKeyFrame(1f, toSize, MenuEasing);

        var geometrySizeAnimation = Compositor.CreateVector2KeyFrameAnimation();
        geometrySizeAnimation.Duration = MenuTransitionDuration;
        geometrySizeAnimation.InsertKeyFrame(0f, fromSize);
        geometrySizeAnimation.InsertKeyFrame(1f, toSize, MenuEasing);

        var touchOffsetAnimation = Compositor.CreateVector3KeyFrameAnimation();
        touchOffsetAnimation.Duration = MenuTransitionDuration;
        touchOffsetAnimation.InsertKeyFrame(0f, fromOffset);
        touchOffsetAnimation.InsertKeyFrame(1f, toOffset, MenuEasing);

        var touchOpacityAnimation = Compositor.CreateScalarKeyFrameAnimation();
        touchOpacityAnimation.Duration = MenuTransitionDuration;
        touchOpacityAnimation.InsertKeyFrame(0f, fakeTouchFromOpacity);
        touchOpacityAnimation.InsertKeyFrame(1f, fakeTouchToOpacity, MenuEasing);

        MenuBackgroundVisual.StartAnimation(nameof(Visual.Offset), offsetAnimation);
        MenuBackgroundVisual.StartAnimation(nameof(Visual.Size), visualSizeAnimation);
        MenuBackgroundCornerShape.StartAnimation(nameof(CompositionRoundedRectangleGeometry.Size), geometrySizeAnimation);
        touchVisual.StartAnimation(nameof(Visual.Offset), touchOffsetAnimation);
        touchVisual.StartAnimation(nameof(Visual.Opacity), touchOpacityAnimation);

        batch.Completed += (_, _) => taskCompletionSource.TrySetResult();
        batch.End();

        return taskCompletionSource.Task;
    }

    /// <summary>
    /// 把 TouchDockAnchor 翻译到所位于的窗口坐标系位置。
    /// </summary>
    private static Point AnchorPoint(TouchDockAnchor anchor, Size window)
    {
        var width = window.Width;
        var height = window.Height;
        var alignRight = width - Shared.TouchSize - Shared.TouchSpacing;
        var alignBottom = height - Shared.TouchSize - Shared.TouchSpacing;

        return anchor switch
        {
            TouchDockAnchor.TopLeft => new Point(Shared.TouchSpacing, Shared.TouchSpacing),
            TouchDockAnchor.TopRight => new Point(alignRight, Shared.TouchSpacing),
            TouchDockAnchor.BottomLeft => new Point(Shared.TouchSpacing, alignBottom),
            TouchDockAnchor.BottomRight => new Point(alignRight, alignBottom),
            TouchDockAnchor.Left x => new Point(Shared.TouchSpacing, x.Scale * height - Shared.TouchSize / 2 - Shared.TouchSpacing),
            TouchDockAnchor.Top x => new Point(x.Scale * width - Shared.TouchSize / 2 - Shared.TouchSpacing, Shared.TouchSpacing),
            TouchDockAnchor.Right x => new Point(alignRight, x.Scale * height - Shared.TouchSize / 2 - Shared.TouchSpacing),
            TouchDockAnchor.Bottom x => new Point(x.Scale * width - Shared.TouchSize / 2 - Shared.TouchSpacing, alignBottom),
            _ => default,
        };
    }
}

internal static class MenuCompositionExtensions
{
    public static Vector3 ToVector3(this Point point) =>
        new((float)point.X, (float)point.Y, 0f);
}
