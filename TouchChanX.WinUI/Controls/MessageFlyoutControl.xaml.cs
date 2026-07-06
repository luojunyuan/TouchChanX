using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using R3;
using System.Numerics;

namespace TouchChanX.WinUI.Controls;

public sealed partial class MessageFlyoutControl : UserControl
{
    private static readonly TimeSpan DisplayDuration = TimeSpan.FromSeconds(1.6);
    private static readonly TimeSpan ShowDuration = TimeSpan.FromMilliseconds(280);
    private static readonly TimeSpan HideDuration = TimeSpan.FromMilliseconds(220);
    private static readonly Vector3 VisibleTranslation = new(0f, 0f, 32f);
    private static readonly Vector3 HiddenTranslation = new(0f, -126f, 32f);

    private readonly Subject<MessageFlyoutRequest> _messageRequested = new();
    private Compositor? _compositor;
    private CompositionEasingFunction? _showEasing;
    private CompositionEasingFunction? _hideEasing;
    private int _messageVersion;
    private bool _hasStartedAnimations;
    private FlyoutPresentationState _presentationState = FlyoutPresentationState.Hidden;

    private Compositor Compositor =>
        _compositor ??= ElementCompositionPreview.GetElementVisual(FlyoutBorder).Compositor;

    private CompositionEasingFunction ShowEasing =>
        _showEasing ??= Compositor.CreateCubicBezierEasingFunction(
            new Vector2(0.16f, 1.0f),
            new Vector2(0.3f, 1.0f));

    private CompositionEasingFunction HideEasing =>
        _hideEasing ??= Compositor.CreateCubicBezierEasingFunction(
            new Vector2(0.7f, 0.0f),
            new Vector2(0.84f, 0.0f));

    public MessageFlyoutControl()
    {
        InitializeComponent();

        ElementCompositionPreview.SetIsTranslationEnabled(FlyoutBorder, true);

        var visual = ElementCompositionPreview.GetElementVisual(FlyoutBorder);
        visual.Properties.InsertVector3("Translation", HiddenTranslation);
        visual.Opacity = 0f;

        _messageRequested
            .Subscribe(ShowMessageCore);

        _messageRequested
            .Debounce(DisplayDuration)
            .SubscribeAwait(async (request, _) => await HideMessageAsync(request.Version));
    }

    public void ShowMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        _messageRequested.OnNext(new(message, ++_messageVersion));
    }

    private void ShowMessageCore(MessageFlyoutRequest request)
    {
        MessageText.Text = request.Message;
        if (_presentationState is FlyoutPresentationState.Hidden or FlyoutPresentationState.Hiding)
        {
            _ = PlayShowAnimationAsync();
            return;
        }
    }

    private async Task PlayShowAnimationAsync()
    {
        _presentationState = FlyoutPresentationState.Showing;

        await PlayTransitionAsync(
            HiddenTranslation,
            VisibleTranslation,
            fromOpacity: 0f,
            toOpacity: 1f,
            ShowDuration,
            ShowEasing);

        if (_presentationState == FlyoutPresentationState.Showing)
            _presentationState = FlyoutPresentationState.Visible;
    }

    private async Task HideMessageAsync(int version)
    {
        if (version != _messageVersion || _presentationState == FlyoutPresentationState.Hidden)
            return;

        _presentationState = FlyoutPresentationState.Hiding;

        await PlayTransitionAsync(
            VisibleTranslation,
            HiddenTranslation,
            fromOpacity: 1f,
            toOpacity: 0f,
            HideDuration,
            HideEasing);

        if (version != _messageVersion)
            return;

        _presentationState = FlyoutPresentationState.Hidden;
    }

    private Task PlayTransitionAsync(
        Vector3 fromTranslation,
        Vector3 toTranslation,
        float fromOpacity,
        float toOpacity,
        TimeSpan duration,
        CompositionEasingFunction easing)
    {
        var taskCompletionSource = new TaskCompletionSource();
        var visual = ElementCompositionPreview.GetElementVisual(FlyoutBorder);
        var batch = Compositor.CreateScopedBatch(CompositionBatchTypes.Animation);

        if (_hasStartedAnimations)
        {
            visual.StopAnimation("Translation");
            visual.StopAnimation(nameof(Visual.Opacity));
        }

        visual.Properties.InsertVector3("Translation", fromTranslation);
        visual.Opacity = fromOpacity;

        var translationAnimation = Compositor.CreateVector3KeyFrameAnimation();
        translationAnimation.Duration = duration;
        translationAnimation.InsertKeyFrame(0f, fromTranslation);
        translationAnimation.InsertKeyFrame(1f, toTranslation, easing);

        var opacityAnimation = Compositor.CreateScalarKeyFrameAnimation();
        opacityAnimation.Duration = duration;
        opacityAnimation.InsertKeyFrame(0f, fromOpacity);
        opacityAnimation.InsertKeyFrame(1f, toOpacity, easing);

        visual.StartAnimation("Translation", translationAnimation);
        visual.StartAnimation(nameof(Visual.Opacity), opacityAnimation);
        _hasStartedAnimations = true;

        batch.Completed += (_, _) => taskCompletionSource.TrySetResult();
        batch.End();

        return taskCompletionSource.Task;
    }

    private sealed record MessageFlyoutRequest(string Message, int Version);

    private enum FlyoutPresentationState
    {
        Hidden,
        Showing,
        Visible,
        Hiding,
    }
}
