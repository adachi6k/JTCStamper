using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Windows.Interop;

// Explicit research command only. Does not run during fixture generation.
internal static class EmfClipboard
{
    internal static void Copy(string path)
    {
        byte[] expected=File.ReadAllBytes(path);
        if(expected.Length<108 || expected.Length>32*1024*1024) throw new InvalidDataException("Invalid EMF size.");
        nint handle=SetEnhMetaFileBits((uint)expected.Length,expected);
        if(handle==0) throw new Win32Exception(Marshal.GetLastWin32Error(),"SetEnhMetaFileBits");
        try
        {
            using var owner=new HwndSource(new HwndSourceParameters("JTC EMF clipboard experiment") { Width=0,Height=0,WindowStyle=0 });
            bool opened=false;
            for(int i=0;i<5 && !opened;i++) { opened=OpenClipboard(owner.Handle); if(!opened) Thread.Sleep(100); }
            if(!opened) throw new Win32Exception(Marshal.GetLastWin32Error(),"OpenClipboard");
            try
            {
                if(!EmptyClipboard()) throw new Win32Exception(Marshal.GetLastWin32Error(),"EmptyClipboard");
                if(SetClipboardData(14,handle)==0) throw new Win32Exception(Marshal.GetLastWin32Error(),"SetClipboardData");
                handle=0; // Ownership transferred. Never delete the system-owned metafile.
                nint borrowed=GetClipboardData(14);
                if(borrowed==0) throw new InvalidDataException("CF_ENHMETAFILE unavailable after copy.");
                uint count=GetEnhMetaFileBits(borrowed,0,null);
                if(count!=expected.Length) throw new InvalidDataException("Clipboard EMF size mismatch.");
                var actual=new byte[count];
                if(GetEnhMetaFileBits(borrowed,count,actual)!=count || !actual.AsSpan().SequenceEqual(expected))
                    throw new InvalidDataException("Clipboard EMF bytes differ.");
            }
            finally { CloseClipboard(); }
        }
        finally { if(handle!=0) DeleteEnhMetaFile(handle); }
        Console.WriteLine("CF_ENHMETAFILE readback matched SHA256 "+Convert.ToHexString(SHA256.HashData(expected)));
    }
    [DllImport("gdi32.dll",SetLastError=true)] static extern nint SetEnhMetaFileBits(uint size,byte[] data);
    [DllImport("gdi32.dll",SetLastError=true)] static extern uint GetEnhMetaFileBits(nint handle,uint size,byte[]? data);
    [DllImport("gdi32.dll")] static extern bool DeleteEnhMetaFile(nint handle);
    [DllImport("user32.dll",SetLastError=true)] static extern bool OpenClipboard(nint owner);
    [DllImport("user32.dll")] static extern bool CloseClipboard();
    [DllImport("user32.dll",SetLastError=true)] static extern bool EmptyClipboard();
    [DllImport("user32.dll",SetLastError=true)] static extern nint SetClipboardData(uint format,nint handle);
    [DllImport("user32.dll")] static extern nint GetClipboardData(uint format);
}
