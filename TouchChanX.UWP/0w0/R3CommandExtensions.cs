using R3;
using System.Windows.Input;

namespace TouchChanX.UWP;

public static class R3CommandExtensions
{
    public static IDisposable InvokeCommand<T>(this Observable<T> source, ICommand command)
    {
        return source
            .Where(command, static (v, cmd) => cmd.CanExecute(v))
            .Subscribe(command, static (v, cmd) => cmd.Execute(v));
    }
}
public static class ReactiveCommandFactory
{
    public static ReactiveCommand<T> CreateStatusReactiveCommand<T>(
        Func<T, CancellationToken, ValueTask> executeAsync)
    {
        var canExecute = new ReactiveProperty<bool>(true);

        return canExecute.ToReactiveCommand<T>(
            async (value, cancellationToken) =>
            {
                canExecute.Value = false;

                try
                {
                    await executeAsync(value, cancellationToken);
                }
                finally
                {
                    canExecute.Value = true;
                }
            },
            initialCanExecute: canExecute.Value,
            awaitOperation: AwaitOperation.Drop);
    }
}
