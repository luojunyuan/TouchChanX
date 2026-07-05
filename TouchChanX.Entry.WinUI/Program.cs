WinRT.ComWrappersSupport.InitializeComWrappers();
Microsoft.UI.Xaml.Application.Start(p =>
{
    var context = new Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(
        Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
    SynchronizationContext.SetSynchronizationContext(context);
    _ = new TouchChanX.WinUI.App();
});
