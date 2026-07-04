using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace TouchChanX.UWP;

internal static unsafe partial class ShellLinkResolver
{
    private const int MaxPathBufferLength = 32768;
    private const uint ClsctxInprocServer = 0x1;
    private const uint StgmRead = 0x0;

    private static readonly Guid ClsidShellLink = new("00021401-0000-0000-C000-000000000046");
    private static readonly Guid IidIUnknown = new("00000000-0000-0000-C000-000000000046");
    private static readonly StrategyBasedComWrappers ComWrappers = new();

    public static string ResolveIfShortcut(string path)
    {
        if (!Path.GetExtension(path).Equals(".lnk", StringComparison.OrdinalIgnoreCase))
            return path;

        return TryResolveShortcut(path, out var resolvedPath) ? resolvedPath : string.Empty;
    }

    private static bool TryResolveShortcut(string path, out string resolvedPath)
    {
        resolvedPath = string.Empty;

        var hr = CoCreateInstance(ClsidShellLink, 0, ClsctxInprocServer, IidIUnknown, out var unknown);
        if (Failed(hr) || unknown == 0)
            return false;

        object? comObject = null;
        try
        {
            comObject = ComWrappers.GetOrCreateObjectForComInstance(
                unknown,
                CreateObjectFlags.UniqueInstance);

            var shellLink = (IShellLinkW)comObject;
            var persistFile = (IPersistFile)shellLink;
            hr = persistFile.Load(path, StgmRead);
            if (Failed(hr))
                return false;

            var buffer = new char[MaxPathBufferLength];
            fixed (char* bufferPtr = buffer)
            {
                hr = shellLink.GetPath(bufferPtr, buffer.Length, 0, ShellLinkGetPathFlags.UncPriority);
            }

            if (Failed(hr))
                return false;

            var length = Array.IndexOf(buffer, '\0');
            if (length <= 0)
                return false;

            resolvedPath = new string(buffer, 0, length);
            return true;
        }
        finally
        {
            (comObject as IDisposable)?.Dispose();
        }
    }

    private static bool Failed(int hr) => hr < 0;

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(
        in Guid rclsid,
        nint pUnkOuter,
        uint dwClsContext,
        in Guid riid,
        out nint ppv);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("000214F9-0000-0000-C000-000000000046")]
internal unsafe partial interface IShellLinkW
{
    [PreserveSig]
    int GetPath(char* pszFile, int cch, nint pfd, ShellLinkGetPathFlags fFlags);
}

[GeneratedComInterface]
[Guid("0000010C-0000-0000-C000-000000000046")]
internal partial interface IPersist
{
    [PreserveSig]
    int GetClassID(out Guid pClassID);
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("0000010B-0000-0000-C000-000000000046")]
internal partial interface IPersistFile : IPersist
{
    [PreserveSig]
    int IsDirty();

    [PreserveSig]
    int Load(string pszFileName, uint dwMode);
}

internal enum ShellLinkGetPathFlags : uint
{
    UncPriority = 0x2,
}
