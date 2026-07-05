using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace TouchChanX.Win32.Menu;

internal static class InputSimulator
{
    private const int KeyPressDelay = 10;

    public static async Task PressAsync(params VirtualKeyCode[] keys)
    {
        foreach (var key in keys)
            SendKey(key, isKeyUp: false);

        await Task.Delay(KeyPressDelay);

        for (var i = keys.Length - 1; i >= 0; i--)
            SendKey(keys[i], isKeyUp: true);
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
}

internal enum VirtualKeyCode : ushort
{
    Tab = 0x09,
    Enter = 0x0D,
    Shift = 0x10,
    Menu = 0x12,
    A = 0x41,
    D = 0x44,
    S = 0x53,
    LeftWindows = 0x5B,
    VolumeDown = 0xAE,
    VolumeUp = 0xAF,
}
