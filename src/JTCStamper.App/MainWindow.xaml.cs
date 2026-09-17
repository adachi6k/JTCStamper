using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using JTCStamper.Core;
using Microsoft.Win32;
namespace JTCStamper.App;
public partial class MainWindow : Window
{
    Journal? journal;
    bool ready;
    public sealed record HistoryRow(Guid Id, string Label);
    public MainWindow()
    {
        InitializeComponent(); DateInput.SelectedDate = DateTime.Today;
        try
        {
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JTCStamper");
            var key = KeyStore.Load(root);
            try { journal = new Journal(Path.Combine(root, "journal"), key); }
            finally { CryptographicOperations.ZeroMemory(key); }
            ready = true; UpdatePreview(); RefreshHistory(); Status.Text = "保存先: " + root;
        }
        catch (Exception ex) { CopyButton.IsEnabled = false; Status.Text = "初期化できません: " + ex.Message; }
        Closed += (_, _) => journal?.Dispose();
    }
    Stamp Current()
    {
        if (string.IsNullOrWhiteSpace(NameInput.Text) || string.IsNullOrWhiteSpace(BottomInput.Text))
            throw new ArgumentException("氏名と下段文字を入力してください。");
        // Parse the editable text too, so an uncommitted/invalid date cannot silently use an older value.
        if (!DateTime.TryParse(DateInput.Text, out var date)) throw new ArgumentException("有効な日付を入力してください。");
        return new(NameInput.Text.Trim(), DateOnly.FromDateTime(date), BottomInput.Text.Trim());
    }
    void UpdatePreview() { if (!ready) return; try { Preview.Source = StampRenderer.Render(Current()); } catch { Preview.Source = null; } }
    void InputsChanged(object sender, TextChangedEventArgs e) => UpdatePreview();
    void DateChanged(object sender, SelectionChangedEventArgs e) => UpdatePreview();
    void CopyClick(object sender, RoutedEventArgs e)
    {
        if (journal is null) return;
        try
        {
            var stamp = Current(); var png = StampRenderer.Png(StampRenderer.Render(stamp));
            var id = new CopyService(journal, new WindowsClipboard()).GenerateAndCopy(stamp, png);
            RefreshHistory(id); Status.Text = $"PNGコピーと記録が完了しました。貼付は未確認です。イベントID: {id}";
        }
        catch (Exception ex)
        {
            Status.Text = "成功扱いにしていません。クリップボードに画像が残っている可能性があります。 " + ex.Message;
            try { RefreshHistory(); } catch { CopyButton.IsEnabled = false; }
        }
    }
    void RefreshHistory(Guid? select = null)
    {
        if (journal is null) return;
        var records = journal.Read();
        History.ItemsSource = records.Where(x => x.Entry.Kind == "Generated").Reverse().Select(x =>
        {
            var g = JsonSerializer.Deserialize<Generation>(x.Entry.Payload)!;
            var state = records.Any(r => r.Entry.EventId == g.EventId && r.Entry.Kind == "CopyCompleted") ? "コピー記録あり" : "コピー未完了／不明";
            return new HistoryRow(g.EventId, $"{g.CreatedUtc.ToLocalTime():yyyy/MM/dd HH:mm:ss}  {g.Stamp.Name}  印面 {g.Stamp.DisplayDate:yyyy/MM/dd}  {state}");
        }).ToList();
        if (select.HasValue) History.SelectedItem = History.Items.Cast<HistoryRow>().FirstOrDefault(x => x.Id == select);
    }
    void HistoryChanged(object sender, SelectionChangedEventArgs e)
    {
        try
        {
            Details.Text = History.SelectedItem is HistoryRow row && journal is not null
                ? string.Join(Environment.NewLine + Environment.NewLine, journal.Read().Where(x => x.Entry.EventId == row.Id)
                    .Select(x => $"{x.Entry.Kind}  {x.Entry.RecordedUtc:O}\nID: {row.Id}\n{x.Entry.Payload}")) : "";
        }
        catch (Exception ex) { Status.Text = "履歴検証に失敗: " + ex.Message; CopyButton.IsEnabled = false; }
    }
    void NoteClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (journal is null || History.SelectedItem is not HistoryRow row) throw new InvalidOperationException("履歴を選択してください。");
            journal.Annotate(row.Id, NoteInput.Text); NoteInput.Clear(); RefreshHistory(row.Id); Status.Text = "注釈を追記しました。";
        }
        catch (Exception ex) { Status.Text = "追記できません: " + ex.Message; }
    }
    void ExportClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (journal is null || History.SelectedItem is not HistoryRow row) throw new InvalidOperationException("履歴を選択してください。");
            var original = journal.Read().Single(x => x.Entry.EventId == row.Id && x.Entry.Kind == "Generated").Signed;
            var dialog = new SaveFileDialog { Filter = "JTC原本|*.jtc", FileName = row.Id + ".jtc" };
            if (dialog.ShowDialog() == true) { File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(original)); Status.Text = "認証情報付き原本を保存しました。秘密鍵は含みません。"; }
        }
        catch (Exception ex) { Status.Text = "原本を保存できません: " + ex.Message; }
    }
}
