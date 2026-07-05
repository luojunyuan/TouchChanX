TouchChanX.WinUI.TouchChanXSettings.ConfigureStorage(
    key => EntrySettings.Values.TryGetValue(key, out var value) ? value : null,
    (key, value) => EntrySettings.Values[key] = value);

WinRT.ComWrappersSupport.InitializeComWrappers();
Microsoft.UI.Xaml.Application.Start(p =>
{
    var context = new Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(
        Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
    SynchronizationContext.SetSynchronizationContext(context);
    _ = new TouchChanX.WinUI.App();
});

internal static class EntrySettings
{
    public static Dictionary<string, object> Values { get; } = [];
}
