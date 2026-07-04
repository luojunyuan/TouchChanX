using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

[GeneratedComInterface, Guid("45D64A29-A63E-4CB6-B498-5781D298CB4F")]
partial interface ICoreWindowInterop
{
    [PreserveSig]
    int GetWindowHandle(out nint hwnd);
}
