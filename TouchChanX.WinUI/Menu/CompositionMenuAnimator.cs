using Microsoft.UI.Composition;
using Microsoft.UI.Xaml.Hosting;
using System.Numerics;
using TouchChanX.WinUI.Menu.Model;

namespace TouchChanX.WinUI.Menu;

internal sealed class CompositionMenuAnimator
{
    private readonly Compositor _compositor;
    private readonly CompositionEasingFunction _easing;

    public CompositionMenuAnimator(Compositor compositor)
    {
        _compositor = compositor;
        _easing = compositor.CreateCubicBezierEasingFunction(
            new Vector2(0.22f, 0.61f),
            new Vector2(0.36f, 1.0f));
    }

    public Task PlayEnterAsync(
        IReadOnlyList<MenuItemView> items,
        MenuCell origin,
        float cellDistance,
        TimeSpan duration) =>
        RunBatch(() =>
        {
            foreach (var item in items)
            {
                var offset = OffsetFromOrigin(item.Descriptor.Cell, origin, cellDistance);
                Animate(item.Element, 0f, 1f, offset, Vector3.Zero, duration);
            }
        });

    public Task PlayExitAsync(
        IReadOnlyList<MenuItemView> items,
        MenuCell origin,
        float cellDistance,
        TimeSpan duration) =>
        RunBatch(() =>
        {
            foreach (var item in items)
            {
                var offset = OffsetFromOrigin(item.Descriptor.Cell, origin, cellDistance);
                Animate(item.Element, 1f, 0f, Vector3.Zero, offset, duration);
            }
        });

    public Task PlaySwitchAsync(
        IReadOnlyList<MenuItemView> outgoingItems,
        IReadOnlyList<MenuItemView> incomingItems,
        MenuCell origin,
        float cellDistance,
        TimeSpan duration) =>
        RunBatch(() =>
        {
            foreach (var item in outgoingItems)
                Animate(item.Element, 1f, 0f, Vector3.Zero, Vector3.Zero, duration);

            foreach (var item in incomingItems)
            {
                var offset = OffsetFromOrigin(item.Descriptor.Cell, origin, cellDistance);
                Animate(item.Element, 0f, 1f, offset, Vector3.Zero, duration);
            }
        });

    public Task PlayFadeInAsync(
        IReadOnlyList<MenuItemView> items,
        TimeSpan duration) =>
        RunBatch(() =>
        {
            foreach (var item in items)
                Animate(item.Element, 0f, 1f, Vector3.Zero, Vector3.Zero, duration);
        });

    public Task PlayFadeOutAsync(
        IReadOnlyList<MenuItemView> items,
        TimeSpan duration) =>
        RunBatch(() =>
        {
            foreach (var item in items)
                Animate(item.Element, 1f, 0f, Vector3.Zero, Vector3.Zero, duration);
        });

    public void PrepareHidden(IReadOnlyList<MenuItemView> items, MenuCell origin, float cellDistance)
    {
        foreach (var item in items)
        {
            var visual = ElementCompositionPreview.GetElementVisual(item.Element);
            StopItemAnimations(visual);
            visual.Opacity = 0f;
            visual.Offset = OffsetFromOrigin(item.Descriptor.Cell, origin, cellDistance);
        }
    }

    public void PrepareHiddenInPlace(IReadOnlyList<MenuItemView> items)
    {
        foreach (var item in items)
        {
            var visual = ElementCompositionPreview.GetElementVisual(item.Element);
            StopItemAnimations(visual);
            visual.Opacity = 0f;
            visual.Offset = Vector3.Zero;
        }
    }

    public void Reset(IReadOnlyList<MenuItemView> items)
    {
        foreach (var item in items)
        {
            var visual = ElementCompositionPreview.GetElementVisual(item.Element);
            StopItemAnimations(visual);
            visual.Opacity = 1f;
            visual.Offset = Vector3.Zero;
        }
    }

    private Task RunBatch(Action startAnimations)
    {
        var taskCompletionSource = new TaskCompletionSource();
        var batch = _compositor.CreateScopedBatch(CompositionBatchTypes.Animation);

        startAnimations();

        batch.Completed += (_, _) => taskCompletionSource.TrySetResult();
        batch.End();

        return taskCompletionSource.Task;
    }

    private void Animate(
        Microsoft.UI.Xaml.UIElement element,
        float fromOpacity,
        float toOpacity,
        Vector3 fromOffset,
        Vector3 toOffset,
        TimeSpan duration)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        visual.Opacity = toOpacity;
        visual.Offset = toOffset;

        var opacityAnimation = _compositor.CreateScalarKeyFrameAnimation();
        opacityAnimation.Duration = duration;
        opacityAnimation.InsertKeyFrame(0f, fromOpacity);
        opacityAnimation.InsertKeyFrame(1f, toOpacity, _easing);

        var offsetAnimation = _compositor.CreateVector3KeyFrameAnimation();
        offsetAnimation.Duration = duration;
        offsetAnimation.InsertKeyFrame(0f, fromOffset);
        offsetAnimation.InsertKeyFrame(1f, toOffset, _easing);

        visual.StartAnimation(nameof(Visual.Opacity), opacityAnimation);
        visual.StartAnimation(nameof(Visual.Offset), offsetAnimation);
    }

    private static void StopItemAnimations(Visual visual)
    {
        visual.StopAnimation(nameof(Visual.Opacity));
        visual.StopAnimation(nameof(Visual.Offset));
    }

    private static Vector3 OffsetFromOrigin(MenuCell itemCell, MenuCell origin, float cellDistance) =>
        new(
            (origin.Column - itemCell.Column) * cellDistance,
            (origin.Row - itemCell.Row) * cellDistance,
            0f);
}
