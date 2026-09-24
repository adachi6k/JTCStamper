using System.IO;
using System.Windows;
using Microsoft.Win32;
namespace JTCStamper.App;

public partial class MainWindow
{
    void BackupHistoryClick(object sender, RoutedEventArgs e)
    {
        if (journal is null) { Status.Text = "履歴の保存先を開いてください。"; return; }
        if (verification?.IsBusy == true) { Status.Text = "照合が完了してからバックアップしてください。"; return; }
        var dialog = new SaveFileDialog
        {
            Title = "同じWindowsユーザー用の履歴バックアップを保存",
            Filter = "JTC履歴バックアップ|*.jtcbackup", FileName = $"JTCStamper-{DateTime.Now:yyyyMMdd-HHmmss}.jtcbackup"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            HistoryBackup.Save(dialog.FileName, storageRoot, journal);
            Status.Text = "鍵・履歴・画像・注釈をバックアップしました。同じWindowsユーザー環境専用です。設定ファイルは含みません。";
        }
        catch (Exception ex) { Status.Text = "バックアップできません：" + ex.Message; }
    }

    void RestoreHistoryClick(object sender, RoutedEventArgs e)
    {
        if (verification?.IsBusy == true) { Status.Text = "照合が完了してから復元してください。"; return; }
        var input = new OpenFileDialog { Title = "同じWindowsユーザー用のバックアップを選択", Filter = "JTC履歴バックアップ|*.jtcbackup" };
        if (input.ShowDialog(this) != true) return;
        var folder = new OpenFolderDialog { Title = "復元用の新しいフォルダーを作る場所を選択" };
        if (folder.ShowDialog(this) != true) return;
        var destination = Path.Combine(folder.FolderName, $"JTCStamper-Restored-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6]}");
        if (MessageBox.Show(this, "以下の新しいフォルダーへ復元します。既存の履歴は上書きしません。\n\n" + destination +
            "\n\n同じWindowsユーザー環境専用です。復元後も現在の保存先は変更しません。", "履歴の復元", MessageBoxButton.OKCancel,
            MessageBoxImage.Question, MessageBoxResult.Cancel) != MessageBoxResult.OK) return;
        try
        {
            HistoryBackup.Restore(input.FileName, destination);
            Status.Text = "復元しました：" + destination;
            MessageBox.Show(this, "復元と履歴検証が完了しました。\n\n" + destination +
                "\n\n［履歴 → 保存先を選択］からこのフォルダーを開けます。", "復元完了", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) { Status.Text = "復元できません：" + ex.Message; }
    }
}
