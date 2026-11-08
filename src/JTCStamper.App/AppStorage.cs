using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace JTCStamper.App;

internal static class AppStorage
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    static extern int GetCurrentPackageFamilyName(ref uint length, StringBuilder? familyName);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    static extern int GetCurrentPackagePath(ref uint length, StringBuilder? path);

    [DllImport("shell32.dll", ExactSpelling = true)]
    static extern int SHGetKnownFolderPath(ref Guid folderId, uint flags, IntPtr token, out IntPtr path);

    internal static string DefaultRoot
    {
        get
        {
            string? family = null;
            string? packageDirectory = null;
            if (OperatingSystem.IsWindows())
            {
                packageDirectory = ReadPackageString(GetCurrentPackagePath);
                if (packageDirectory is not null) family = ReadPackageString(GetCurrentPackageFamilyName);
            }
            return ResolveRoot(AppContext.BaseDirectory, family, packageDirectory);
        }
    }

    delegate int GetPackageString(ref uint length, StringBuilder? value);

    static string? ReadPackageString(GetPackageString query)
    {
        uint length = 0;
        int result = query(ref length, null);
        if (result == 15700) return null; // APPMODEL_ERROR_NO_PACKAGE
        if (result != 122) throw new Win32Exception(result); // ERROR_INSUFFICIENT_BUFFER
        var buffer = new StringBuilder(checked((int)length));
        result = query(ref length, buffer);
        if (result != 0) throw new Win32Exception(result);
        return buffer.ToString();
    }

    internal static string ResolveRoot(string executableDirectory, string? familyName, string? packageDirectory,
        Func<Guid, uint, string>? getKnownFolderPath = null)
    {
        // Our manifest installs the EXE at the package root. An external EXE can
        // inherit another package's identity; that must not change its storage.
        if (familyName is null || packageDirectory is null ||
            !string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(executableDirectory)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(packageDirectory)), StringComparison.OrdinalIgnoreCase))
            return executableDirectory;
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
