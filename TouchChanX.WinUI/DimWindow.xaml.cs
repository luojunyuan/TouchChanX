using Microsoft.UI.Xaml;

namespace TouchChanX.WinUI;

public sealed partial class DimWindow : Window
{
    private const int MaxBrightnessLevel = 8;
    private int _brightnessLevel;

    public DimWindow()
    {
        InitializeComponent();
    }

    public void Dim()
    {
        if (_brightnessLevel == MaxBrightnessLevel)
            return;

        _brightnessLevel++;
        DimMask.Opacity = _brightnessLevel / 10.0;
        DimMask.Visibility = Visibility.Visible;
    }
}
