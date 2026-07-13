using System.ComponentModel;
using System.Runtime.InteropServices;
using TouchChanX.Win32.Interop;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace TouchChanX.Win32.Menu;

internal static class TouchConversionHooker
{
    private const int MouseClickDelay = 10;
    private const uint WM_LBUTTONUP = 0x0202;
    private const uint WM_RBUTTONUP = 0x0205;
    private const ulong MOUSEEVENTF_FROMTOUCH = 0xFF515700;

    private static readonly object SyncRoot = new();
    private static readonly HOOKPROC HookProc = Hook;
    private static UnhookWindowsHookExSafeHandle? _hookId;
    private static nint _gameWindowHandle;
    private static nint _touchWindowHandle;

    public static void Install(nint gameWindowHandle, nint touchWindowHandle)
    {
        lock (SyncRoot)
        {
            _gameWindowHandle = gameWindowHandle;
            _touchWindowHandle = touchWindowHandle;

            if (_hookId is { IsInvalid: false })
                return;

            var moduleHandle = PInvoke.GetModuleHandle(null);
            _hookId = PInvoke.SetWindowsHookEx(WINDOWS_HOOK_ID.WH_MOUSE_LL, HookProc, moduleHandle, 0);
            if (_hookId.IsInvalid)
                throw new Win32Exception();
        }
    }

    public static void Uninstall()
    {
        lock (SyncRoot)
        {
            if (_hookId is null)
                return;

            _hookId.Dispose();
            _hookId = null;
            _gameWindowHandle = nint.Zero;
            _touchWindowHandle = nint.Zero;
        }
    }

    private static LRESULT Hook(int nCode, WPARAM wParam, LPARAM lParam)
    {
        // Managed exceptions must not cross the native hook boundary.
        try
        {
            if (nCode >= 0 && lParam.Value != 0)
                HandleMouseHook(wParam, lParam);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Debug.WriteLine($"Touch conversion hook failed: {ex}");
        }

        return PInvoke.CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private static void HandleMouseHook(WPARAM wParam, LPARAM lParam)
    {
        var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
        var extraInfo = unchecked((ulong)info.dwExtraInfo);
        if ((extraInfo & MOUSEEVENTF_FROMTOUCH) != MOUSEEVENTF_FROMTOUCH)
            return;

        var message = (uint)wParam.Value;
        if (message is not (WM_LBUTTONUP or WM_RBUTTONUP))
            return;

        nint gameWindowHandle;
        nint touchWindowHandle;
        lock (SyncRoot)
        {
            gameWindowHandle = _gameWindowHandle;
            touchWindowHandle = _touchWindowHandle;
        }

        if (gameWindowHandle == nint.Zero || PInvoke.GetForegroundWindow() != new HWND(gameWindowHandle))
            return;

        if (OsPlatformApi.IsPointInsideWindowOrChild(info.pt, touchWindowHandle))
            return;

        if (!OsPlatformApi.IsPointInsideClientArea(info.pt, gameWindowHandle))
            return;

        switch (message)
        {
            case WM_LBUTTONUP:
                PInvoke.SetCursorPos(info.pt.X, info.pt.Y);
                SendMouseClick(MOUSE_EVENT_FLAGS.MOUSEEVENTF_LEFTDOWN, MOUSE_EVENT_FLAGS.MOUSEEVENTF_LEFTUP);
                break;
            case WM_RBUTTONUP:
                SendMouseClick(MOUSE_EVENT_FLAGS.MOUSEEVENTF_RIGHTDOWN, MOUSE_EVENT_FLAGS.MOUSEEVENTF_RIGHTUP);
                break;
        }
    }

    private static void SendMouseClick(MOUSE_EVENT_FLAGS downFlag, MOUSE_EVENT_FLAGS upFlag)
    {
        _ = Task.Run(async () =>
        {
            SendMouseInput(downFlag);
            await Task.Delay(MouseClickDelay);
            SendMouseInput(upFlag);
        });
    }

    private static unsafe void SendMouseInput(MOUSE_EVENT_FLAGS flags)
    {
        var input = CreateMouseInput(flags);
        _ = PInvoke.SendInput(1, &input, Marshal.SizeOf<INPUT>());
    }

    private static INPUT CreateMouseInput(MOUSE_EVENT_FLAGS flags) => new()
    {
        type = INPUT_TYPE.INPUT_MOUSE,
        Anonymous = new INPUT._Anonymous_e__Union
        {
            mi = new MOUSEINPUT { dwFlags = flags },
        },
    };
}
