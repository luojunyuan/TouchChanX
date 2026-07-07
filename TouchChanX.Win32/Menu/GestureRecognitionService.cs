using System.Numerics;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Devices.HumanInterfaceDevice;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input;
using Windows.Win32.UI.WindowsAndMessaging;

namespace TouchChanX.Win32.Menu;

public sealed class GestureRecognitionService : IDisposable
{
    private const uint WM_INPUT = 0x00FF;
    private const uint WM_INPUT_DEVICE_CHANGE = 0x00FE;
    private const uint WM_POINTERUPDATE = 0x0245;
    private const uint WM_POINTERDOWN = 0x0246;
    private const uint WM_POINTERUP = 0x0247;
    private const uint WM_POINTERCAPTURECHANGED = 0x024C;
    private const int HIDP_STATUS_SUCCESS = 0x00110000;
    private const ushort GenericDesktopPage = 0x01;
    private const ushort DigitizerUsagePage = 0x0D;
    private const ushort ContactIdentifierId = 0x51;
    private const ushort ContactCountId = 0x54;
    private const ushort TipId = 0x42;
    private const ushort XCoordinateId = 0x30;
    private const ushort YCoordinateId = 0x31;
    private const ushort TouchScreenUsage = 0x04;
    private const double TapMovementThreshold = 32.0;
    private const double SwipeDistanceThreshold = 90.0;
    private static readonly TimeSpan TapDurationThreshold = TimeSpan.FromMilliseconds(450);

    private readonly WndProcDelegate _wndProc;
    private readonly Dictionary<int, PointerStroke> _activeStrokes = [];
    private readonly Dictionary<nint, ushort> _validRawInputDevices = [];
    private readonly List<PointerStroke> _completedStrokes = [];
    private readonly List<RawContact> _rawContacts = [];
    private DateTimeOffset _captureStartedAt;
    private nint _previousWndProc;
    private nint _hwnd;
    private uint _maxContactCount;
    private int _requiredRawContactCount;
    private bool _disposed;
    private bool _isEnabled;

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

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value)
                return;

            _isEnabled = value;
            ResetCapture();
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
    }

    private nint WndProc(nint hwnd, uint msg, nuint wParam, nint lParam)
    {
        if (_isEnabled)
        {
            switch (msg)
            {
                case WM_INPUT:
                    ProcessRawInput(lParam);
                    break;
                case WM_INPUT_DEVICE_CHANGE:
                    _validRawInputDevices.Clear();
                    break;
                case WM_POINTERDOWN:
                    HandlePointerDown(GetPointerId(wParam), TryGetPointerPoint(wParam, out var downPoint) ? downPoint : null);
                    break;
                case WM_POINTERUPDATE:
                    HandlePointerUpdate(GetPointerId(wParam), TryGetPointerPoint(wParam, out var updatePoint) ? updatePoint : null);
                    break;
                case WM_POINTERUP:
                    HandlePointerUp(GetPointerId(wParam), TryGetPointerPoint(wParam, out var upPoint) ? upPoint : null);
                    break;
                case WM_POINTERCAPTURECHANGED:
                    ResetCapture();
                    break;
            }
        }

        return PInvoke.CallWindowProc(
            Marshal.GetDelegateForFunctionPointer<WNDPROC>(_previousWndProc),
            new HWND(hwnd),
            msg,
            wParam,
            lParam);
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
            throw new InvalidOperationException("Failed to register raw touch input.");
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

    private void ProcessRawInput(nint rawInputHandle)
    {
        try
        {
            if (!TryReadRawInput(rawInputHandle, out var contacts))
                return;

            ProcessRawContacts(contacts);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ExternalException or OverflowException)
        {
        }
    }

    private unsafe bool TryReadRawInput(nint rawInputHandle, out IReadOnlyList<RawContact> contacts)
    {
        contacts = [];
        uint size = 0;
        uint headerSize = (uint)Marshal.SizeOf<RAWINPUTHEADER>();
        _ = PInvoke.GetRawInputData(new HRAWINPUT((void*)rawInputHandle), RAW_INPUT_DATA_COMMAND_FLAGS.RID_INPUT, null, &size, headerSize);
        if (size == 0)
            return false;

        nint buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            uint readSize = PInvoke.GetRawInputData(new HRAWINPUT((void*)rawInputHandle), RAW_INPUT_DATA_COMMAND_FLAGS.RID_INPUT, (void*)buffer, &size, headerSize);
            if (readSize != size)
                return false;

            var raw = Marshal.PtrToStructure<RAWINPUT>(buffer);
            if (raw.header.dwType != (uint)RID_DEVICE_INFO_TYPE.RIM_TYPEHID)
                return false;

            if (!TryGetRawInputUsage((nint)raw.header.hDevice, out ushort usage) || usage != TouchScreenUsage)
                return false;

            int contactCount = GetContactCount((nint)raw.header.hDevice, buffer, raw);
            if (contactCount != 0)
            {
                _requiredRawContactCount = contactCount;
                _rawContacts.Clear();
            }

            if (_requiredRawContactCount == 0)
                return false;

            var hid = raw.data.hid;
            nint rawData = buffer + ((int)raw.header.dwSize - (int)(hid.dwSizeHid * hid.dwCount));
            using var preparsedData = GetPreparsedData((nint)raw.header.hDevice);
            var linkNodes = GetLinkCollectionNodes(preparsedData.Handle);
            int childCount = linkNodes.Length > 0 ? linkNodes[0].NumberOfChildren : 1;
            if (childCount <= 0)
                childCount = contactCount;

            var physicalMax = GetPhysicalMax(preparsedData.Handle, linkNodes.Length);

            for (int packetIndex = 0; packetIndex < hid.dwCount && _requiredRawContactCount > 0; packetIndex++)
            {
                nint packet = rawData + packetIndex * (int)hid.dwSizeHid;
                for (ushort nodeIndex = 1; nodeIndex <= childCount && _requiredRawContactCount > 0; nodeIndex++)
                {
                    _rawContacts.Add(ReadRawContact(preparsedData.Handle, packet, (int)hid.dwSizeHid, nodeIndex, physicalMax));
                    _requiredRawContactCount--;
                }
            }

            if (_requiredRawContactCount != 0)
                return false;

            contacts = _rawContacts.ToArray();
            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private unsafe bool TryGetRawInputUsage(nint deviceHandle, out ushort usage)
    {
        usage = 0;
        if (_validRawInputDevices.TryGetValue(deviceHandle, out usage))
            return true;

        uint size = 0;
        _ = PInvoke.GetRawInputDeviceInfo(new HANDLE((void*)deviceHandle), RAW_INPUT_DEVICE_INFO_COMMAND.RIDI_DEVICEINFO, null, &size);
        if (size == 0)
            return false;

        nint buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            uint result = PInvoke.GetRawInputDeviceInfo(new HANDLE((void*)deviceHandle), RAW_INPUT_DEVICE_INFO_COMMAND.RIDI_DEVICEINFO, (void*)buffer, &size);
            if (result == uint.MaxValue)
                return false;

            var info = Marshal.PtrToStructure<RID_DEVICE_INFO>(buffer);
            usage = info.Anonymous.hid.usUsage;
            _validRawInputDevices[deviceHandle] = usage;
            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static unsafe int GetContactCount(nint deviceHandle, nint rawInputBuffer, RAWINPUT raw)
    {
        var hid = raw.data.hid;
        nint rawData = rawInputBuffer + ((int)raw.header.dwSize - (int)(hid.dwSizeHid * hid.dwCount));
        using var preparsedData = GetPreparsedData(deviceHandle);
        uint contactCount = 0;
        var status = PInvoke.HidP_GetUsageValue(
            HIDP_REPORT_TYPE.HidP_Input,
            DigitizerUsagePage,
            0,
            ContactCountId,
            out contactCount,
            preparsedData.Handle,
            new PSTR((byte*)rawData),
            hid.dwSizeHid);

        return IsHidSuccess(status)
            ? (int)contactCount
            : 0;
    }

    private static unsafe PreparsedDataHandle GetPreparsedData(nint deviceHandle)
    {
        uint size = 0;
        _ = PInvoke.GetRawInputDeviceInfo(new HANDLE((void*)deviceHandle), RAW_INPUT_DEVICE_INFO_COMMAND.RIDI_PREPARSEDDATA, null, &size);
        if (size == 0)
            throw new InvalidOperationException("Raw input preparsed data is empty.");

        nint handle = Marshal.AllocHGlobal((int)size);
        uint result = PInvoke.GetRawInputDeviceInfo(new HANDLE((void*)deviceHandle), RAW_INPUT_DEVICE_INFO_COMMAND.RIDI_PREPARSEDDATA, (void*)handle, &size);
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
        if (count <= 0)
            return [];

        var nodes = new HIDP_LINK_COLLECTION_NODE[count];
        var status = PInvoke.HidP_GetLinkCollectionNodes(nodes, ref count, preparsedData);
        if (!IsHidSuccess(status))
            return [];

        return nodes;
    }

    private static PointerPoint GetPhysicalMax(PHIDP_PREPARSED_DATA preparsedData, int collectionCount)
    {
        int count = Math.Max(collectionCount, 1);
        var caps = new HIDP_VALUE_CAPS[count];
        ushort capsLength = (ushort)caps.Length;
        var xStatus = PInvoke.HidP_GetSpecificValueCaps(
            HIDP_REPORT_TYPE.HidP_Input,
            GenericDesktopPage,
            0,
            XCoordinateId,
            caps,
            ref capsLength,
            preparsedData);
        int x = IsHidSuccess(xStatus)
            ? GetMaxCoordinateValue(caps, capsLength)
            : 0;

        capsLength = (ushort)caps.Length;
        var yStatus = PInvoke.HidP_GetSpecificValueCaps(
            HIDP_REPORT_TYPE.HidP_Input,
            GenericDesktopPage,
            0,
            YCoordinateId,
            caps,
            ref capsLength,
            preparsedData);
        int y = IsHidSuccess(yStatus)
            ? GetMaxCoordinateValue(caps, capsLength)
            : 0;

        return new(x, y);
    }

    private static int GetMaxCoordinateValue(HIDP_VALUE_CAPS[] caps, ushort capsLength)
    {
        int length = Math.Clamp(capsLength, 0, caps.Length);
        for (int i = 0; i < length; i++)
        {
            int value = caps[i].PhysicalMax != 0 ? caps[i].PhysicalMax : caps[i].LogicalMax;
            if (value != 0)
                return value;
        }

        return 0;
    }

    private static unsafe RawContact ReadRawContact(PHIDP_PREPARSED_DATA preparsedData, nint packet, int packetSize, ushort nodeIndex, PointerPoint physicalMax)
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

        var point = ScaleToScreen(physicalX, physicalY, physicalMax);
        bool isTip = IsTipContact(preparsedData, packet, packetSize, nodeIndex);
        return new((int)contactId, isTip, point);
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

        if (usageLength <= 0)
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

        return IsHidSuccess(status) &&
            usages.Take((int)usageLength).Contains(TipId);
    }

    private static PointerPoint ScaleToScreen(int physicalX, int physicalY, PointerPoint physicalMax)
    {
        if (physicalMax.X <= 0 || physicalMax.Y <= 0)
            return new(physicalX, physicalY);

        int screenWidth = Math.Max(1, PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CXSCREEN));
        int screenHeight = Math.Max(1, PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CYSCREEN));
        return new(
            physicalX * screenWidth / physicalMax.X,
            physicalY * screenHeight / physicalMax.Y);
    }

    private void ProcessRawContacts(IReadOnlyList<RawContact> contacts)
    {
        foreach (var contact in contacts)
        {
            if (contact.IsTip)
            {
                if (_activeStrokes.ContainsKey(contact.Id))
                    HandlePointerUpdate(contact.Id, contact.Point);
                else
                    HandlePointerDown(contact.Id, contact.Point);
            }
            else
            {
                HandlePointerUp(contact.Id, contact.Point);
            }
        }

        if (_activeStrokes.Count == 0 && _completedStrokes.Count > 0)
            CompleteCapture();
        else if (ShouldTriggerEarlySwipe())
            CompleteCapture();
    }

    // Trigger early when one finger lifts with a swipe and the remaining fingers are stationary (held).
    private bool ShouldTriggerEarlySwipe()
    {
        if (_completedStrokes.Count == 0 || _activeStrokes.Count == 0)
            return false;

        bool anyCompletedSwipe = _completedStrokes.Any(
            s => Math.Abs(s.Delta.Y) >= SwipeDistanceThreshold &&
                 Math.Abs(s.Delta.Y) > Math.Abs(s.Delta.X) * 1.3);

        bool allActiveStationary = _activeStrokes.Values.All(
            s => s.Movement <= TapMovementThreshold);

        return anyCompletedSwipe && allActiveStationary;
    }

    private void HandlePointerDown(int pointerId, PointerPoint? point)
    {
        if (point is not { } value)
            return;

        if (_activeStrokes.Count == 0)
        {
            _completedStrokes.Clear();
            _captureStartedAt = DateTimeOffset.Now;
        }

        _activeStrokes[pointerId] = new PointerStroke(value);
        _maxContactCount = Math.Max(_maxContactCount, (uint)_activeStrokes.Count);
    }

    private void HandlePointerUpdate(int pointerId, PointerPoint? point)
    {
        if (point is not { } value)
            return;

        if (_activeStrokes.TryGetValue(pointerId, out var stroke))
            stroke.Add(value);
    }

    private void HandlePointerUp(int pointerId, PointerPoint? point)
    {
        if (point is not { } value)
            return;

        if (_activeStrokes.Remove(pointerId, out var stroke))
        {
            stroke.Add(value);
            _completedStrokes.Add(stroke);
        }
    }

    private readonly List<GestureDefinition> _gestures =
    [
        new("ThreeFingerTap", ctx => ctx.MaxContactCount >= 3
            && ctx.Duration <= TapDurationThreshold
            && ctx.MaxMovement <= TapMovementThreshold),
        new("TwoFingerTap",  ctx => ctx.MaxContactCount == 2
            && ctx.Duration <= TapDurationThreshold
            && ctx.MaxMovement <= TapMovementThreshold),
        new("TwoFingerSwipeUp",   ctx => IsVerticalSwipe(ctx) && ctx.DominantStroke!.Delta.Y < 0),
        new("TwoFingerSwipeDown", ctx => IsVerticalSwipe(ctx) && ctx.DominantStroke!.Delta.Y > 0),
    ];

    private static bool IsVerticalSwipe(GestureContext ctx) =>
        ctx.MaxContactCount >= 2 &&
        ctx.DominantStroke is not null &&
        Math.Abs(ctx.DominantStroke.Delta.Y) >= SwipeDistanceThreshold &&
        Math.Abs(ctx.DominantStroke.Delta.Y) > Math.Abs(ctx.DominantStroke.Delta.X) * 1.3;


    private void CompleteCapture()
    {
        string? gestureName = RecognizeGesture();
        if (gestureName is not null)
            Debug.WriteLine($"Gesture recognized: {gestureName}");

        ResetCapture();
    }

    private string? RecognizeGesture()
    {
        if (_completedStrokes.Count == 0)
            return null;

        var ctx = BuildGestureContext();
        return _gestures.FirstOrDefault(g => g.Matches(ctx))?.Name;
    }

    private GestureContext BuildGestureContext() => new(
        MaxContactCount: (int)_maxContactCount,
        Duration: DateTimeOffset.Now - _captureStartedAt,
        MaxMovement: _completedStrokes.Max(static s => s.Movement),
        DominantStroke: _completedStrokes.MaxBy(static s => Math.Abs(s.Delta.Y))
    );

    private void ResetCapture()
    {
        _activeStrokes.Clear();
        _completedStrokes.Clear();
        _rawContacts.Clear();
        _maxContactCount = 0;
        _requiredRawContactCount = 0;
    }

    private static bool TryGetPointerPoint(nuint wParam, out PointerPoint point)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(8))
        {
            point = default;
            return false;
        }

        uint pointerId = (uint)(wParam & 0xFFFF);
        if (!PInvoke.GetPointerInfo(pointerId, out var pointerInfo))
        {
            point = default;
            return false;
        }

        point = new(pointerInfo.ptPixelLocation.X, pointerInfo.ptPixelLocation.Y);
        return true;
    }

    private static int GetPointerId(nuint wParam) =>
        (int)(wParam & 0xFFFF);

    private static bool IsHidSuccess(NTSTATUS status) =>
        (int)status.Value == HIDP_STATUS_SUCCESS;

    private readonly record struct RawContact(int Id, bool IsTip, PointerPoint Point);

    private readonly record struct PointerPoint(int X, int Y);

    private sealed class PointerStroke
    {
        private readonly PointerPoint _start;
        private PointerPoint _last;

        public PointerStroke(PointerPoint start)
        {
            _start = start;
            _last = start;
        }

        public Vector2 Delta => new((float)(_last.X - _start.X), (float)(_last.Y - _start.Y));

        public double Movement { get; private set; }

        public void Add(PointerPoint point)
        {
            double distance = Distance(_last, point);
            if (distance <= 0)
                return;

            Movement += distance;
            _last = point;
        }

        private static double Distance(PointerPoint left, PointerPoint right)
        {
            double dx = right.X - left.X;
            double dy = right.Y - left.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }

    private sealed class PreparsedDataHandle(PHIDP_PREPARSED_DATA handle) : IDisposable
    {
        public PHIDP_PREPARSED_DATA Handle { get; } = handle;

        public void Dispose() =>
            Marshal.FreeHGlobal(Handle.Value);
    }

    private sealed record GestureContext(
        int MaxContactCount,
        TimeSpan Duration,
        double MaxMovement,
        PointerStroke? DominantStroke);

    private sealed record GestureDefinition(string Name, Func<GestureContext, bool> Matches);

    private delegate nint WndProcDelegate(nint hWnd, uint msg, nuint wParam, nint lParam);
}
