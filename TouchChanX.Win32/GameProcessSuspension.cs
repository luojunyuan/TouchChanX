using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Diagnostics.ToolHelp;
using Windows.Win32.System.Threading;

namespace TouchChanX.Win32;

/// <summary>
/// Suspends the target process threads and resumes those suspensions when disposed.
/// </summary>
public sealed class GameProcessSuspension : IDisposable
{
    private const int MaxThreadEnumerationPasses = 3;

    private readonly IReadOnlyList<SafeFileHandle> _suspendedThreads;
    private int _isDisposed;

    private GameProcessSuspension(IReadOnlyList<SafeFileHandle> suspendedThreads) =>
        _suspendedThreads = suspendedThreads;

    public static bool TrySuspendForWindow(nint windowHandle, out GameProcessSuspension? suspension)
    {
        suspension = null;

        if (windowHandle == nint.Zero ||
            PInvoke.GetWindowThreadProcessId(new HWND(windowHandle), out var processId) == 0 ||
            processId == 0 ||
            processId == Environment.ProcessId)
        {
            return false;
        }

        var suspendedThreads = new List<SafeFileHandle>();
        var suspendedThreadIds = new HashSet<uint>();

        for (var pass = 0; pass < MaxThreadEnumerationPasses; pass++)
        {
            var snapshot = PInvoke.CreateToolhelp32Snapshot(
                CREATE_TOOLHELP_SNAPSHOT_FLAGS.TH32CS_SNAPTHREAD,
                0);
            if (snapshot == HANDLE.Null || (nint)snapshot == -1)
                break;

            using var snapshotHandle = new SafeFileHandle((nint)snapshot, ownsHandle: true);
            var threadEntry = new THREADENTRY32
            {
                dwSize = (uint)Marshal.SizeOf<THREADENTRY32>(),
            };

            if (!PInvoke.Thread32First(snapshotHandle, ref threadEntry))
                break;

            var suspendedInPass = 0;
            do
            {
                if (threadEntry.th32OwnerProcessID != processId ||
                    suspendedThreadIds.Contains(threadEntry.th32ThreadID))
                {
                    continue;
                }

                var threadHandle = PInvoke.OpenThread(
                    THREAD_ACCESS_RIGHTS.THREAD_SUSPEND_RESUME,
                    false,
                    threadEntry.th32ThreadID);
                if (threadHandle == HANDLE.Null)
                    continue;

                var safeThreadHandle = new SafeFileHandle((nint)threadHandle, ownsHandle: true);
                if (PInvoke.SuspendThread(safeThreadHandle) == uint.MaxValue)
                {
                    safeThreadHandle.Dispose();
                    continue;
                }

                suspendedThreadIds.Add(threadEntry.th32ThreadID);
                suspendedThreads.Add(safeThreadHandle);
                suspendedInPass++;
            }
            while (PInvoke.Thread32Next(snapshotHandle, ref threadEntry));

            if (suspendedInPass == 0)
                break;
        }

        if (suspendedThreads.Count == 0)
            return false;

        suspension = new GameProcessSuspension(suspendedThreads);
        return true;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
            return;

        foreach (var threadHandle in _suspendedThreads)
        {
            _ = PInvoke.ResumeThread(threadHandle);
            threadHandle.Dispose();
        }
    }
}
