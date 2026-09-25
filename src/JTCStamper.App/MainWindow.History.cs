using System.IO;
using System.Text.Json;
using JTCStamper.Core;

namespace JTCStamper.App;

public partial class MainWindow
{
    CancellationTokenSource? historyDetailsCancellation;
    internal Task HistoryDetailsPending { get; private set; } = Task.CompletedTask;

    void CancelHistoryDetails()
    {
        historyDetailsCancellation?.Cancel();
        historyDetailsCancellation = null;
    }

    async Task LoadHistoryDetailsAsync(Journal source, HistoryRow row, CancellationTokenSource cancellation)
    {
        bool IsCurrent() => ReferenceEquals(historyDetailsCancellation, cancellation) &&
            ReferenceEquals(journal, source) && History.SelectedItem is HistoryRow current && current.Id == row.Id;
        try
        {
            var records = await Task.Run(() => source.ReadEvent(row.Id, cancellation.Token), cancellation.Token);
            if (!IsCurrent()) return;
            var entry = records.Single(x => x.Entry.Kind == "Generated");
            var generation = JsonSerializer.Deserialize<Generation>(entry.Entry.Payload)
                ?? throw new InvalidDataException("生成記録を読めません。");
            var image = HistoryImage.Load(generation);
            HistoryPreview.Source = image.Image;
            HistoryImageDescription.Text = image.Description;
            Details.Text = string.Join(Environment.NewLine + Environment.NewLine, records
                .Select(x => $"{x.Entry.Kind}  {x.Entry.RecordedUtc:O}\nID: {row.Id}\n{HistoryPayload(x.Entry)}"));
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex)
        {
            // Closing, switching stores, deleting, or selecting another row invalidates old results/errors.
            if (!IsCurrent()) return;
            HistoryPreview.Source = null;
            HistoryImageDescription.Text = "履歴を検証できませんでした。";
            Details.Text = "";
            Status.Text = "履歴検証に失敗: " + ex.Message;
            CopyButton.IsEnabled = false;
        }
        finally
        {
            if (ReferenceEquals(historyDetailsCancellation, cancellation)) historyDetailsCancellation = null;
            cancellation.Dispose();
        }
    }
}
