using R3;
using System.Diagnostics;
using TouchChanX.Win32;
using TouchChanX.Win32.Battery;
using TouchChanX.Win32.Interop;

namespace TouchChanX;

/// <summary>
/// Owns the process-scoped WinUI lifetime and rotates game-window sessions inside it.
/// </summary>
internal sealed partial class WinUIAppController(Process process)
{
    private readonly Process _process = process;
    private bool _isFirstGameWindow = true;
    private IDisposable? _windowLoopSubscription;

    public void Start()
    {
        InitializeReactiveRuntime();
        WinUI.Menu.MenuControl.IsBatteryFeatureAvailable = BatteryMonitor.IsAvailable();
        _windowLoopSubscription = CreateWindowLoop()
            // A window can disappear during startup and complete synchronously.
            // Trampoline keeps the next lookup from growing the call stack recursively.
            .Trampoline()
            .Subscribe(
                static _ => { },
                HandleWindowLoopResult);
    }

    private Observable<Unit> CreateWindowLoop() =>
        Observable.Defer(CreateWindowSession);

    private Observable<Unit> CreateWindowSession()
    {
        try
        {
            return CreateWindowSessionCore();
        }
        catch (Exception ex)
        {
            return Observable.Create<Unit>(observer =>
            {
                observer.OnCompleted(Result.Failure(ex));
                return Disposable.Empty;
            });
        }
    }

    private Observable<Unit> CreateWindowSessionCore()
    {
        if (_process.HasExited)
            return Observable.Empty<Unit>();

        var handleResult = GameStartup.FindGoodWindowHandle(_process);
        if (handleResult.IsFailure(out var error, out var gameWindowHandle))
        {
            if (error is WindowHandleNotFoundError)
                OsPlatformApi.MessageBox.Show("Timeout! Failed to find a valid window of game");

            return Observable.Empty<Unit>();
        }

        if (GameStartup.HasAttachedCurrentTouchChanX(gameWindowHandle))
            return Observable.Empty<Unit>();

        var windowLifetime = new GameWindowLifetime(
            _process,
            gameWindowHandle,
            isFirstGameWindow: _isFirstGameWindow);

        return Observable.Create<Unit>(observer =>
        {
            IDisposable? lifetimeSubscription = null;
            try
            {
                lifetimeSubscription = windowLifetime.Completed.Subscribe(
                    _ =>
                    {
                        _isFirstGameWindow = false;
                        observer.OnNext(Unit.Default);
                    },
                    observer.OnCompleted);

                windowLifetime.Start();
            }
            catch (Exception ex)
            {
                lifetimeSubscription?.Dispose();
                windowLifetime.Dispose();
                observer.OnCompleted(Result.Failure(ex));
                return Disposable.Empty;
            }

            return Disposable.Create(() =>
            {
                lifetimeSubscription?.Dispose();
                windowLifetime.Dispose();
            });
        }).Concat(Observable.Defer(CreateWindowLoop));
    }

    private void HandleWindowLoopResult(Result result)
    {
        if (result.IsFailure)
        {
            Debug.WriteLine($"WinUI game window loop failed: {result.Exception}");
        }

        _windowLoopSubscription = null;
        WinUIApplication.SignalStartupCompleted();
        Environment.Exit(0);
    }

    private static void InitializeReactiveRuntime()
    {
        ObservableSystem.RegisterUnhandledExceptionHandler(ex => Debug.WriteLine(ex.ToString()));
        ObservableSystem.DefaultTimeProvider = WinUI3DispatcherTimeProvider.Default;
    }
}
