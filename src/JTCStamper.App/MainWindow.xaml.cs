using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Media;
using JTCStamper.Core;
using Microsoft.Win32;
namespace JTCStamper.App;
public partial class MainWindow : Window
{
    void VerifyClick(object sender, RoutedEventArgs e)
    {
        if (journal is null) { Status.Text = "照合する履歴の保存先を開いてください。"; return; }
        var window = new VerificationWindow(journal, storageRoot) { Owner = this };
        if (window.ShowDialog() == true && window.SelectedEventId is Guid id)
        {
            try { RefreshHistory(id); } catch (Exception ex) { Status.Text = ex.Message; }
        }
    }
    Journal? journal;
    bool ready;
    readonly StampDateSelection dateSelection = new();
    readonly DispatcherTimer dateTimer = new() { Interval = TimeSpan.FromSeconds(15) };
    string? settingsPath;
    string storageRoot = AppContext.BaseDirectory;
    public sealed record HistoryRow(Guid Id, string Label);
    public MainWindow() : this(AppContext.BaseDirectory) { }
    internal MainWindow(string dataRoot)
    {
        InitializeComponent(); DateInput.SelectedDate = DateTime.Today;
        ready = true; RefreshDateControls(); UpdatePreview();
        dateTimer.Tick += (_, _) => RefreshToday();
        Activated += (_, _) => RefreshToday();
        dateTimer.Start();
        SwitchJournal(dataRoot);
        Closed += (_, _) => { dateTimer.Stop(); journal?.Dispose(); };
    }
    void SwitchJournal(string root)
    {
        try
        {
            if (journal is not null && Path.GetFullPath(root) == Path.GetFullPath(storageRoot)) return;
            var key = KeyStore.Load(root);
            Journal next;
            try { next = new Journal(Path.Combine(root, "journal"), key); }
            finally { CryptographicOperations.ZeroMemory(key); }
            journal?.Dispose(); journal = next; storageRoot = root;
            CopyButton.IsEnabled = true; RefreshHistory(); Status.Text = "履歴保存先: " + root;
        }
        catch (Exception ex)
        {
            CopyButton.IsEnabled = journal is not null;
            Status.Text = "保存先を開けません: " + ex.Message + " ［履歴］から保存先を選択できます。";
        }
    }
    void OpenSettingsClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "印面設定|*.jtcstamp|JSON|*.json" };
        if (dialog.ShowDialog() != true) return;
        try
        {
            var settings = StampSettings.Load(dialog.FileName);
            NameInput.Text = settings.Name; BottomInput.Text = settings.Bottom;
            dateSelection.UseToday(); RefreshDateControls();
            settingsPath = dialog.FileName; UpdatePreview(); Status.Text = "設定を読み込みました。日付は当日に戻しました: " + settingsPath;
        }
        catch (Exception ex) { Status.Text = "設定を読み込めません: " + ex.Message; }
    }
    void SaveSettingsClick(object sender, RoutedEventArgs e) => SaveSettings(false);
    void SaveSettingsAsClick(object sender, RoutedEventArgs e) => SaveSettings(true);
    void SaveSettings(bool choosePath)
    {
        try
        {
            var stamp = Current();
            var path = settingsPath;
            if (choosePath || path is null)
            {
                var dialog = new SaveFileDialog { Filter = "印面設定|*.jtcstamp", FileName = "印面.jtcstamp" };
                if (dialog.ShowDialog() != true) return;
                path = dialog.FileName;
            }
            new StampSettings(1, stamp.Name, stamp.DisplayDate, stamp.Bottom).Save(path);
            settingsPath = path; Status.Text = "設定を保存しました: " + path;
        }
        catch (Exception ex) { Status.Text = "設定を保存できません: " + ex.Message; }
    }
    void DefaultHistoryClick(object sender, RoutedEventArgs e) => SwitchJournal(AppContext.BaseDirectory);
    void ChooseHistoryClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "履歴と鍵を保存するフォルダー" };
        if (dialog.ShowDialog() == true) SwitchJournal(dialog.FolderName);
    }
    void LegacyHistoryClick(object sender, RoutedEventArgs e)
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JTCStamper");
        if (!Directory.Exists(root)) { Status.Text = "旧保存先の履歴はありません。"; return; }
        SwitchJournal(root);
    }
    void ExitClick(object sender, RoutedEventArgs e) => Close();
    void AboutClick(object sender, RoutedEventArgs e) => MessageBox.Show(this,
        "JTC Stamper\nJust To Confirm — 確認した、その記録を。", "バージョン情報", MessageBoxButton.OK, MessageBoxImage.Information);
    Stamp Current()
    {
        if (string.IsNullOrWhiteSpace(NameInput.Text) || string.IsNullOrWhiteSpace(BottomInput.Text))
            throw new ArgumentException("氏名と下段文字を入力してください。");
        if (dateSelection.IsSpecified)
        {
            // Invalid/uncommitted input must not fall back to a previous date.
            if (!DateTime.TryParse(DateInput.Text, out var specified)) throw new ArgumentException("有効な指定日を入力してください。");
            dateSelection.Specify(DateOnly.FromDateTime(specified));
        }
        return new(NameInput.Text.Trim(), dateSelection.Resolve(), BottomInput.Text.Trim(), "wpf-v2-short-date");
    }
    void DateModeClick(object sender, RoutedEventArgs e)
    {
        if (dateSelection.IsSpecified) dateSelection.UseToday();
        else dateSelection.Specify(dateSelection.Resolve());
        RefreshDateControls(); UpdatePreview();
        if (dateSelection.IsSpecified) { DateInput.Focus(); DateInput.IsDropDownOpen = true; }
    }
    void RefreshDateControls()
    {
        var specified = dateSelection.IsSpecified;
        DateModeLabel.Text = specified ? "指定日" : "当日";
        DateModeLabel.Foreground = specified ? Brushes.DarkOrange : SystemColors.ControlTextBrush;
        DateModeButton.Content = specified ? "当日に戻す" : "日付を指定…";
        TodayDisplay.Visibility = specified ? Visibility.Collapsed : Visibility.Visible;
        DateInput.Visibility = specified ? Visibility.Visible : Visibility.Collapsed;
        DateInput.IsEnabled = specified;
        DateInput.IsDropDownOpen = false;
        var date = dateSelection.Resolve();
        TodayDisplay.Text = date.ToString("yyyy/MM/dd");
        DateInput.SelectedDate = date.ToDateTime(TimeOnly.MinValue);
    }
    void RefreshToday()
    {
        if (!ready || dateSelection.IsSpecified) return;
        if (TodayDisplay.Text != dateSelection.Resolve().ToString("yyyy/MM/dd"))
        {
            RefreshDateControls(); UpdatePreview();
        }
    }

    void UpdatePreview() { if (!ready) return; try { Preview.Source = StampRenderer.Render(Current() with { Renderer = RingCode.Renderer, GeometryCode = 0 }); } catch { Preview.Source = null; } }
    void InputsChanged(object sender, TextChangedEventArgs e) => UpdatePreview();
    void DateChanged(object sender, SelectionChangedEventArgs e) => UpdatePreview();
    void CopyClick(object sender, RoutedEventArgs e)
    {
        if (journal is null) return;
        try
        {
            RefreshToday();
            var stamp = Current();
            var id = new CopyService(journal, new WindowsClipboard()).GenerateCodedAndCopy(stamp, coded =>
            {
                var bitmap = StampRenderer.Render(coded);
                Preview.Source = bitmap;
                return StampRenderer.Png(bitmap);
            });
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
