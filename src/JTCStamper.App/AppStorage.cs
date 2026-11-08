using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace JTCStamper.App;

internal static class AppStorage
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern int GetCurrentPackageFamilyName(ref uint length, StringBuilder? familyName);

    [DllImport("shell32.dll", ExactSpelling = true)]
    static extern int SHGetKnownFolderPath(ref Guid folderId, uint flags, IntPtr token, out IntPtr path);

    internal static string DefaultRoot
    {
        get
        {
            string? family = null;
            if (OperatingSystem.IsWindows())
            {
                uint length = 0;
                int result = GetCurrentPackageFamilyName(ref length, null);
                if (result != 15700) // APPMODEL_ERROR_NO_PACKAGE: preserve EXE/ZIP storage.
                {
                    if (result != 122) throw new Win32Exception(result); // ERROR_INSUFFICIENT_BUFFER
                    var buffer = new StringBuilder(checked((int)length));
                    result = GetCurrentPackageFamilyName(ref length, buffer);
                    if (result != 0) throw new Win32Exception(result);
                    family = buffer.ToString();
                }
            }
            return ResolveRoot(AppContext.BaseDirectory, family);
        }
    }

    internal static string ResolveRoot(string executableDirectory, string? familyName,
        Func<Guid, uint, string>? getKnownFolderPath = null)
    {
        if (familyName is null) return executableDirectory;
        var localAppDataId = new Guid("F1B32785-6FBA-4FCF-9D55-7B8E7F157091");
        const uint noPackageRedirection = 0x00010000; // KF_FLAG_NO_PACKAGE_REDIRECTION
        // Packaged processes otherwise receive a package-specific LocalAppData path.
        var localAppData = (getKnownFolderPath ?? GetKnownFolderPath)(localAppDataId, noPackageRedirection);
        return Path.Combine(localAppData, "Packages", familyName, "LocalState");
    }

    static string GetKnownFolderPath(Guid folderId, uint flags)
    {
        IntPtr path = IntPtr.Zero;
        try
        {
            Marshal.ThrowExceptionForHR(SHGetKnownFolderPath(ref folderId, flags, IntPtr.Zero, out path));
            return Marshal.PtrToStringUni(path) ?? throw new InvalidOperationException("LocalAppData path is unavailable.");
        }
        finally { Marshal.FreeCoTaskMem(path); }
    }
}
