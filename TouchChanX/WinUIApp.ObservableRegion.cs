using TouchChanX.Win32.Interop;

namespace TouchChanX;

public partial class WinUIApp
{
    private sealed class WindowObservableRegionSet(nint hwnd)
    {
        private System.Drawing.Rectangle? _baseRegion;
        private System.Drawing.Rectangle? _messageFlyoutRegion;
        private bool _usesOriginalRegion = true;

        public void UseOriginalRegion()
        {
            _usesOriginalRegion = true;
            Apply();
        }

        public void SetBaseRegion(System.Drawing.Rectangle rect)
        {
            _baseRegion = rect;
            _usesOriginalRegion = false;
            Apply();
        }

        public void SetMessageFlyoutRegion(System.Drawing.Rectangle? rect)
        {
            _messageFlyoutRegion = rect;
            Apply();
        }

        private void Apply()
        {
            if (_usesOriginalRegion)
            {
                OsPlatformApi.ResetWindowOriginalObservableRegion(hwnd);
                return;
            }

            var regions = new List<System.Drawing.Rectangle>();
            if (_baseRegion is { } baseRegion)
                regions.Add(baseRegion);
            if (_messageFlyoutRegion is { } messageFlyoutRegion)
                regions.Add(messageFlyoutRegion);

            OsPlatformApi.SetWindowObservableRegions(hwnd, regions);
        }
    }
}
