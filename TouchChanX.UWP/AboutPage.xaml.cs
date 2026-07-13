using R3;
using R3.ObservableEvents;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Windows.ApplicationModel.Core;
using Windows.ApplicationModel.DataTransfer;
using Windows.Services.Store;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using WinRT;
using WinRT.Interop;

namespace TouchChanX.UWP;

public sealed partial class AboutPage : Page
{
    private const string OpenSourceUrl = "https://github.com/luojunyuan/TachiChanX";

    public ReactiveCommand RateInStoreCommand => field ??= new(async (_, _) =>
    {
        var result = await StoreContext.RequestRateAndReviewAppAsync();

        if (result.ExtendedError is not null)
        {
            var pfn = Windows.ApplicationModel.Package.Current.Id.FamilyName;
            var storeUri = new Uri($"ms-windows-store://review/?PFN={pfn}");
            await Launcher.LaunchUriAsync(storeUri);
        }
    });

    public ReactiveCommand OpenSourceCommand => field ??= new(async (_, _) =>
        await Launcher.LaunchUriAsync(new Uri(OpenSourceUrl)));

    public AboutPage()
    {
        InitializeComponent();

        CopyQqGroupButton.Events().Click.SubscribeAwait(
            async (_, cancellationToken) =>
            {
                var dataPackage = new DataPackage();
                dataPackage.SetText(QqGroupNumberText.Text);
                Clipboard.SetContent(dataPackage);

                CopyQqGroupContent.Visibility = Visibility.Collapsed;
                QqGroupCopiedContent.Visibility = Visibility.Visible;
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                CopyQqGroupContent.Visibility = Visibility.Visible;
                QqGroupCopiedContent.Visibility = Visibility.Collapsed;
            },
            AwaitOperation.Drop);
    }

    private StoreContext StoreContext => field ??= new Func<StoreContext>(() =>
    {
        var coreWindow = CoreApplication.GetCurrentView().CoreWindow;
        coreWindow.As<ICoreWindowInterop>().GetWindowHandle(out nint hwnd);

        var storeContext = StoreContext.GetDefault();
        InitializeWithWindow.Initialize(storeContext, hwnd);

        return storeContext;
    })();
}

[GeneratedComInterface, Guid("45D64A29-A63E-4CB6-B498-5781D298CB4F")]
partial interface ICoreWindowInterop
{
    [PreserveSig]
    int GetWindowHandle(out nint hwnd);
}
