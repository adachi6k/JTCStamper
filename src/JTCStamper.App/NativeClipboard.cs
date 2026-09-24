using System.IO;
using System.Runtime.InteropServices;
using System.ComponentModel;

namespace JTCStamper.App;

// Copy clipboard-owned memory while it is locked. Never free the clipboard handle.
internal static class NativeClipboard
{
    const int MaxBytes = 32 * 1024 * 1024;
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint RegisterClipboardFormat(string format);
    [DllImport("user32.dll", SetLastError = true)] static extern bool OpenClipboard(IntPtr owner);
    [DllImport("user32.dll")] static extern bool CloseClipboard();
    [DllImport("user32.dll")] static extern bool IsClipboardFormatAvailable(uint format);
    [DllImport("user32.dll")] static extern IntPtr GetClipboardData(uint format);
    [DllImport("kernel32.dll")] static extern nuint GlobalSize(IntPtr handle);
    [DllImport("kernel32.dll")] static extern IntPtr GlobalLock(IntPtr handle);
    [DllImport("kernel32.dll")] static extern bool GlobalUnlock(IntPtr handle);
    [DllImport("user32.dll")] static extern uint GetClipboardSequenceNumber();
    internal static uint SequenceNumber => GetClipboardSequenceNumber();
    static readonly uint PngFormat = RegisterClipboardFormat("PNG");

    internal static (bool Available, byte[]? Bytes) ReadPng()
    {
        if (!OpenClipboard(IntPtr.Zero)) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            if (!IsClipboardFormatAvailable(PngFormat)) return (false, null);
            var handle = GetClipboardData(PngFormat); if (handle == IntPtr.Zero) return (true, null);
            var size = GlobalSize(handle);
            if (size > MaxBytes) throw new InvalidDataException("画像は32MiB以内にしてください。");
            if (size == 0) return (true, null);
            var pointer = GlobalLock(handle); if (pointer == IntPtr.Zero) return (true, null);
            try
            {
                var bytes = new byte[(int)size]; Marshal.Copy(pointer, bytes, 0, bytes.Length); return (true, bytes);
            }
            finally { GlobalUnlock(handle); }
        }
        finally { CloseClipboard(); }
    }

}
