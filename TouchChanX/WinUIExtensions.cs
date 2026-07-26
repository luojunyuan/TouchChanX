using Microsoft.UI.Xaml;

namespace TouchChanX;

public static class WinUIExtension
{
    private const int AntiClippingOffset = 1;

    extension(Windows.Foundation.Rect rect)
    {
        public Windows.Foundation.Rect Scale(double f) =>
            new(rect.X * f, rect.Y * f, rect.Width * f, rect.Height * f);

        public System.Drawing.Rectangle ToGdiRect() =>
            new(
                (int)rect.X,
                (int)rect.Y,
                (int)rect.Width + AntiClippingOffset,
                (int)rect.Height + AntiClippingOffset);
    }

    extension(Window window)
    {
        public double Dpi => window.Content.XamlRoot.RasterizationScale;
    }
}
