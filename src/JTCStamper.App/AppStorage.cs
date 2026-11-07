using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace JTCStamper.App;

internal static class AppStorage
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern int GetCurrentPackageFamilyName(ref uint length, StringBuilder? familyName);

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
            return ResolveRoot(AppContext.BaseDirectory,
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), family);
        }
    }

    internal static string ResolveRoot(string executableDirectory, string localAppData, string? familyName) =>
        familyName is null ? executableDirectory : Path.Combine(localAppData, "Packages", familyName, "LocalState");
}
