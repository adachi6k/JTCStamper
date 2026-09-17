using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace JTCStamper.App;
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            var window = new MainWindow();
            // An optional decoration must never prevent the app from starting.
            try
            {
                var icon = BitmapFrame.Create(new Uri("pack://application:,,,/Assets/jtc-icon.png"),
                    BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                icon.Freeze(); window.Icon = icon;
            }
            catch (Exception iconError) { WriteDiagnostic("icon-error.log", iconError); }
            MainWindow = window;
            window.Show();
        }
        catch (Exception error)
        {
            var log = WriteDiagnostic("startup-error.log", error);
            MessageBox.Show("起動できませんでした。\n\n" + error.GetBaseException().Message +
                "\n\n詳細: " + log, "JTC Stamper — 起動エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    static string WriteDiagnostic(string filename, Exception error)
    {
        foreach (var root in new[] { AppContext.BaseDirectory, Path.Combine(Path.GetTempPath(), "JTCStamper") })
        {
            try
            {
                Directory.CreateDirectory(root);
                var path = Path.Combine(root, filename);
                File.AppendAllText(path, $"{DateTimeOffset.Now:O}\n{error}\n\n");
                return path;
            }
            catch { /* Try the alternate diagnostic directory. */ }
        }
        return "エラーログを保存できませんでした。";
    }
}
