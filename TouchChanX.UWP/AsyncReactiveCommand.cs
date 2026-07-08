using System.Diagnostics;
using System.Windows.Input;
using R3;

namespace TouchChanX.UWP;

public sealed partial class AsyncReactiveCommand : ICommand, IDisposable
{
    private readonly Func<CancellationToken, ValueTask> _executeAsync;
    private readonly IDisposable? _canExecuteSubscription;
    private readonly CancellationTokenSource _disposeCancellation = new();
    private bool _canExecute;

    public AsyncReactiveCommand(
        Func<CancellationToken, ValueTask> executeAsync,
        Observable<bool>? canExecute = null,
        bool initialCanExecute = true)
    {
        _executeAsync = executeAsync;
        _canExecute = initialCanExecute;
        _canExecuteSubscription = canExecute?.Subscribe(ChangeCanExecute);
    }

    public ReactiveProperty<bool> IsExecuting { get; } = new(false);

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => _canExecute && !IsExecuting.Value;

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
            return;

        IsExecuting.Value = true;
        RaiseCanExecuteChanged();

        try
        {
            await _executeAsync(_disposeCancellation.Token);
        }
        catch (OperationCanceledException) when (_disposeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
        finally
        {
            IsExecuting.Value = false;
            RaiseCanExecuteChanged();
        }
    }

    public void Dispose()
    {
        _disposeCancellation.Cancel();
        _canExecuteSubscription?.Dispose();
        IsExecuting.Dispose();
        _disposeCancellation.Dispose();
    }

    private void ChangeCanExecute(bool canExecute)
    {
        if (_canExecute == canExecute)
            return;

        _canExecute = canExecute;
        RaiseCanExecuteChanged();
    }

    private void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public static class R3CommandExtensions
{
    /// <summary>
    /// Pipes each element of the source observable into a plain <see cref="ICommand"/> after checking <c>CanExecute</c>.
    /// </summary>
    public static IDisposable InvokeCommand<T>(this Observable<T> source, ICommand command)
    {
        return source
            .Where(command, static (v, cmd) => cmd.CanExecute(v))
            .Subscribe(command, static (v, cmd) => cmd.Execute(v));
    }
}