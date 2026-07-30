using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.Security;
using Windows.Win32.Storage.Packaging.Appx;
using Windows.Win32.UI.HiDpi;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace TouchChanX.Win32.Interop;

public static partial class OsPlatformApi
{
    public static class MessageBox
    {
        private const string DisplayName = "TachiChan";

        public static void Show(string text, string caption = DisplayName) =>
            PInvoke.MessageBox(HWND.Null, text, caption, MESSAGEBOX_STYLE.MB_OK);
    }

    /// <summary>
    /// 发送 Alt + Enter 消息
    /// </summary>
    public static void SendAltEnter(IntPtr hwnd)
    {
        PInvoke.SendMessage(new(hwnd), PInvoke.WM_SYSKEYDOWN, (int)VIRTUAL_KEY.VK_MENU, nint.Zero);
        PInvoke.SendMessage(new(hwnd), PInvoke.WM_SYSKEYDOWN, (int)VIRTUAL_KEY.VK_RETURN, 0x20000000);
        PInvoke.SendMessage(new(hwnd), PInvoke.WM_SYSKEYUP, (int)VIRTUAL_KEY.VK_RETURN, 0x20000000);
        PInvoke.SendMessage(new(hwnd), PInvoke.WM_SYSKEYUP, (int)VIRTUAL_KEY.VK_MENU, nint.Zero);
    }

    /// <summary>
    /// 判断进程对象是否对 DPI 不感知
    /// </summary>
    [SupportedOSPlatform("windows8.1")]
    public static bool IsDpiUnaware(Process process)
    {
        var result = PInvoke.GetProcessDpiAwareness(process.SafeHandle, out var awareType);

        return result == 0 && awareType is PROCESS_DPI_AWARENESS.PROCESS_DPI_UNAWARE;
    }

    /// <summary>
    /// 获取窗口所在显示器的 raw DPI
    /// </summary>
    [SupportedOSPlatform("windows8.1")]
    public static uint GetDpiForWindowsMonitor(nint hwnd)
    {
        var monitorHandle = PInvoke.MonitorFromWindow(new(hwnd), MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
        PInvoke.GetDpiForMonitor(monitorHandle, MONITOR_DPI_TYPE.MDT_EFFECTIVE_DPI, out var dpiX, out _);
        return dpiX;
    }

    [SupportedOSPlatform("windows10.0.22000.0")]
    public static bool TryRegisterDependency(string familyName, PackageDependencyProcessorArchitectures arch)
    {
        var createResult = PInvoke.TryCreatePackageDependency(
            PSID.Null,
            familyName,
            new PACKAGE_VERSION(),
            (Windows.Win32.Storage.Packaging.Appx.PackageDependencyProcessorArchitectures)arch,
            PackageDependencyLifetimeKind.PackageDependencyLifetimeKind_Process,
            string.Empty,
            CreatePackageDependencyOptions.CreatePackageDependencyOptions_None,
            out var packageDependencyId);

        if (createResult.Failed)
            return false;

        var addResult = PInvoke.AddPackageDependency(
            packageDependencyId.ToString(),
            0,
            AddPackageDependencyOptions.AddPackageDependencyOptions_PrependIfRankCollision,
            out _,
            out _);

        return addResult.Succeeded;
    }
}

[Flags]
public enum PackageDependencyProcessorArchitectures : uint
{
    X64 = Windows.Win32.Storage.Packaging.Appx.PackageDependencyProcessorArchitectures.PackageDependencyProcessorArchitectures_X64,
    Arm64 = Windows.Win32.Storage.Packaging.Appx.PackageDependencyProcessorArchitectures.PackageDependencyProcessorArchitectures_Arm64,
}
