using System.IO;
using System.Windows;
using Microsoft.Win32;
namespace JTCStamper.App;

public partial class MainWindow
{
    async void BackupHistoryClick(object sender, RoutedEventArgs e)
    {
        if (!CanStartStoreOperation()) return;
        if (journal is null) { Status.Text = "履歴の保存先を開いてください。"; return; }
        if (verification?.IsBusy == true) { Status.Text = "照合が完了してからバックアップしてください。"; return; }
        var dialog = new SaveFileDialog
        {
            Title = "同じWindowsユーザー用の履歴バックアップを保存",
            Filter = "JTC履歴バックアップ|*.jtcbackup", FileName = $"JTCStamper-{DateTime.Now:yyyyMMdd-HHmmss}.jtcbackup"
        };
        if (dialog.ShowDialog(this) != true) return;
        await RunStoreOperationAsync("履歴をバックアップしています…", "バックアップできません：", async () =>
        {
            await Task.Run(() => HistoryBackup.Save(dialog.FileName, storageRoot, journal));
            Status.Text = "鍵・履歴・画像・注釈をバックアップしました。同じWindowsユーザー環境専用です。設定ファイルは含みません。";
        });
    }

    async void RestoreHistoryClick(object sender, RoutedEventArgs e)
    {
        if (!CanStartStoreOperation()) return;
        if (verification?.IsBusy == true) { Status.Text = "照合が完了してから復元してください。"; return; }
        var input = new OpenFileDialog { Title = "同じWindowsユーザー用のバックアップを選択", Filter = "JTC履歴バックアップ|*.jtcbackup" };
        if (input.ShowDialog(this) != true) return;
        var folder = new OpenFolderDialog { Title = "復元用の新しいフォルダーを作る場所を選択" };
        if (folder.ShowDialog(this) != true) return;
        var destination = Path.Combine(folder.FolderName, $"JTCStamper-Restored-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6]}");
        if (MessageBox.Show(this, "以下の新しいフォルダーへ復元します。既存の履歴は上書きしません。\n\n" + destination +
            "\n\n同じWindowsユーザー環境専用です。復元後も現在の保存先は変更しません。", "履歴の復元", MessageBoxButton.OKCancel,
            MessageBoxImage.Question, MessageBoxResult.Cancel) != MessageBoxResult.OK) return;
        await RunStoreOperationAsync("バックアップから復元しています…", "復元できません：", async () =>
        {
            await Task.Run(() => HistoryBackup.Restore(input.FileName, destination));
            Status.Text = "復元しました：" + destination;
            if (!closeAfterStoreOperation) MessageBox.Show(this, "復元と履歴検証が完了しました。\n\n" + destination +
                "\n\n［履歴 → 保存先を選択］からこのフォルダーを開けます。", "復元完了", MessageBoxButton.OK, MessageBoxImage.Information);
        });
    }
    async void PortableBackupClick(object sender, RoutedEventArgs e)
    {
        if (!CanStartStoreOperation()) return;
        if (journal is null) { Status.Text = "履歴の保存先を開いてください。"; return; }
        var output = new SaveFileDialog
        {
            Title = "別PCへ移行できるバックアップを保存",
            Filter = "JTC移行用バックアップ|*.jtcportable", FileName = $"JTCStamper-Portable-{DateTime.Now:yyyyMMdd-HHmmss}.jtcportable"
        };
        if (output.ShowDialog(this) != true) return;
        var password = new PassphraseDialog(this, confirm: true);
        try
        {
            if (password.ShowDialog() != true) return;
            string passphrase = password.Passphrase;
            password.Clear();
            try
            {
                await RunStoreOperationAsync("移行用バックアップを暗号化しています…", "移行用バックアップを保存できません：", async () =>
                {
                    await Task.Run(() => HistoryBackup.SavePortable(output.FileName, storageRoot, journal, passphrase));
                    Status.Text = "移行用バックアップを保存しました。パスフレーズは別の場所で保管してください。印面設定は含みません。";
                });
            }
            finally { passphrase = ""; }
        }
        finally { password.Clear(); }
    }

    async void PortableRestoreClick(object sender, RoutedEventArgs e)
    {
        if (!CanStartStoreOperation()) return;
        var input = new OpenFileDialog { Title = "移行用バックアップを選択", Filter = "JTC移行用バックアップ|*.jtcportable" };
        if (input.ShowDialog(this) != true) return;
        var folder = new OpenFolderDialog { Title = "復元用の新しいフォルダーを作る場所を選択" };
        if (folder.ShowDialog(this) != true) return;
        var destination = Path.Combine(folder.FolderName, $"JTCStamper-Migrated-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}");
        if (MessageBox.Show(this, "以下の新しいフォルダーへ復元し、このWindowsユーザーで鍵を保護し直します。\n既存の履歴は上書きしません。\n\n" +
            destination + "\n\n復元後も現在の保存先は変更しません。", "移行用バックアップの復元",
            MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel) != MessageBoxResult.OK) return;
        var password = new PassphraseDialog(this, confirm: false);
        try
        {
            if (password.ShowDialog() != true) return;
            string passphrase = password.Passphrase;
            password.Clear();
            try
            {
                await RunStoreOperationAsync("移行用バックアップを復号・検証しています…", "移行用バックアップを復元できません：", async () =>
                {
                    await Task.Run(() => HistoryBackup.RestorePortable(input.FileName, destination, passphrase));
                    Status.Text = "移行用バックアップを復元しました：" + destination;
                    if (!closeAfterStoreOperation) MessageBox.Show(this, "復元と履歴の認証が完了しました。\n\n" + destination +
                        "\n\n［履歴 → 保存先を選択］から開けます。", "復元完了", MessageBoxButton.OK, MessageBoxImage.Information);
                });
            }
            finally { passphrase = ""; }
        }
        finally { password.Clear(); }
    }

}
