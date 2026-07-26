using R3;
using TouchChanX.Win32.Interop;
using TouchChanX.Win32.Menu;

namespace TouchChanX.Win32.Gamepad;

public readonly record struct GamepadMapping(string Button, string Key);

public sealed class GamepadController : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(33);

    private static readonly IReadOnlyDictionary<GamepadButtonFlags, VirtualKeyCode> ActionMapping =
        new Dictionary<GamepadButtonFlags, VirtualKeyCode>
        {
            [GamepadButtonFlags.A] = VirtualKeyCode.Enter,
            [GamepadButtonFlags.B] = VirtualKeyCode.Space,
            [GamepadButtonFlags.X] = VirtualKeyCode.None,
            [GamepadButtonFlags.Y] = VirtualKeyCode.None,
            [GamepadButtonFlags.DPadLeft] = VirtualKeyCode.Left,
            [GamepadButtonFlags.DPadUp] = VirtualKeyCode.Up,
            [GamepadButtonFlags.DPadRight] = VirtualKeyCode.Right,
            [GamepadButtonFlags.DPadDown] = VirtualKeyCode.Down,
            [GamepadButtonFlags.LeftShoulder] = VirtualKeyCode.None,
            [GamepadButtonFlags.RightShoulder] = VirtualKeyCode.Control,
            [GamepadButtonFlags.Start] = VirtualKeyCode.None,
            [GamepadButtonFlags.Back] = VirtualKeyCode.None,
            [GamepadButtonFlags.LeftThumb] = VirtualKeyCode.None,
            [GamepadButtonFlags.RightThumb] = VirtualKeyCode.None,
        };

    public static IReadOnlyList<GamepadMapping> Mappings { get; } =
    [
        new("D-pad Left", "Left"),
        new("D-pad Up", "Up"),
        new("D-pad Right", "Right"),
        new("D-pad Down", "Down"),
        new("A", "Enter"),
        new("B", "Space"),
        new("X", "-"),
        new("Y", "-"),
        new("LB", "-"),
        new("RB", "Ctrl"),
        new("Start", "-"),
        new("Back", "Show mapping"),
        new("Left stick", "-"),
        new("Right stick", "-"),
    ];

    private readonly nint _gameWindowHandle;
    private readonly SerialDisposable _monitoring = new();
    private readonly Subject<Unit> _mappingRequested = new();
    private readonly Subject<bool> _availabilityChanged = new();
    private readonly HashSet<VirtualKeyCode> _pressedKeys = [];
    private bool _hasPreviousState;
    private bool _hasConnectedController;
    private bool _isEnabled;
    private bool _isDisposed;
    private uint _controllerIndex;
    private GamepadButtonFlags _previousButtons;

    public GamepadController(nint gameWindowHandle)
    {
        _gameWindowHandle = gameWindowHandle;

        if (IsSupported)
            UpdateAvailability(XInputNative.TryGetFirstConnected(out _, out _));
    }

    public static bool IsSupported => XInputNative.IsSupported;

    public static bool IsAvailable()
    {
        if (!XInputNative.IsSupported)
            return false;

        return XInputNative.TryGetFirstConnected(out _, out _);
    }

    public bool IsEnabled => _isEnabled;

    public bool HasConnectedController => _hasConnectedController;

    public Observable<Unit> ObservableMappingRequested => _mappingRequested;

    public Observable<bool> ObservableAvailabilityChanged => _availabilityChanged;

    public void SetEnabled(bool isEnabled)
    {
        if (_isDisposed || _isEnabled == isEnabled)
            return;

        if (isEnabled && (!IsSupported || !HasConnectedController))
            return;

        _isEnabled = isEnabled;
        _monitoring.Disposable = Disposable.Empty;
        ResetState();

        if (!isEnabled)
            return;

        Poll();
        _monitoring.Disposable = Observable.Interval(PollInterval).Subscribe(_ => Poll());
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        _isEnabled = false;
        _monitoring.Dispose();
        ResetState();
        _mappingRequested.Dispose();
        _availabilityChanged.Dispose();
    }

    private void Poll()
    {
        if (!_isEnabled || _isDisposed)
            return;

        if (!XInputNative.TryGetFirstConnected(out var controllerIndex, out var state))
        {
            UpdateAvailability(false);
            ResetState();
            return;
        }

        UpdateAvailability(true);

        var buttons = state.Gamepad.Buttons;
        if (!_hasPreviousState || _controllerIndex != controllerIndex)
        {
            ReleasePressedKeys();
            _controllerIndex = controllerIndex;
            _previousButtons = buttons;
            _hasPreviousState = true;
            return;
        }

        foreach (var button in ActionMapping.Keys)
        {
            var isDown = buttons.HasFlag(button);
            var wasDown = _previousButtons.HasFlag(button);
            if (isDown && !wasDown)
                HandleButtonDown(button);
            else if (!isDown && wasDown)
                HandleButtonUp(button);
        }

        _previousButtons = buttons;
    }

    private void HandleButtonDown(GamepadButtonFlags button)
    {
        if (!ActionMapping.TryGetValue(button, out var key) ||
            key == VirtualKeyCode.None ||
            !OsPlatformApi.IsForegroundWindow(_gameWindowHandle))
        {
            return;
        }

        InputSimulator.KeyDown(key);
        _pressedKeys.Add(key);
    }

    private void HandleButtonUp(GamepadButtonFlags button)
    {
        if (button == GamepadButtonFlags.Back)
        {
            _mappingRequested.OnNext(Unit.Default);
            return;
        }

        if (!ActionMapping.TryGetValue(button, out var key) ||
            key == VirtualKeyCode.None ||
            !_pressedKeys.Remove(key))
        {
            return;
        }

        InputSimulator.KeyUp(key);
    }

    private void ResetState()
    {
        ReleasePressedKeys();
        _hasPreviousState = false;
        _controllerIndex = 0;
        _previousButtons = default;
    }

    private void UpdateAvailability(bool isAvailable)
    {
        if (_hasConnectedController == isAvailable)
            return;

        _hasConnectedController = isAvailable;
        _availabilityChanged.OnNext(isAvailable);
    }

    private void ReleasePressedKeys()
    {
        foreach (var key in _pressedKeys)
            InputSimulator.KeyUp(key);

        _pressedKeys.Clear();
    }
}
