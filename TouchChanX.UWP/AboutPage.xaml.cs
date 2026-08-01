using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using R3;
using R3.ObservableEvents;
using TouchChanX.Persistence;
using Windows.ApplicationModel.Core;
using Windows.ApplicationModel.DataTransfer;
using Windows.ApplicationModel.Resources.Core;
using Windows.Globalization;
using Windows.Services.Store;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Animation;
using WinRT;
using WinRT.Interop;

namespace TouchChanX.UWP;

public sealed partial class AboutPage : Page
{
    public LocalizedStrings Strings { get; } = LocalizedStrings.Current;

    public Visibility QqGroupVisibility { get; } = IsZhCn()
        ? Visibility.Visible
        : Visibility.Collapsed;

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

        CopyQqGroupButton.Events().Click.Subscribe(
            _ =>
            {
                var dataPackage = new DataPackage();
                dataPackage.SetText(QqGroupNumberText.Text);
                Clipboard.SetContent(dataPackage);

                if (CopyQqGroupButtonContent.Resources["CopyQqGroupSuccessAnimation"] is Storyboard animation)
                {
                    animation.Begin();
                }
            });
    }

    private StoreContext StoreContext => field ??= new Func<StoreContext>(() =>
    {
        var coreWindow = CoreApplication.GetCurrentView().CoreWindow;
        coreWindow.As<ICoreWindowInterop>().GetWindowHandle(out nint hwnd);

        var storeContext = StoreContext.GetDefault();
        InitializeWithWindow.Initialize(storeContext, hwnd);

        return storeContext;
    })();

    private static bool IsZhCn()
    {
        var languages = ResourceContext.GetForViewIndependentUse().Languages;
        return languages.Count > 0 && string.Equals(languages[0], "zh-CN", StringComparison.OrdinalIgnoreCase);
    }
}

[GeneratedComInterface, Guid("45D64A29-A63E-4CB6-B498-5781D298CB4F")]
partial interface ICoreWindowInterop
{
    [PreserveSig]
    int GetWindowHandle(out nint hwnd);
}
