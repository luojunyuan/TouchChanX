using R3;
using R3.ObservableEvents;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Windows.ApplicationModel.Core;
using Windows.Services.Store;
using Windows.System;
using Windows.UI.Xaml.Controls;
using WinRT;
using WinRT.Interop;

namespace TouchChanX.UWP;

public sealed partial class AboutPage : Page
{
    private const string OpenSourceUrl = "https://github.com/luojunyuan/TachiChanX";

    public AboutPage()
    {
        InitializeComponent();
        BindReactiveInteractions();
    }

    private StoreContext StoreContext => field ??= new Func<StoreContext>(() =>
    {
        var coreWindow = CoreApplication.GetCurrentView().CoreWindow;
        coreWindow.As<ICoreWindowInterop>().GetWindowHandle(out nint hwnd);

        var storeContext = StoreContext.GetDefault();
        InitializeWithWindow.Initialize(storeContext, hwnd);

        return storeContext;
    })();

    private void BindReactiveInteractions()
    {
        RateInStoreCard.Events().Click
            .SubscribeAwait(async (_, _) => await RateInStoreAsync());

        OpenSourceCard.Events().Click
            .SubscribeAwait(async (_, _) => await Launcher.LaunchUriAsync(new Uri(OpenSourceUrl)));
    }

    private async Task RateInStoreAsync()
    {
        var result = await StoreContext.RequestRateAndReviewAppAsync();

        if (result.ExtendedError is not null)
        {
            var pfn = Windows.ApplicationModel.Package.Current.Id.FamilyName;
            var storeUri = new Uri($"ms-windows-store://review/?PFN={pfn}");
            await Launcher.LaunchUriAsync(storeUri);
        }
    }
}

[GeneratedComInterface, Guid("45D64A29-A63E-4CB6-B498-5781D298CB4F")]
partial interface ICoreWindowInterop
{
    [PreserveSig]
    int GetWindowHandle(out nint hwnd);
}
