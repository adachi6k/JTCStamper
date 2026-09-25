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
            var snapshot = await Task.Run(() => new VerificationService(source).ReadEventHistory(row.Id, cancellation.Token), cancellation.Token);
            if (!IsCurrent()) return;
            var records = snapshot.Entries;
            var generation = snapshot.Generations.Single();
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
            historyReady = false; UpdateStoreControls();
        }
        finally
        {
            if (ReferenceEquals(historyDetailsCancellation, cancellation)) historyDetailsCancellation = null;
            cancellation.Dispose();
        }
    }
}
