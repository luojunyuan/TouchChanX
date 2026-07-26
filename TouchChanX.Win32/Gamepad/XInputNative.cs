using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace TouchChanX.Win32.Gamepad;

internal static partial class XInputNative
{
    private const uint ErrorSuccess = 0;
    private const int ControllerCount = 4;

    private static readonly GetStateDelegate? GetState = ResolveGetState();

    public static bool IsSupported => GetState is not null;

    public static bool TryGetFirstConnected(
        out uint userIndex,
        out XInputGamepadState state)
    {
        if (GetState is null)
        {
            userIndex = 0;
            state = default;
            return false;
        }

        for (uint index = 0; index < ControllerCount; index++)
        {
            if (TryGetState(index, out state))
            {
                userIndex = index;
                return true;
            }
        }

        userIndex = 0;
        state = default;
        return false;
    }

    private static bool TryGetState(uint userIndex, out XInputGamepadState state)
    {
        state = default;
        var getState = GetState;
        return getState is not null && getState(userIndex, out state) == ErrorSuccess;
    }

    private static GetStateDelegate? ResolveGetState()
    {
        if (TryLoadXInput("xinput1_4.dll"))
            return GetState14;

        if (TryLoadXInput("xinput1_3.dll"))
            return GetState13;

        if (TryLoadXInput("xinput9_1_0.dll"))
            return GetState910;

        return null;
    }

    private static bool TryLoadXInput(string libraryName)
    {
        if (!NativeLibrary.TryLoad(libraryName, out var libraryHandle))
            return false;

        if (NativeLibrary.TryGetExport(libraryHandle, "XInputGetState", out _))
            return true;

        NativeLibrary.Free(libraryHandle);
        return false;
    }

    [LibraryImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    private static partial uint GetState14(uint userIndex, out XInputGamepadState state);

    [LibraryImport("xinput1_3.dll", EntryPoint = "XInputGetState")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    private static partial uint GetState13(uint userIndex, out XInputGamepadState state);

    [LibraryImport("xinput9_1_0.dll", EntryPoint = "XInputGetState")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    private static partial uint GetState910(uint userIndex, out XInputGamepadState state);

    private delegate uint GetStateDelegate(uint userIndex, out XInputGamepadState state);
}

[StructLayout(LayoutKind.Sequential)]
internal struct XInputGamepadState
{
    public uint PacketNumber;
    public XInputGamepad Gamepad;
}

[StructLayout(LayoutKind.Sequential)]
internal struct XInputGamepad
{
    public GamepadButtonFlags Buttons;
    public byte LeftTrigger;
    public byte RightTrigger;
    public short LeftThumbX;
    public short LeftThumbY;
    public short RightThumbX;
    public short RightThumbY;
}

[Flags]
internal enum GamepadButtonFlags : ushort
{
    DPadUp = 0x0001,
    DPadDown = 0x0002,
    DPadLeft = 0x0004,
    DPadRight = 0x0008,
    Start = 0x0010,
    Back = 0x0020,
    LeftThumb = 0x0040,
    RightThumb = 0x0080,
    LeftShoulder = 0x0100,
    RightShoulder = 0x0200,
    A = 0x1000,
    B = 0x2000,
    X = 0x4000,
    Y = 0x8000,
}
