using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace TouchChanX.Win32.Menu;

internal enum TouchScreenOrientation : uint
{
    Angle0 = 0,
    Angle90 = 1,
    Angle180 = 2,
    Angle270 = 3,
}

/// <summary>
/// Converts the HID coordinate space into the current desktop pixel space.
/// </summary>
/// <remarks>
/// The digitizer reports its native axes even when Windows rotates the display.
/// This mirrors the coordinate conversion used by the original gesture process.
/// </remarks>
internal readonly partial record struct TouchScreenCoordinateMapper(
    int ScreenWidth,
    int ScreenHeight,
    TouchScreenOrientation Orientation)
{
    public static TouchScreenCoordinateMapper Create()
    {
        var orientation = GetCurrentOrientation();
        var width = Math.Max(1, PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CXSCREEN));
        var height = Math.Max(1, PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CYSCREEN));
        return new(width, height, orientation);
    }

    public System.Drawing.Point Map(int physicalX, int physicalY, int physicalMaxX, int physicalMaxY)
    {
        if (physicalMaxX <= 0 || physicalMaxY <= 0)
            return new(physicalX, physicalY);

        bool axesCorrespond = Orientation is TouchScreenOrientation.Angle0 or TouchScreenOrientation.Angle180;
        bool xAxisDirection = Orientation is TouchScreenOrientation.Angle0 or TouchScreenOrientation.Angle270;
        bool yAxisDirection = Orientation is TouchScreenOrientation.Angle0 or TouchScreenOrientation.Angle90;

        int mappedX = axesCorrespond
            ? physicalX * ScreenWidth / physicalMaxX
            : physicalY * ScreenWidth / physicalMaxY;
        int mappedY = axesCorrespond
            ? physicalY * ScreenHeight / physicalMaxY
            : physicalX * ScreenHeight / physicalMaxX;

        mappedX = xAxisDirection ? mappedX : ScreenWidth - mappedX;
        mappedY = yAxisDirection ? mappedY : ScreenHeight - mappedY;
        return new(mappedX, mappedY);
    }

    private static TouchScreenOrientation GetCurrentOrientation()
    {
        var mode = default(DEVMODEW);
        mode.dmSize = (ushort)Marshal.SizeOf<DEVMODEW>();

        if (!PInvoke.EnumDisplaySettings(
                iModeNum: ENUM_DISPLAY_SETTINGS_MODE.ENUM_CURRENT_SETTINGS,
                lpDevMode: ref mode) ||
            (mode.dmFields & DEVMODE_FIELD_FLAGS.DM_DISPLAYORIENTATION) == 0)
        {
            return TouchScreenOrientation.Angle0;
        }

        return mode.dmDisplayOrientation switch
        {
            DEVMODE_DISPLAY_ORIENTATION.DMDO_DEFAULT => TouchScreenOrientation.Angle0,
            DEVMODE_DISPLAY_ORIENTATION.DMDO_90 => TouchScreenOrientation.Angle90,
            DEVMODE_DISPLAY_ORIENTATION.DMDO_180 => TouchScreenOrientation.Angle180,
            DEVMODE_DISPLAY_ORIENTATION.DMDO_270 => TouchScreenOrientation.Angle270,
            _ => TouchScreenOrientation.Angle0,
        };
    }
}
