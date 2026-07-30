using System.Runtime.InteropServices;
using R3;
using Windows.Win32;
using Windows.Win32.Devices.HumanInterfaceDevice;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input;
using Windows.Win32.UI.WindowsAndMessaging;

namespace TouchChanX.Win32.Menu;

/// <summary>
/// Gesture recognition service using the original TouchChan point pipeline.
/// Raw Input is delivered through the supplied WinUI window handle.
/// </summary>
public enum RecognizedGesture
{
    ThreeFingerTap,
    TwoFingerTap,
    TwoFingerSwipeUp,
    TwoFingerSwipeDown,
}

public sealed class GestureRecognitionService : IDisposable
{
    private const uint PBT_APMRESUMEAUTOMATIC = 0x0012;
    private const uint PBT_APMRESUMECRITICAL = 0x0006;
    private const uint PBT_APMRESUMESUSPEND = 0x0007;
    private const int HIDP_STATUS_SUCCESS = 0x00110000;
    private const ushort GenericDesktopPage = 0x01;
    private const ushort DigitizerUsagePage = 0x0D;
    private const ushort ContactIdentifierId = 0x51;
    private const ushort ContactCountId = 0x54;
    private const ushort TipId = 0x42;
    private const ushort XCoordinateId = 0x30;
    private const ushort YCoordinateId = 0x31;
    private const ushort TouchScreenUsage = 0x04;
    private const ushort TouchPadUsage = 0x05;
    private const ushort PenUsage = 0x02;
    private const int MinimumPointDistance = 20;
    private const int ProbabilityThreshold = 80;
    private static readonly GesturePattern[] GesturePatterns =
    [
        new(
            RecognizedGesture.TwoFingerTap,
            [
                [new(685, 357)],
                [new(833, 257)],
            ]),
        new(
            RecognizedGesture.ThreeFingerTap,
            [
                [new(615, 345)],
                [new(735, 268)],
                [new(897, 277)],
            ]),
        new(
            RecognizedGesture.TwoFingerSwipeUp,
            [
                [new(242, 414)],
                [
                    new(1000, 213),
                    new(992, 254),
                    new(990, 300),
                    new(987, 340),
                    new(985, 380),
                    new(982, 426),
                    new(981, 469),
                    new(981, 522),
                ],
            ]),
        new(
            RecognizedGesture.TwoFingerSwipeDown,
            [
                [new(294, 490)],
                [
                    new(743, 676),
                    new(744, 645),
                    new(745, 613),
                    new(746, 582),
                    new(746, 551),
                ],
            ]),
    ];

    private readonly Subject<RecognizedGesture> _gestureRecognized = new();
    private readonly Dictionary<nint, ushort> _validDevices = [];
    private readonly Dictionary<int, List<System.Drawing.Point>> _pointsCaptured = [];
    private List<RawPoint> _outputTouchs = [];
    private readonly WndProcDelegate _wndProc;
    private nint _previousWndProc;
    private nint _hwnd;
    private int _requiringContactCount;
    private int _lastPointsCount;
    private bool _sourceActive;
    private bool _disposed;
    private bool _isEnabled;
    private CaptureState _state = CaptureState.Ready;
    private TouchScreenCoordinateMapper _coordinateMapper;

    public GestureRecognitionService(nint hwnd)
    {
        if (hwnd == nint.Zero)
            throw new ArgumentException("Window handle is required.", nameof(hwnd));

        _hwnd = hwnd;
        _wndProc = WndProc;
        _previousWndProc = PInvoke.SetWindowLongPtr(
            new HWND(_hwnd),
            WINDOW_LONG_PTR_INDEX.GWL_WNDPROC,
            Marshal.GetFunctionPointerForDelegate(_wndProc));

        if (_previousWndProc == 0)
            throw new InvalidOperationException("Failed to subclass window for gesture recognition.");

        RegisterRawTouchInput();
    }

    public Observable<RecognizedGesture> ObservableGestureRecognized => _gestureRecognized;

    internal System.Drawing.Point LastGesturePosition { get; private set; }

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value)
                return;

            _isEnabled = value;
            if (!value)
                ResetCapture();
            else
                _state = CaptureState.Ready;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        IsEnabled = false;
        UnregisterRawTouchInput();

        if (_previousWndProc != 0)
            PInvoke.SetWindowLongPtr(new HWND(_hwnd), WINDOW_LONG_PTR_INDEX.GWL_WNDPROC, _previousWndProc);

        _disposed = true;
        _gestureRecognized.Dispose();
    }

    public void UpdateRegistration()
    {
        if (_disposed)
            return;

        _validDevices.Clear();
        RegisterRawTouchInput();
    }

    private void RegisterRawTouchInput()
    {
        var device = new RAWINPUTDEVICE
        {
            usUsagePage = DigitizerUsagePage,
            usUsage = TouchScreenUsage,
            dwFlags = RAWINPUTDEVICE_FLAGS.RIDEV_INPUTSINK | RAWINPUTDEVICE_FLAGS.RIDEV_DEVNOTIFY,
            hwndTarget = new HWND(_hwnd),
        };

        if (!PInvoke.RegisterRawInputDevices([device], (uint)Marshal.SizeOf<RAWINPUTDEVICE>()))
            throw new InvalidOperationException("Failed to register the gesture raw input device.");
    }

    private void UnregisterRawTouchInput()
    {
        var device = new RAWINPUTDEVICE
        {
            usUsagePage = DigitizerUsagePage,
            usUsage = TouchScreenUsage,
            dwFlags = RAWINPUTDEVICE_FLAGS.RIDEV_REMOVE,
        };

        _ = PInvoke.RegisterRawInputDevices([device], (uint)Marshal.SizeOf<RAWINPUTDEVICE>());
    }

    private nint WndProc(nint hwnd, uint message, nuint wParam, nint lParam)
    {
        try
        {
            if (_isEnabled)
            {
                switch (message)
                {
                    case PInvoke.WM_INPUT:
                        ProcessInputCommand(lParam);
                        break;
                    case PInvoke.WM_INPUT_DEVICE_CHANGE:
                        _validDevices.Clear();
                        RefreshRawTouchInput();
                        break;
                    case PInvoke.WM_DISPLAYCHANGE:
                    case PInvoke.WM_SETTINGCHANGE:
                        ResetCapture();
                        break;
                    case PInvoke.WM_POWERBROADCAST when IsResumeNotification(wParam):
                        ResetCapture();
                        RefreshRawTouchInput();
                        break;
                    case PInvoke.WM_POWERBROADCAST:
                    case PInvoke.WM_ENDSESSION:
                        ResetCapture();
                        break;
                }
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Debug.WriteLine($"Gesture recognition failed: {ex}");
            ResetCapture();
        }

        return PInvoke.CallWindowProc(
            Marshal.GetDelegateForFunctionPointer<WNDPROC>(_previousWndProc),
            new HWND(hwnd),
            message,
            wParam,
            lParam);
    }

    private void RefreshRawTouchInput()
    {
        if (_disposed)
            return;

        try
        {
            RegisterRawTouchInput();
        }
        catch (Exception ex) when (ex is InvalidOperationException or ExternalException)
        {
            Debug.WriteLine($"Failed to refresh raw touch input: {ex}");
        }
    }

    private static bool IsResumeNotification(nuint wParam) =>
        wParam is PBT_APMRESUMEAUTOMATIC or PBT_APMRESUMECRITICAL or PBT_APMRESUMESUSPEND;

    private unsafe void ProcessInputCommand(nint rawInputHandle)
    {
        uint size = 0;
        uint headerSize = (uint)Marshal.SizeOf<RAWINPUTHEADER>();
        _ = PInvoke.GetRawInputData(
            new HRAWINPUT((void*)rawInputHandle),
            RAW_INPUT_DATA_COMMAND_FLAGS.RID_INPUT,
            null,
            &size,
            headerSize);

        if (size == 0)
            return;

        nint buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            uint readSize = PInvoke.GetRawInputData(
                new HRAWINPUT((void*)rawInputHandle),
                RAW_INPUT_DATA_COMMAND_FLAGS.RID_INPUT,
                (void*)buffer,
                &size,
                headerSize);
            if (readSize != size)
                return;

            var raw = Marshal.PtrToStructure<RAWINPUT>(buffer);
            if (raw.header.dwType != (uint)RID_DEVICE_INFO_TYPE.RIM_TYPEHID)
                return;

            if (!_validDevices.TryGetValue((nint)raw.header.hDevice, out ushort usage))
            {
                if (!ValidateDevice((nint)raw.header.hDevice, out usage))
                    return;

                _validDevices[(nint)raw.header.hDevice] = usage;
            }

            if (usage != TouchScreenUsage)
                return;

            nint rawData = buffer + ((int)raw.header.dwSize - (int)(raw.data.hid.dwSizeHid * raw.data.hid.dwCount));
            using var preparsedData = GetPreparsedData((nint)raw.header.hDevice);

            if (!_sourceActive)
            {
                _sourceActive = true;
                _coordinateMapper = TouchScreenCoordinateMapper.Create();
            }

            int contactCount = GetContactCount(preparsedData.Handle, rawData, (int)raw.data.hid.dwSizeHid);
            if (contactCount != 0)
            {
                _requiringContactCount = contactCount;
                _outputTouchs = new List<RawPoint>(contactCount);
            }

            if (_requiringContactCount == 0)
                return;

            var linkNodes = GetLinkCollectionNodes(preparsedData.Handle);
            int childCount = linkNodes.Length > 0 ? linkNodes[0].NumberOfChildren : 1;
            var physicalMax = GetPhysicalMax(preparsedData.Handle, linkNodes.Length);

            for (int packetIndex = 0; packetIndex < raw.data.hid.dwCount && _requiringContactCount > 0; packetIndex++)
            {
                nint packet = rawData + packetIndex * (int)raw.data.hid.dwSizeHid;
                for (ushort nodeIndex = 1; nodeIndex <= childCount && _requiringContactCount > 0; nodeIndex++)
                {
                    _outputTouchs.Add(ReadRawPoint(
                        preparsedData.Handle,
                        packet,
                        (int)raw.data.hid.dwSizeHid,
                        nodeIndex,
                        physicalMax,
                        _coordinateMapper,
                        rawData));
                    _requiringContactCount--;
                }
            }

            if (_requiringContactCount != 0)
                return;

            TranslateTouchEvent(_outputTouchs);
            if (_outputTouchs.Count == 0 || _outputTouchs.TrueForAll(static point => !point.IsTip))
                _sourceActive = false;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static unsafe bool ValidateDevice(nint deviceHandle, out ushort usage)
    {
        usage = 0;
        uint size = 0;
        _ = PInvoke.GetRawInputDeviceInfo(
            new HANDLE((void*)deviceHandle),
            RAW_INPUT_DEVICE_INFO_COMMAND.RIDI_DEVICEINFO,
            null,
            &size);
        if (size == 0)
            return false;

        nint buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            Marshal.WriteInt32(buffer, Marshal.SizeOf<RID_DEVICE_INFO>());
            uint result = PInvoke.GetRawInputDeviceInfo(
                new HANDLE((void*)deviceHandle),
                RAW_INPUT_DEVICE_INFO_COMMAND.RIDI_DEVICEINFO,
                (void*)buffer,
                &size);
            if (result == uint.MaxValue)
                return false;

            var info = Marshal.PtrToStructure<RID_DEVICE_INFO>(buffer);
            ushort deviceUsage = info.Anonymous.hid.usUsage;
            if (deviceUsage is not (TouchPadUsage or TouchScreenUsage or PenUsage))
                return true;

            uint nameSize = 0;
            _ = PInvoke.GetRawInputDeviceInfo(
                new HANDLE((void*)deviceHandle),
                RAW_INPUT_DEVICE_INFO_COMMAND.RIDI_DEVICENAME,
                null,
                &nameSize);
            if (nameSize == 0)
                return false;

            nint nameBuffer = Marshal.AllocHGlobal(checked((int)nameSize * sizeof(char)));
            try
            {
                uint nameResult = PInvoke.GetRawInputDeviceInfo(
                    new HANDLE((void*)deviceHandle),
                    RAW_INPUT_DEVICE_INFO_COMMAND.RIDI_DEVICENAME,
                    (void*)nameBuffer,
                    &nameSize);
                if (nameResult == uint.MaxValue)
                    return false;

                string? deviceName = Marshal.PtrToStringUni(nameBuffer);
                if (string.IsNullOrEmpty(deviceName) ||
                    deviceName.Contains("VIRTUAL_DIGITIZER", StringComparison.OrdinalIgnoreCase) ||
                    deviceName.Contains("ROOT", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                usage = deviceUsage;
                return true;
            }
            finally
            {
                Marshal.FreeHGlobal(nameBuffer);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static unsafe int GetContactCount(PHIDP_PREPARSED_DATA preparsedData, nint rawData, int packetSize)
    {
        uint contactCount = 0;
        var status = PInvoke.HidP_GetUsageValue(
            HIDP_REPORT_TYPE.HidP_Input,
            DigitizerUsagePage,
            0,
            ContactCountId,
            out contactCount,
            preparsedData,
            new PSTR((byte*)rawData),
            (uint)packetSize);

        return IsHidSuccess(status) ? (int)contactCount : 0;
    }

    private static unsafe PreparsedDataHandle GetPreparsedData(nint deviceHandle)
    {
        uint size = 0;
        _ = PInvoke.GetRawInputDeviceInfo(
            new HANDLE((void*)deviceHandle),
            RAW_INPUT_DEVICE_INFO_COMMAND.RIDI_PREPARSEDDATA,
            null,
            &size);
        if (size == 0)
            throw new InvalidOperationException("Raw input preparsed data is empty.");

        nint handle = Marshal.AllocHGlobal((int)size);
        uint result = PInvoke.GetRawInputDeviceInfo(
            new HANDLE((void*)deviceHandle),
            RAW_INPUT_DEVICE_INFO_COMMAND.RIDI_PREPARSEDDATA,
            (void*)handle,
            &size);
        if (result == uint.MaxValue)
        {
            Marshal.FreeHGlobal(handle);
            throw new InvalidOperationException("GetRawInputDeviceInfo(RIDI_PREPARSEDDATA) failed.");
        }

        return new(new PHIDP_PREPARSED_DATA(handle));
    }

    private static HIDP_LINK_COLLECTION_NODE[] GetLinkCollectionNodes(PHIDP_PREPARSED_DATA preparsedData)
    {
        uint count = 0;
        _ = PInvoke.HidP_GetLinkCollectionNodes([], ref count, preparsedData);
        if (count == 0)
            return [];

        var nodes = new HIDP_LINK_COLLECTION_NODE[count];
        var status = PInvoke.HidP_GetLinkCollectionNodes(nodes, ref count, preparsedData);
        return IsHidSuccess(status) ? nodes : [];
    }

    private static PointerPoint GetPhysicalMax(PHIDP_PREPARSED_DATA preparsedData, int collectionCount)
    {
        int count = Math.Max(collectionCount, 1);
        var caps = new HIDP_VALUE_CAPS[count];
        ushort capsLength = (ushort)caps.Length;
        _ = PInvoke.HidP_GetSpecificValueCaps(
            HIDP_REPORT_TYPE.HidP_Input,
            GenericDesktopPage,
            0,
            XCoordinateId,
            caps,
            ref capsLength,
            preparsedData);
        int x = capsLength > 0 ? GetMaxCoordinateValue(caps) : 0;

        capsLength = (ushort)caps.Length;
        _ = PInvoke.HidP_GetSpecificValueCaps(
            HIDP_REPORT_TYPE.HidP_Input,
            GenericDesktopPage,
            0,
            YCoordinateId,
            caps,
            ref capsLength,
            preparsedData);
        int y = capsLength > 0 ? GetMaxCoordinateValue(caps) : 0;
        return new(x, y);
    }

    private static int GetMaxCoordinateValue(HIDP_VALUE_CAPS[] caps)
    {
        foreach (var cap in caps)
        {
            int value = cap.PhysicalMax != 0 ? cap.PhysicalMax : cap.LogicalMax;
            if (value != 0)
                return value;
        }

        return 0;
    }

    private static unsafe RawPoint ReadRawPoint(
        PHIDP_PREPARSED_DATA preparsedData,
        nint packet,
        int packetSize,
        ushort nodeIndex,
        PointerPoint physicalMax,
        TouchScreenCoordinateMapper coordinateMapper,
        nint firstPacket)
    {
        uint contactId = 0;
        _ = PInvoke.HidP_GetUsageValue(
            HIDP_REPORT_TYPE.HidP_Input,
            DigitizerUsagePage,
            nodeIndex,
            ContactIdentifierId,
            out contactId,
            preparsedData,
            new PSTR((byte*)packet),
            (uint)packetSize);

        int physicalX = 0;
        int physicalY = 0;
        _ = PInvoke.HidP_GetScaledUsageValue(
            HIDP_REPORT_TYPE.HidP_Input,
            GenericDesktopPage,
            nodeIndex,
            XCoordinateId,
            out physicalX,
            preparsedData,
            new PSTR((byte*)packet),
            (uint)packetSize);
        _ = PInvoke.HidP_GetScaledUsageValue(
            HIDP_REPORT_TYPE.HidP_Input,
            GenericDesktopPage,
            nodeIndex,
            YCoordinateId,
            out physicalY,
            preparsedData,
            new PSTR((byte*)packet),
            (uint)packetSize);

        bool isTip = IsTipContact(preparsedData, firstPacket, packetSize, nodeIndex);
        return new((int)contactId, isTip, coordinateMapper.Map(physicalX, physicalY, physicalMax.X, physicalMax.Y));
    }

    private static unsafe bool IsTipContact(PHIDP_PREPARSED_DATA preparsedData, nint packet, int packetSize, ushort nodeIndex)
    {
        uint usageLength = 0;
        _ = PInvoke.HidP_GetUsages(
            HIDP_REPORT_TYPE.HidP_Input,
            DigitizerUsagePage,
            nodeIndex,
            [],
            ref usageLength,
            preparsedData,
            new PSTR((byte*)packet),
            (uint)packetSize);
        if (usageLength == 0)
            return false;

        var usages = new ushort[usageLength];
        var status = PInvoke.HidP_GetUsages(
            HIDP_REPORT_TYPE.HidP_Input,
            DigitizerUsagePage,
            nodeIndex,
            usages,
            ref usageLength,
            preparsedData,
            new PSTR((byte*)packet),
            (uint)packetSize);
        return IsHidSuccess(status) && usages[0] == TipId;
    }

    private void TranslateTouchEvent(IReadOnlyList<RawPoint> rawPoints)
    {
        int releaseCount = rawPoints.Count(static point => !point.IsTip);
        var points = rawPoints
            .Select(static point => new InputPoint(point.ContactIdentifier, point.Point))
            .ToList();

        if (rawPoints.Count == _lastPointsCount)
        {
            if (releaseCount != 0)
            {
                OnPointUp(points);
                _lastPointsCount -= releaseCount;
                return;
            }

            OnPointMove(points);
        }
        else if (rawPoints.Count > _lastPointsCount)
        {
            if (releaseCount != 0)
                return;

            if (_pointsCaptured.Values.Any(static points => points.Count > 10))
            {
                OnPointMove(points);
                return;
            }

            _lastPointsCount = rawPoints.Count;
            OnPointDown(points);
        }
        else
        {
            OnPointUp(points);
            _lastPointsCount = _lastPointsCount - rawPoints.Count > releaseCount
                ? rawPoints.Count
                : _lastPointsCount - releaseCount;
        }
    }

    private void OnPointDown(List<InputPoint> points)
    {
        if (!_isEnabled)
            return;

        if (_state is not (CaptureState.Ready or CaptureState.Capturing or CaptureState.CapturingInvalid))
            return;

        _ = TryBeginCapture(points);
    }

    private void OnPointMove(List<InputPoint> points)
    {
        if (!_isEnabled)
            return;

        if (_state is CaptureState.Capturing or CaptureState.CapturingInvalid)
            AddPoint(points);
    }

    private void OnPointUp(List<InputPoint> points)
    {
        if (!_isEnabled)
            return;

        if (_state is CaptureState.Capturing or CaptureState.CapturingInvalid)
        {
            EndCapture();
            return;
        }

        if (_state == CaptureState.TriggerFired)
            _state = CaptureState.Ready;
    }

    private bool TryBeginCapture(List<InputPoint> firstPoints)
    {
        _state = CaptureState.CapturingInvalid;
        _pointsCaptured.Clear();

        foreach (var point in firstPoints.OrderBy(static point => point.Point.X))
        {
            if (!_pointsCaptured.ContainsKey(point.ContactIdentifier))
                _pointsCaptured.Add(point.ContactIdentifier, new List<System.Drawing.Point>(30));
        }

        AddPoint(firstPoints);
        return true;
    }

    private void EndCapture()
    {
        var strokes = _pointsCaptured.Values
            .Select(static points => points.ToArray())
            .ToArray();

        _state = CaptureState.Ready;
        var gesture = RecognizeGesture(strokes);
        if (gesture is not null)
        {
            LastGesturePosition = _pointsCaptured.Values
                .Select(static points => points.FirstOrDefault())
                .FirstOrDefault();
            _gestureRecognized.OnNext(gesture.Value);
        }

        _pointsCaptured.Clear();
    }

    private void AddPoint(List<InputPoint> points)
    {
        bool getNewPoint = false;
        foreach (var point in points)
        {
            if (!_pointsCaptured.TryGetValue(point.ContactIdentifier, out var stroke))
                continue;

            if (stroke.Count != 0)
            {
                if (Distance(stroke[^1], point.Point) < MinimumPointDistance)
                    continue;

                if (_state == CaptureState.CapturingInvalid)
                    _state = CaptureState.Capturing;
            }

            getNewPoint = true;
            stroke.Add(point.Point);
        }

        if (getNewPoint && _state == CaptureState.Capturing)
            return;
    }

    private static double Distance(System.Drawing.Point left, System.Drawing.Point right)
    {
        double dx = right.X - left.X;
        double dy = right.Y - left.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static double Distance(System.Drawing.PointF left, System.Drawing.PointF right)
    {
        double dx = right.X - left.X;
        double dy = right.Y - left.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static RecognizedGesture? RecognizeGesture(System.Drawing.Point[][] strokes)
    {
        if (strokes.Length == 0)
            return null;

        double bestProbability = double.MinValue;
        RecognizedGesture? result = null;
        foreach (var pattern in GesturePatterns)
        {
            if (pattern.Strokes.Length != strokes.Length)
                continue;

            double probability = 0;
            bool matches = true;
            for (int i = 0; i < strokes.Length; i++)
            {
                double strokeProbability = GetPointPatternProbability(pattern.Strokes[i], strokes[i]);
                if (strokeProbability <= ProbabilityThreshold)
                {
                    matches = false;
                    break;
                }

                probability += strokeProbability;
            }

            if (matches && probability > bestProbability)
            {
                bestProbability = probability;
                result = pattern.Gesture;
            }
        }

        return result;
    }

    private static double GetPointPatternProbability(System.Drawing.Point[] compareTo, System.Drawing.Point[] points)
    {
        if (compareTo.Length == 1 || points.Length <= 1)
            return points.Length == compareTo.Length ? 100d : 0d;

        var compareToAngles = GetAngularMargins(Interpolate(compareTo, 100));
        var compareAngles = GetAngularMargins(Interpolate(points, 100));
        double totalDelta = 0;
        for (int i = 0; i < compareToAngles.Length; i++)
            totalDelta += GetAngularDelta(compareToAngles[i], compareAngles[i]);

        return Math.Abs(totalDelta / compareToAngles.Length * 31.830988618379067D - 100);
    }

    private static System.Drawing.PointF[] Interpolate(System.Drawing.Point[] points, int segments)
    {
        var interpolated = new List<System.Drawing.PointF>(segments);
        double desiredSegmentLength = GetPointArrayLength(points) / segments;
        double currentSegmentLength = 0;
        var lastTestPoint = new System.Drawing.PointF(points[0].X, points[0].Y);
        interpolated.Add(lastTestPoint);

        for (int currentIndex = 1; currentIndex < points.Length; currentIndex++)
        {
            var currentPoint = new System.Drawing.PointF(points[currentIndex].X, points[currentIndex].Y);
            double incrementLength = Distance(lastTestPoint, currentPoint);
            double testSegmentLength = currentSegmentLength + incrementLength;
            if (testSegmentLength < desiredSegmentLength)
            {
                currentSegmentLength = testSegmentLength;
                lastTestPoint = currentPoint;
                continue;
            }

            double interpolationPosition = (desiredSegmentLength - currentSegmentLength) * (1 / incrementLength);
            var interpolatedPoint = new System.Drawing.PointF(
                (float)((1 - interpolationPosition) * lastTestPoint.X + interpolationPosition * currentPoint.X),
                (float)((1 - interpolationPosition) * lastTestPoint.Y + interpolationPosition * currentPoint.Y));
            interpolated.Add(interpolatedPoint);
            if (interpolated.Count == segments)
                break;

            lastTestPoint = interpolatedPoint;
            currentSegmentLength = 0;
            currentIndex--;
        }

        return interpolated.ToArray();
    }

    private static double GetPointArrayLength(System.Drawing.Point[] points)
    {
        double length = 0;
        for (int i = 1; i < points.Length; i++)
            length += Distance(points[i - 1], points[i]);
        return length;
    }

    private static double[] GetAngularMargins(System.Drawing.PointF[] points)
    {
        var margins = new double[Math.Max(0, points.Length - 1)];
        for (int i = 1; i < points.Length; i++)
            margins[i - 1] = Math.Atan2(points[i].Y - points[i - 1].Y, points[i].X - points[i - 1].X);
        return margins;
    }

    private static double GetAngularDelta(double left, double right)
    {
        double result = Math.Abs(left - right);
        if (result > Math.PI)
            result = Math.PI - (result - Math.PI);
        return result;
    }

    private void ResetCapture()
    {
        _pointsCaptured.Clear();
        _outputTouchs.Clear();
        _requiringContactCount = 0;
        _lastPointsCount = 0;
        _sourceActive = false;
        _state = _isEnabled ? CaptureState.Ready : CaptureState.Disabled;
    }

    private static bool IsHidSuccess(NTSTATUS status) =>
        (int)status.Value == HIDP_STATUS_SUCCESS;

    private enum CaptureState
    {
        Ready,
        Disabled,
        Capturing,
        CapturingInvalid,
        TriggerFired,
    }

    private readonly record struct RawPoint(int ContactIdentifier, bool IsTip, System.Drawing.Point Point);
    private readonly record struct InputPoint(int ContactIdentifier, System.Drawing.Point Point);
    private readonly record struct PointerPoint(int X, int Y);
    private readonly record struct GesturePattern(RecognizedGesture Gesture, System.Drawing.Point[][] Strokes);

    private sealed class PreparsedDataHandle(PHIDP_PREPARSED_DATA handle) : IDisposable
    {
        public PHIDP_PREPARSED_DATA Handle { get; } = handle;

        public void Dispose() => Marshal.FreeHGlobal(Handle.Value);
    }

    private delegate nint WndProcDelegate(nint hwnd, uint message, nuint wParam, nint lParam);

}
