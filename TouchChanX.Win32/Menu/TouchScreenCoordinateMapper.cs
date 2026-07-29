using System.Runtime.InteropServices;
using Windows.Win32;
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
    private const uint DM_DISPLAYORIENTATION = 0x00000080;

    public static TouchScreenCoordinateMapper Create()
    {
        var orientation = DisplayOrientationNativeMethods.GetCurrentOrientation();
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

    private static partial class DisplayOrientationNativeMethods
    {
        public static TouchScreenOrientation GetCurrentOrientation()
        {
            unsafe
            {
                var mode = default(DevMode);
                mode.dmSize = (ushort)sizeof(DevMode);

                if (!EnumDisplaySettings(nint.Zero, ENUM_CURRENT_SETTINGS, ref mode) ||
                    (mode.dmFields & DM_DISPLAYORIENTATION) == 0)
                {
                    return TouchScreenOrientation.Angle0;
                }

                return mode.dmDisplayOrientation switch
                {
                    0 => TouchScreenOrientation.Angle0,
                    1 => TouchScreenOrientation.Angle90,
                    2 => TouchScreenOrientation.Angle180,
                    3 => TouchScreenOrientation.Angle270,
                    _ => TouchScreenOrientation.Angle0,
                };
            }
        }

        private const int ENUM_CURRENT_SETTINGS = -1;

        [LibraryImport("user32.dll", EntryPoint = "EnumDisplaySettingsW", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool EnumDisplaySettings(
            nint deviceName,
            int modeNum,
            ref DevMode devMode);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private unsafe struct DevMode
        {
            public fixed char dmDeviceName[32];
            public ushort dmSpecVersion;
            public ushort dmDriverVersion;
            public ushort dmSize;
            public ushort dmDriverExtra;
            public uint dmFields;
            public int dmPositionX;
            public int dmPositionY;
            public uint dmDisplayOrientation;
            public uint dmDisplayFixedOutput;
            public short dmColor;
            public short dmDuplex;
            public short dmYResolution;
            public short dmTTOption;
            public short dmCollate;
            public fixed char dmFormName[32];
            public ushort dmLogPixels;
            public uint dmBitsPerPel;
            public uint dmPelsWidth;
            public uint dmPelsHeight;
            public uint dmDisplayFlags;
            public uint dmDisplayFrequency;
            public uint dmICMMethod;
            public uint dmICMIntent;
            public uint dmMediaType;
            public uint dmDitherType;
            public uint dmReserved1;
            public uint dmReserved2;
            public uint dmPanningWidth;
            public uint dmPanningHeight;
        }
    }
}
