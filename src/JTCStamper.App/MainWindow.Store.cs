using System.ComponentModel;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Threading;
using JTCStamper.Core;

namespace JTCStamper.App;

public partial class MainWindow
{
    bool storeBusy, storeClosed, closeAfterStoreOperation, historyReady;
    string storeProgress = "";
    readonly string? appearanceLoadError;
    internal Task InitializationPending { get; }
    internal Task StoreOperationPending { get; private set; } = Task.CompletedTask;
    internal bool IsStoreBusy => storeBusy;

    bool CanStartStoreOperation()
    {
        if (storeClosed) return false;
        if (storeBusy)
        {
            Status.Text = closeAfterStoreOperation ? "処理が完了してから終了します。" : storeProgress;
            return false;
        }
        if (verification?.IsBusy == true)
        {
            Status.Text = "照合が完了してから操作してください。";
            return false;
        }
        return true;
    }

    void UpdateStoreControls()
    {
        StoreProgressIndicator.Visibility = storeBusy ? Visibility.Visible : Visibility.Collapsed;
        StampInputs.IsEnabled = !storeBusy;
        CopyButton.IsEnabled = !storeBusy && journal is not null && historyReady;
        History.IsEnabled = RecentHistory.IsEnabled = !storeBusy;
        NoteInput.IsEnabled = !storeBusy;
        NoteButton.IsEnabled = ExportButton.IsEnabled = !storeBusy && journal is not null && historyReady;
        VerificationHost.IsEnabled = !storeBusy;
    }

    void OnStoreAwareClosing(object? sender, CancelEventArgs e)
    {
        if (storeBusy)
        {
            e.Cancel = true;
            closeAfterStoreOperation = true;
            Status.Text = "処理が完了してから終了します。保存に失敗した場合は、この画面に結果を表示します。";
            return;
        }
        if (!ConfirmDiscard(true, true, "終了")) e.Cancel = true;
    }

    Task<bool> RunStoreOperationAsync(string progress, string failureMessage, Func<Task> action)
    {
        if (!CanStartStoreOperation()) return Task.FromResult(false);
        var task = Run();
        StoreOperationPending = task;
        return task;

        async Task<bool> Run()
        {
            storeBusy = true; storeProgress = progress;
            CancelHistoryDetails(); UpdateStoreControls(); Status.Text = progress;
            bool succeeded = false;
            try
            {
                // Always return to the dispatcher before file work starts, including initial startup.
                await Task.Yield();
                await action();
                succeeded = true;
                return true;
            }
            catch (Exception ex)
            {
                Status.Text = failureMessage + ex.Message;
                return false;
            }
            finally
            {
                storeBusy = false; UpdateStoreControls();
                if (closeAfterStoreOperation)
                {
                    closeAfterStoreOperation = false;
                    if (succeeded) Close();
                    else Status.Text += " ／ 終了せずに停止しました。保存状態を確認してください。";
                }
            }
        }
    }

    internal Task<bool> SwitchJournalAsync(string root)
    {
        if (!CanStartStoreOperation()) return Task.FromResult(false);
        try
        {
            root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
            if (journal is not null && string.Equals(root, Path.TrimEndingDirectorySeparator(Path.GetFullPath(storageRoot)), StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            Status.Text = "保存先を開けません: " + ex.Message;
            if (journal is null)
            {
                RecentHistoryMessage.Text = "履歴の保存先を開けません。［履歴］メニューから保存先を選択してください。";
                RecentHistoryMessage.Visibility = Visibility.Visible;
            }
            UpdateStoreControls();
            return Task.FromResult(false);
        }
        if (journal is not null && !ConfirmDiscard(false, true, "保存先を変更")) return Task.FromResult(false);
        return RunStoreOperationAsync("履歴を読み込み、保存先を確認しています…", "保存先を開けません: ", async () =>
        {
            Journal? next = null;
            try
            {
                next = await Task.Run(() =>
                {
                    var key = KeyStore.Load(root);
                    try { return new Journal(Path.Combine(root, "journal"), key); }
                    finally { CryptographicOperations.ZeroMemory(key); }
                });
                var rows = await Task.Run(() => ReadHistoryRows(next));
                await HistoryDetailsPending;
                journal?.Dispose(); journal = next; next = null;
                storageRoot = root; ClearNoteDrafts(); InstallVerification();
                ApplyHistoryRows(rows, null); historyReady = true;
                Status.Text = "履歴保存先: " + root;
                if (appearanceLoadError is not null) Status.Text += " ／ 外観設定を読めないためWindows設定を使用: " + appearanceLoadError;
            }
            catch
            {
                if (journal is null)
                {
                    historyReady = false;
                    RecentHistoryMessage.Text = "履歴の保存先を開けません。［履歴］メニューから保存先を選択してください。";
                    RecentHistoryMessage.Visibility = Visibility.Visible;
                }
                throw;
            }
            finally { next?.Dispose(); }
        });
    }

    internal Task<bool> CopyAsync(IClipboard clipboard)
    {
        if (!CanStartStoreOperation()) return Task.FromResult(false);
        if (journal is null || !historyReady) { Status.Text = "検証済みの履歴の保存先を開いてください。"; return Task.FromResult(false); }
        Stamp stamp;
        try { RefreshToday(); stamp = Current(); }
        catch (Exception ex) { Status.Text = "コピーできません: " + ex.Message; return Task.FromResult(false); }
        var source = journal;
        return RunStoreOperationAsync("生成・コピーの記録を保存しています…", "成功扱いにしていません。クリップボードに画像が残っている可能性があります。 ", async () =>
        {
            Guid id;
            try
            {
                id = await Task.Run(() => new CopyService(source, new DispatcherClipboard(clipboard, Dispatcher))
                    .GenerateCodedAndCopy(stamp, coded => Dispatcher.Invoke(() =>
                    {
                        var bitmap = StampRenderer.Render(coded); Preview.Source = bitmap;
                        return StampRenderer.Png(bitmap);
                    })));
            }
            catch
            {
                try { await RefreshHistoryAsync(); } catch { historyReady = false; }
                throw;
            }
            await RefreshHistoryAsync(id);
            Status.Text = $"PNGコピーと記録が完了しました。貼付は未確認です。イベントID: {id}";
        });
    }

    // Clipboard/OLE and WPF rendering remain on the STA dispatcher; journal I/O runs on a worker.
    sealed class DispatcherClipboard(IClipboard inner, Dispatcher dispatcher) : IClipboard
    {
        public void Copy(byte[] png) => dispatcher.Invoke(() => inner.Copy(png));
    }

    Task<bool> RefreshHistoryOperationAsync(Guid id) => RunStoreOperationAsync("履歴を読み込んでいます…", "履歴を開けません: ", async () =>
    {
        await RefreshHistoryAsync(id);
        Status.Text = "生成履歴を表示しました。";
    });

    sealed record HistoryRows(List<HistoryRow> Rows, Generation[] Recent);
    static HistoryRows ReadHistoryRows(Journal source)
    {
        var snapshot = new VerificationService(source).ReadHistory();
        var completed = snapshot.Entries.Where(r => r.Entry.Kind == "CopyCompleted").Select(r => r.Entry.EventId).ToHashSet();
        var generations = snapshot.Generations.Reverse().ToArray();
        var rows = generations.Select(g =>
        {
            var state = completed.Contains(g.EventId) ? "コピー記録あり" : "コピー未完了／不明";
            var created = $"{g.CreatedUtc.ToLocalTime():yyyy/MM/dd HH:mm:ss}";
            var stamp = $"表示日付 '{g.Stamp.DisplayDate:yy.MM.dd}  上段 {g.Stamp.Name}  下段 {g.Stamp.Bottom}";
            return new HistoryRow(g.EventId, $"表示日付 {g.Stamp.DisplayDate:yyyy/MM/dd}  上段 {g.Stamp.Name}  下段 {g.Stamp.Bottom}  ／ 生成 {created}  {state}", "生成 " + created, stamp, state);
        }).ToList();
        return new(rows, generations.Take(3).ToArray());
    }

    async Task RefreshHistoryAsync(Guid? select = null)
    {
        if (journal is null) return;
        CancelHistoryDetails(); select ??= (History.SelectedItem as HistoryRow)?.Id;
        try
        {
            var source = journal;
            var rows = await Task.Run(() => ReadHistoryRows(source));
            ApplyHistoryRows(rows, select); historyReady = true;
        }
        catch
        {
            historyReady = false;
            History.ItemsSource = RecentHistory.ItemsSource = null;
            RecentHistoryHeading.Text = "直近の生成履歴";
            RecentHistoryMessage.Text = "履歴を確認できません。画面下部のエラー内容を確認してください。";
            RecentHistoryMessage.Visibility = Visibility.Visible;
            throw;
        }
    }

    void ApplyHistoryRows(HistoryRows data, Guid? select)
    {
        for (int i = 0; i < data.Recent.Length; i++)
        {
            try
            {
                var image = HistoryImage.Load(data.Recent[i]);
                data.Rows[i] = data.Rows[i] with { Thumbnail = image.Image, ImageDescription = image.Description };
            }
            catch { data.Rows[i] = data.Rows[i] with { ImageDescription = "保存画像を表示できません" }; }
        }
        History.ItemsSource = data.Rows;
        RecentHistory.ItemsSource = data.Rows.Take(3).ToList();
        RecentHistoryHeading.Text = $"直近の生成履歴（{Math.Min(3, data.Rows.Count)}件／全{data.Rows.Count}件）";
        RecentHistoryMessage.Text = "この保存先にはまだ生成履歴がありません。生成すると、ここに記録が表示されます。";
        RecentHistoryMessage.Visibility = data.Rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (select.HasValue) History.SelectedItem = data.Rows.FirstOrDefault(x => x.Id == select);
    }
}
