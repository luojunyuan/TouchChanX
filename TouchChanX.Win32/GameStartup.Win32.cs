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
        var currentImagePath = GetCurrentProcessImagePath();
        if (currentImagePath is null)
            return false;

        foreach (var childWindow in GetChildWindows(gameWindowHandle))
        {
            _ = PInvoke.GetWindowThreadProcessId(childWindow, out var processId);
            if (processId == 0 || processId == Environment.ProcessId)
                continue;

            var imagePath = GetProcessImagePath(processId);
            if (string.Equals(imagePath, currentImagePath, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static string? GetCurrentProcessImagePath()
    {
        var path = Environment.ProcessPath;
        return string.IsNullOrWhiteSpace(path)
            ? GetProcessImagePath((uint)Environment.ProcessId)
            : NormalizeExecutablePath(path);
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
