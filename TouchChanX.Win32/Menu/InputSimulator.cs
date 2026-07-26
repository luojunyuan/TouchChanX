using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace TouchChanX.Win32.Menu;

internal static class InputSimulator
{
    private const int KeyPressDelay = 10;
    private const int MouseWheelDelta = 120;

    public static async Task PressAsync(params VirtualKeyCode[] keys)
    {
        foreach (var key in keys)
            KeyDown(key);

        await Task.Delay(KeyPressDelay);

        for (var i = keys.Length - 1; i >= 0; i--)
            KeyUp(keys[i]);
    }

    internal static void KeyDown(VirtualKeyCode keyCode) => SendKey(keyCode, isKeyUp: false);

    internal static void KeyUp(VirtualKeyCode keyCode) => SendKey(keyCode, isKeyUp: true);

    public static async Task RightClickAsync()
    {
        SendMouse(MOUSE_EVENT_FLAGS.MOUSEEVENTF_RIGHTDOWN);
        await Task.Delay(KeyPressDelay);
        SendMouse(MOUSE_EVENT_FLAGS.MOUSEEVENTF_RIGHTUP);
    }

    public static void Scroll(int direction)
    {
        var input = new INPUT
        {
            type = INPUT_TYPE.INPUT_MOUSE,
            Anonymous = new INPUT._Anonymous_e__Union
            {
                mi = new MOUSEINPUT
                {
                    mouseData = unchecked((uint)(direction * MouseWheelDelta)),
                    dwFlags = MOUSE_EVENT_FLAGS.MOUSEEVENTF_WHEEL,
                },
            },
        };

        SendMouseInput(input);
    }

    private static unsafe void SendKey(VirtualKeyCode keyCode, bool isKeyUp)
    {
        var input = new INPUT
        {
            type = INPUT_TYPE.INPUT_KEYBOARD,
            Anonymous = new INPUT._Anonymous_e__Union
            {
                ki = new KEYBDINPUT
                {
                    wVk = (VIRTUAL_KEY)(ushort)keyCode,
                    wScan = (ushort)(PInvoke.MapVirtualKey((uint)keyCode, MAP_VIRTUAL_KEY_TYPE.MAPVK_VK_TO_VSC) & 0xFFU),
                    dwFlags = isKeyUp ? KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP : (KEYBD_EVENT_FLAGS)0,
                },
            },
        };

        _ = PInvoke.SendInput(1, &input, Marshal.SizeOf<INPUT>());
    }

    private static unsafe void SendMouse(MOUSE_EVENT_FLAGS flags)
    {
        var input = new INPUT
        {
            type = INPUT_TYPE.INPUT_MOUSE,
            Anonymous = new INPUT._Anonymous_e__Union
            {
                mi = new MOUSEINPUT { dwFlags = flags },
            },
        };

        SendMouseInput(input);
    }

    private static unsafe void SendMouseInput(INPUT input) =>
        _ = PInvoke.SendInput(1, &input, Marshal.SizeOf<INPUT>());
}

internal enum VirtualKeyCode : ushort
{
    None = 0x00,
    Tab = 0x09,
    Enter = 0x0D,
    Shift = 0x10,
    Control = 0x11,
    Menu = 0x12,
    Space = 0x20,
    Left = 0x25,
    Up = 0x26,
    Right = 0x27,
    Down = 0x28,
    A = 0x41,
    D = 0x44,
    S = 0x53,
    LeftWindows = 0x5B,
    VolumeDown = 0xAE,
    VolumeUp = 0xAF,
}
