using Microsoft.UI.Xaml;
using R3;
using R3.ObservableEvents;

namespace TouchChanX.WinUI;

public sealed partial class LockWindow : Window
{
    public Observable<Unit> FreezeRequested { get; }
    public Observable<Unit> UnlockRequested { get; }
    public Observable<Unit> CloseRequested { get; }

    public LockWindow()
    {
        InitializeComponent();

        FreezeRequested = FreezeButton.Events().Click.AsUnitObservable().Share();
        UnlockRequested = UnlockButton.Events().Click.AsUnitObservable().Share();
        CloseRequested = CloseWindowButton.Events().Click.AsUnitObservable().Share();
        SetFrozenState(false);
    }

    public void SetFrozenState(bool isFrozen)
    {
        FreezeButton.IsEnabled = !isFrozen;
        UnlockButton.IsEnabled = isFrozen;
    }
}
