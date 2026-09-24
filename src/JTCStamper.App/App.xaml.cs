using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace JTCStamper.App;
public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Length > 0)
        {
            // Never fall back to a normal launch when test arguments are malformed.
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            if (e.Args[0] != "--smoke-test")
            {
                MessageBox.Show("この起動方法には対応していません。\n\nEXEを引数なしで起動し、アプリ内の［ファイル］メニューや［印影を照合］からファイルを開いてください。",
                    "JTC Stamper — 起動方法", MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown(2);
                return;
            }
            int code = await SmokeTest.RunAsync(e.Args);
            Shutdown(code);
            return;
        }
        try
        {
            var window = new MainWindow();
            // An optional decoration must never prevent the app from starting.
            try
            {
                var icon = LoadWindowIcon();
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

    internal static BitmapFrame LoadWindowIcon() => BitmapFrame.Create(
        new Uri("pack://application:,,,/Assets/jtc-icon.png"), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);

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
