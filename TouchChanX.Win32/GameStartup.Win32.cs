using LightResults;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace TouchChanX.Win32;

public sealed class WindowHandleNotFoundError : Error;

public sealed class ProcessExitedError : Error;

public sealed class ProcessPendingExitedError : Error;

public static partial class GameStartup // Win32
{
    private const int GoodWindowWidth = 320;
    private const int GoodWindowHeight = 240;
    private const int TouchChanXCloseTimeout = 1500;
    private const int TouchChanXKillTimeout = 500;
    private const uint WmClose = 0x0010;

    /// <summary>
    /// 查找合适的窗口句柄，这里需要等待是因为超时处理
    /// </summary>
    public static async Task<Result<nint>> FindGoodWindowHandleAsync(Process proc)
    {
        const int SearchWindowTimeout = 20000;
        const int CheckResponse = 16;

        var goodHandle = proc.MainWindowHandle;

        if (goodHandle != nint.Zero)
        {
            PInvoke.GetClientRect(new(goodHandle), out var clientRect);

            if (IsGoodWindow(clientRect))
                return goodHandle;
        }

        var cts = new CancellationTokenSource(SearchWindowTimeout);
        var timeoutToken = cts.Token;
        while (!timeoutToken.IsCancellationRequested)
        {
            if (proc.HasExited)
                return Result.Failure<nint>(new ProcessExitedError());

            var windows = GetWindowsOfProcess(proc.Id);
            foreach (var handle in windows)
            {
                PInvoke.GetClientRect(handle, out var rect);

                if (IsGoodWindow(rect))
                    return (nint)handle;
            }

            await Task.Delay(CheckResponse, CancellationToken.None);
        }

        return Result.Failure<nint>(new WindowHandleNotFoundError());
    }

    /// <summary>
    /// 检查指定游戏窗口下是否已经挂载过当前 TouchChanX 程序。
    /// </summary>
    public static bool HasAttachedCurrentTouchChanX(nint gameWindowHandle)
    {
        var attachedProcesses = GetAttachedCurrentTouchChanXProcesses(gameWindowHandle);
        try
        {
            return attachedProcesses.Count > 0;
        }
        finally
        {
            foreach (var attachedProcess in attachedProcesses)
                attachedProcess.Process.Dispose();
        }
    }

    /// <summary>
    /// 请求关闭已经挂载在指定游戏窗口下的旧 TouchChanX 实例。
    /// </summary>
    public static async Task<bool> CloseAttachedCurrentTouchChanXAsync(nint gameWindowHandle)
    {
        var attachedProcesses = GetAttachedCurrentTouchChanXProcesses(gameWindowHandle);
        if (attachedProcesses.Count == 0)
            return false;

        try
        {
            foreach (var attachedProcess in attachedProcesses)
                RequestCloseTouchChanXWindow(attachedProcess.WindowHandle);

            foreach (var attachedProcess in attachedProcesses)
                await WaitForTouchChanXExitAsync(attachedProcess.Process).ConfigureAwait(false);
        }
        finally
        {
            foreach (var attachedProcess in attachedProcesses)
                attachedProcess.Process.Dispose();
        }

        return true;
    }

    private static string? GetCurrentProcessImagePath()
    {
        var path = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            try
            {
                using var currentProcess = Process.GetCurrentProcess();
                path = currentProcess.MainModule?.FileName;
            }
            catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
            {
                Debug.WriteLine($"Failed to get current process image path: {ex}");
                return null;
            }
        }

        return string.IsNullOrWhiteSpace(path)
            ? null
            : NormalizeExecutablePath(path);
    }

    private sealed class AttachedTouchChanXProcess(nint windowHandle, Process process)
    {
        public nint WindowHandle { get; } = windowHandle;

        public Process Process { get; } = process;
    }

    private static List<AttachedTouchChanXProcess> GetAttachedCurrentTouchChanXProcesses(nint gameWindowHandle)
    {
        var currentImagePath = GetCurrentProcessImagePath();
        if (currentImagePath is null)
            return [];

        var attachedProcesses = new Dictionary<uint, AttachedTouchChanXProcess>();
        foreach (var childWindow in GetChildWindows(gameWindowHandle))
        {
            _ = PInvoke.GetWindowThreadProcessId(childWindow, out var processId);
            if (processId == 0 ||
                processId == Environment.ProcessId ||
                attachedProcesses.ContainsKey(processId))
            {
                continue;
            }

            var imagePath = GetProcessImagePath(processId);
            if (!string.Equals(imagePath, currentImagePath, StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                var process = Process.GetProcessById((int)processId);
                process.Refresh();

                if (!process.HasExited)
                    attachedProcesses.Add(processId, new((nint)childWindow, process));
                else
                    process.Dispose();
            }
            catch (ArgumentException)
            {
                // Process exited after EnumChildWindows/GetWindowThreadProcessId.
            }
            catch (InvalidOperationException)
            {
                // Process exited while creating or refreshing the managed Process wrapper.
            }
        }

        return [.. attachedProcesses.Values];
    }

    private static void RequestCloseTouchChanXWindow(nint windowHandle) =>
        PInvoke.PostMessage(new(windowHandle), WmClose, 0, 0);

    private static async Task WaitForTouchChanXExitAsync(Process process)
    {
        try
        {
            if (process.HasExited)
                return;

            using var closeTimeout = new CancellationTokenSource(TouchChanXCloseTimeout);
            await process.WaitForExitAsync(closeTimeout.Token).ConfigureAwait(false);
            return;
        }
        catch (OperationCanceledException)
        {
            // Fall back to ending the old TouchChanX process only.
        }
        catch (InvalidOperationException)
        {
            return;
        }

        try
        {
            process.Refresh();
            if (process.HasExited)
                return;

            process.Kill();

            using var killTimeout = new CancellationTokenSource(TouchChanXKillTimeout);
            await process.WaitForExitAsync(killTimeout.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception or OperationCanceledException)
        {
            Debug.WriteLine($"Failed to end old TouchChanX process: {ex}");
        }
    }

    private unsafe struct EnumState
    {
        public int TargetPid;
        public HWND* ResultsPtr;
        public int Count;
        public int Capacity;
    }

    private static unsafe HWND[] GetWindowsOfProcess(int pid)
    {
        var buffer = new HWND[512];
    
        fixed (HWND* pBuffer = buffer)
        {
            var state = new EnumState
            {
                TargetPid = pid,
                ResultsPtr = pBuffer,
                Count = 0,
                Capacity = buffer.Length
            };
        
            PInvoke.EnumChildWindows(HWND.Null, EnumProc, (nint)(&state));
        
            if (state.Count == 0)
                return [];
        
            if (state.Count < buffer.Length)
                Array.Resize(ref buffer, state.Count);
        }
    
        return buffer;
        
        static BOOL EnumProc(HWND hwnd, LPARAM lParam)
        {
            var state = (EnumState*)(nint)lParam;
    
            _ = PInvoke.GetWindowThreadProcessId(hwnd, out var currentPid);
    
            if (currentPid == state->TargetPid && state->Count < state->Capacity)
            {
                state->ResultsPtr[state->Count++] = hwnd;
            }
    
            return true;
        }
    }

    private static HWND[] GetChildWindows(nint parentHandle)
    {
        var results = new List<HWND>();

        PInvoke.EnumChildWindows(new(parentHandle), EnumProc, 0);
        return [.. results];

        BOOL EnumProc(HWND hwnd, LPARAM lParam)
        {
            results.Add(hwnd);
            return true;
        }
    }

    private static bool IsGoodWindow(RECT rect) =>
        rect is { bottom: > GoodWindowHeight, right: > GoodWindowWidth };
}
