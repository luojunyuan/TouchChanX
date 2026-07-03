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

        // 启用 MenuBorder 的 Translation 属性：
        // Translation 叠加在 XAML layout Offset 之上，不受 XAML layout 覆写，
        // 是对 XAML 元素做位移动画的正确方式
        ElementCompositionPreview.SetIsTranslationEnabled(MenuBorder, true);
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

        var shellFromOffset = showing ? anchorOffset : centerOffset;
        var shellToOffset   = showing ? centerOffset : anchorOffset;
        var fromSize = showing ? touchSize : menuSize;
        var toSize   = showing ? menuSize  : touchSize;
        var fakeTouchFromOpacity = showing ? 1f : 0f;
        var fakeTouchToOpacity   = showing ? 0f : 1f;
        var touchVisual = ElementCompositionPreview.GetElementVisual(TouchGlyph);

        // TouchGlyph 在动画结束时位于菜单正中心，而不是左上角
        var touchCenterInMenu = new Vector3(
            (float)(Shared.MenuSize - Shared.TouchSize) / 2,
            (float)(Shared.MenuSize - Shared.TouchSize) / 2,
            0f);
        var touchFromOffset = showing ? anchorOffset : centerOffset + touchCenterInMenu;
        var touchToOffset   = showing ? centerOffset + touchCenterInMenu : anchorOffset;

        MenuBackgroundVisual.Offset = shellToOffset;
        MenuBackgroundVisual.Size = toSize;
        MenuBackgroundCornerShape.Size = toSize;
        touchVisual.Offset  = touchToOffset;
        touchVisual.Opacity = fakeTouchToOpacity;

        var offsetAnimation = Compositor.CreateVector3KeyFrameAnimation();
        offsetAnimation.Duration = MenuTransitionDuration;
        offsetAnimation.InsertKeyFrame(0f, shellFromOffset);
        offsetAnimation.InsertKeyFrame(1f, shellToOffset, MenuEasing);

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
        touchOffsetAnimation.InsertKeyFrame(0f, touchFromOffset);
        touchOffsetAnimation.InsertKeyFrame(1f, touchToOffset, MenuEasing);

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
    /// 同步缩放并平移 MenuBorder，使菜单内容像嵌入壳里一样随壳整体运动。
    /// 位移使用 Translation（叠加在 XAML layout Offset 之上），不受 XAML layout 覆写。
    /// </summary>
    private Task PlayMenuContentScaleTranslationAnimationAsync(bool showing = true)
    {
        var taskCompletionSource = new TaskCompletionSource();
        var batch = Compositor.CreateScopedBatch(CompositionBatchTypes.Animation);

        var anchorOffset = AnchorPoint(_lastTouchDockAnchor, ContainerSize).ToVector3();
        var centerOffset = CenterPosition.ToVector3();

        // Translation 叠加在 XAML layout Offset (= centerOffset) 之上，不受 XAML layout 覆写。
        // 期望起始有效位置 = startOffset = anchorOffset + (TouchSize/2 - MenuSize/2)
        // => 起始 Translation = startOffset - centerOffset
        var halfDelta = (float)(Shared.TouchSize - Shared.MenuSize) / 2;
        var startTranslation = anchorOffset + new Vector3(halfDelta, halfDelta, 0f) - centerOffset;

        var fromTranslation = showing ? startTranslation : Vector3.Zero;
        var toTranslation   = showing ? Vector3.Zero : startTranslation;

        var scaleRatio = (float)(Shared.TouchSize / Shared.MenuSize);
        var fromScale  = new Vector3(showing ? scaleRatio : 1f, showing ? scaleRatio : 1f, 1f);
        var toScale    = new Vector3(showing ? 1f : scaleRatio, showing ? 1f : scaleRatio, 1f);

        // 透明度与 TouchGlyph 反向：展开时 0→1，收起时 1→0，参数保持一致
        var fromOpacity = showing ? 0f : 1f;
        var toOpacity   = showing ? 1f : 0f;

        var visual = ElementCompositionPreview.GetElementVisual(MenuBorder);
        visual.StopAnimation("Translation");
        visual.StopAnimation(nameof(Visual.Scale));
        visual.StopAnimation(nameof(Visual.Opacity));
        // 缩放锚点设在 MenuBorder 自身中心
        visual.CenterPoint = new Vector3((float)Shared.MenuSize / 2, (float)Shared.MenuSize / 2, 0f);
        // 静态预设为 FROM 值：Translation 不受 XAML 覆写，确保任何间隙帧显示正确起始状态
        visual.Properties.InsertVector3("Translation", fromTranslation);
        visual.Scale   = fromScale;
        visual.Opacity = fromOpacity;

        var translationAnimation = Compositor.CreateVector3KeyFrameAnimation();
        translationAnimation.Duration = MenuTransitionDuration;
        translationAnimation.InsertKeyFrame(0f, fromTranslation);
        translationAnimation.InsertKeyFrame(1f, toTranslation, MenuEasing);

        var scaleAnimation = Compositor.CreateVector3KeyFrameAnimation();
        scaleAnimation.Duration = MenuTransitionDuration;
        scaleAnimation.InsertKeyFrame(0f, fromScale);
        scaleAnimation.InsertKeyFrame(1f, toScale, MenuEasing);

        var opacityAnimation = Compositor.CreateScalarKeyFrameAnimation();
        opacityAnimation.Duration = MenuTransitionDuration;
        opacityAnimation.InsertKeyFrame(0f, fromOpacity);
        opacityAnimation.InsertKeyFrame(1f, toOpacity, MenuEasing);

        visual.StartAnimation("Translation", translationAnimation);
        visual.StartAnimation(nameof(Visual.Scale), scaleAnimation);
        visual.StartAnimation(nameof(Visual.Opacity), opacityAnimation);

        batch.Completed += (_, _) => taskCompletionSource.TrySetResult();
        batch.End();

        return taskCompletionSource.Task;
    }

    /// <summary>
    /// 动画结束后重置 MenuBorder 的 Composition 变换，还给 XAML 布局管理，
    /// 同时确保窗口缩放时 MenuBorder 位置能跟随 XAML 布局重算。
    /// </summary>
    private void ResetMenuContentVisual()
    {
        var visual = ElementCompositionPreview.GetElementVisual(MenuBorder);
        visual.StopAnimation("Translation");
        visual.StopAnimation(nameof(Visual.Scale));
        visual.StopAnimation(nameof(Visual.Opacity));
        visual.Properties.InsertVector3("Translation", Vector3.Zero);
        visual.Scale   = Vector3.One;
        visual.Opacity = 1f;
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
