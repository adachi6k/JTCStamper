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
    void WindowMenuClick(object sender, RoutedEventArgs e)
    {
        var screen = PointToScreen(new Point(0, TitleBar.ActualHeight));
        var dpi = VisualTreeHelper.GetDpi(this);
        SystemCommands.ShowSystemMenu(this, new Point(screen.X / dpi.DpiScaleX, screen.Y / dpi.DpiScaleY));
    }
    void UpdateThemeChecks()
    {
        SystemThemeItem.IsChecked = Appearance.Current == AppearanceMode.System;
        LightThemeItem.IsChecked = Appearance.Current == AppearanceMode.Light;
        DarkThemeItem.IsChecked = Appearance.Current == AppearanceMode.Dark;
    }
    void ThemeClick(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string value } || !Enum.TryParse<AppearanceMode>(value, out var mode)) return;
        try
        {
            Appearance.Apply(mode); UpdateThemeChecks();
            Appearance.Save(appearanceRoot, mode);
            Status.Text = "テーマを変更しました。次回起動時もこの設定を使います。";
        }
        catch (Exception ex)
        {
            UpdateThemeChecks();
            Status.Text = "外観設定の変更または保存に失敗しました: " + ex.Message;
        }
    }
    void VerifyClick(object sender, RoutedEventArgs e)
    {
        if (journal is null) { Status.Text = "照合する履歴の保存先を開いてください。"; return; }
        MainTabs.SelectedItem = VerificationTab;
    }
    void DeleteHistoryClick(object sender, RoutedEventArgs e)
    {
        if (journal is null) { Status.Text = "履歴の保存先を開いてください。"; return; }
        if (verification?.IsBusy == true) { Status.Text = "照合が完了してから履歴を削除してください。"; return; }
        try
        {
            var records = journal.Read();
            if (records.Count == 0) { Status.Text = "削除する履歴はありません。"; return; }
            int count = records.Count(x => x.Entry.Kind == "Generated");
            var answer = MessageBox.Show(this,
                $"現在の保存先の生成履歴{count}件と、コピー記録・注釈をすべて削除します。\n\n保存先：{storageRoot}\n\n元に戻せません。削除した記録は照合に使えなくなります。別途保存した画像・原本、鍵、印面設定は削除しません。\n\n削除しますか？",
                "生成履歴をすべて削除", MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel);
            if (answer != MessageBoxResult.OK) return;
            string outcome;
            try { journal.DeleteAllHistory(); outcome = "現在の保存先の生成履歴・コピー記録・注釈をすべて削除しました。"; }
            catch (Exception ex) { outcome = "削除を完了できませんでした。一部の新しい記録は削除済みの可能性があります：" + ex.Message; }
            InstallVerification(); ClearNoteDrafts(); Details.Text = ""; RefreshHistory();
            Status.Text = outcome;
        }
        catch (Exception ex) { Status.Text = "履歴の削除処理に失敗しました：" + ex.Message; }
    }
    void ShowHistoryClick(object sender, RoutedEventArgs e) => MainTabs.SelectedItem = HistoryTab;
    VerificationView? verification;
    void InstallVerification()
    {
        verification?.CancelPending();
        verification = new VerificationView(journal!, storageRoot);
        verification.HistoryRequested += id =>
        {
            try { RefreshHistory(id); MainTabs.SelectedItem = HistoryTab; }
            catch (Exception ex) { Status.Text = "履歴を開けません: " + ex.Message; }
        };
        VerificationHost.Content = verification;
        VerificationTab.IsEnabled = true;
    }
    Journal? journal;
    bool ready;
    readonly StampDateSelection dateSelection = new();
    readonly DispatcherTimer dateTimer = new() { Interval = TimeSpan.FromSeconds(15) };
    string? settingsPath;
    readonly string appearanceRoot;
    string storageRoot = AppContext.BaseDirectory;
    public sealed record HistoryRow(Guid Id, string Label, string GeneratedLabel, string StampLabel, string CopyState, ImageSource? Thumbnail = null, string ImageDescription = "");
    public MainWindow() : this(AppContext.BaseDirectory) { }
    internal MainWindow(string dataRoot)
    {
        appearanceRoot = dataRoot;
        string? appearanceError = null;
        try { Appearance.Apply(Appearance.Load(dataRoot)); }
        catch (Exception ex) { Appearance.Apply(AppearanceMode.System); appearanceError = ex.Message; }
        InitializeComponent(); InitializeCaption(); UpdateThemeChecks(); DateInput.SelectedDate = DateTime.Today;
        ready = true; RefreshDateControls(); UpdatePreview();
        savedSettings = EditableSettings;
        Closing += (_, e) => { if (!ConfirmDiscard(true, true, "終了")) e.Cancel = true; };
        dateTimer.Tick += (_, _) => RefreshToday();
        Activated += (_, _) => RefreshToday();
        dateTimer.Start();
        SwitchJournal(dataRoot);
        if (appearanceError is not null) Status.Text += " ／ 外観設定を読めないためWindows設定を使用: " + appearanceError;
        Closed += (_, _) => { dateTimer.Stop(); verification?.CancelPending(); journal?.Dispose(); };
    }
    void SwitchJournal(string root)
    {
        try
        {
            if (journal is not null && Path.GetFullPath(root) == Path.GetFullPath(storageRoot)) return;
            if (verification?.IsBusy == true) { Status.Text = "照合が完了してから履歴の保存先を変更してください。"; return; }
            if (journal is not null && !ConfirmDiscard(false, true, "保存先を変更")) return;
            var key = KeyStore.Load(root);
            Journal next;
            try { next = new Journal(Path.Combine(root, "journal"), key); }
            finally { CryptographicOperations.ZeroMemory(key); }
            journal?.Dispose(); journal = next; storageRoot = root; ClearNoteDrafts();
            InstallVerification();
            CopyButton.IsEnabled = true; RefreshHistory(); Status.Text = "履歴保存先: " + root;
        }
        catch (Exception ex)
        {
            if (journal is null)
            {
                CopyButton.IsEnabled = false;
                RecentHistoryMessage.Text = "履歴の保存先を開けません。［履歴］メニューから保存先を選択してください。";
                RecentHistoryMessage.Visibility = Visibility.Visible;
            }
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
            if (!ConfirmDiscard(true, false, "設定を読込")) return;
            NameInput.Text = settings.Name; BottomInput.Text = settings.Bottom; PlainMode.IsChecked = settings.Plain;
            dateSelection.UseToday(); RefreshDateControls();
            settingsPath = dialog.FileName; savedSettings = EditableSettings; UpdatePreview(); Status.Text = "設定を読み込みました。日付は当日に戻しました: " + settingsPath;
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
            new StampSettings(1, stamp.Name, stamp.DisplayDate, stamp.Bottom, stamp.Renderer == RingCode.PlainRenderer).Save(path);
            settingsPath = path; savedSettings = EditableSettings; Status.Text = "設定を保存しました: " + path;
        }
        catch (Exception ex) { Status.Text = "設定を保存できません: " + ex.Message; }
    }
    void DefaultHistoryClick(object sender, RoutedEventArgs e) => SwitchJournal(AppContext.BaseDirectory);
    void ChooseHistoryClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "履歴と鍵を保存するフォルダー" };
        if (dialog.ShowDialog() == true) SwitchJournal(dialog.FolderName);
    }
    void ExitClick(object sender, RoutedEventArgs e) => Close();
    void AboutClick(object sender, RoutedEventArgs e) => MessageBox.Show(this,
        "JTC Stamper\nJust To Confirm — 確認した、その記録を。\n\n" + BuildIdentity.Description, "バージョン情報", MessageBoxButton.OK, MessageBoxImage.Information);
    Stamp Current()
    {
        if (string.IsNullOrWhiteSpace(NameInput.Text) || string.IsNullOrWhiteSpace(BottomInput.Text))
            throw new ArgumentException("上段文字と下段文字を入力してください。");
        if (dateSelection.IsSpecified)
        {
            // Invalid/uncommitted input must not fall back to a previous date.
            if (!DateTime.TryParse(DateInput.Text, out var specified)) throw new ArgumentException("有効な指定日を入力してください。");
            dateSelection.Specify(DateOnly.FromDateTime(specified));
        }
        return new(NameInput.Text.Trim(), dateSelection.Resolve(), BottomInput.Text.Trim(), PlainMode.IsChecked == true ? RingCode.PlainRenderer : RingCode.Renderer);
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
        DateModeLabel.SetResourceReference(TextBlock.ForegroundProperty, specified ? "AccentTextFillColorPrimaryBrush" : "TextFillColorPrimaryBrush");
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

    void PlainModeChanged(object sender, RoutedEventArgs e) => UpdatePreview();
    void UpdatePreview()
    {
        if (!ready) return;
        try
        {
            var stamp = Current();
            bool plain = stamp.Renderer == RingCode.PlainRenderer;
            Preview.Source = StampRenderer.Render(stamp with { GeometryCode = plain ? null : 0 });
            ModeHelp.Text = plain ? "円の欠け・横線の傾き・コード埋め込みなし。生成記録は保存します。" : "円周の短い欠けは生成時に決まります。";
        }
        catch { Preview.Source = null; }
    }
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
    void RecentHistoryClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: Guid id }) return;
        try
        {
            RefreshHistory(id);
            MainTabs.SelectedItem = HistoryTab;
            if (History.SelectedItem is not null) History.ScrollIntoView(History.SelectedItem);
        }
        catch (Exception ex) { Status.Text = "履歴を開けません: " + ex.Message; }
    }
    void RefreshHistory(Guid? select = null)
    {
        if (journal is null) return;
        select ??= (History.SelectedItem as HistoryRow)?.Id;
        try
        {
            var records = journal.Read();
            var completed = records.Where(r => r.Entry.Kind == "CopyCompleted").Select(r => r.Entry.EventId).ToHashSet();
            // Journal order is the generation order; a user-specified display date must not affect it.
            var rows = new VerificationService(journal).ReadGenerations().Reverse().Select((g, index) =>
            {
                var state = completed.Contains(g.EventId) ? "コピー記録あり" : "コピー未完了／不明";
                var created = $"{g.CreatedUtc.ToLocalTime():yyyy/MM/dd HH:mm:ss}";
                var stamp = $"表示日付 '{g.Stamp.DisplayDate:yy.MM.dd}  上段 {g.Stamp.Name}  下段 {g.Stamp.Bottom}";
                ImageSource? thumbnail = null; string imageDescription = "";
                if (index < 3)
                {
                    try { var image = HistoryImage.Load(g); thumbnail = image.Image; imageDescription = image.Description; }
                    catch { imageDescription = "保存画像を表示できません"; }
                }
                return new HistoryRow(g.EventId, $"表示日付 {g.Stamp.DisplayDate:yyyy/MM/dd}  上段 {g.Stamp.Name}  下段 {g.Stamp.Bottom}  ／ 生成 {created}  {state}", "生成 " + created, stamp, state, thumbnail, imageDescription);
            }).ToList();
            History.ItemsSource = rows;
            RecentHistory.ItemsSource = rows.Take(3).ToList();
            RecentHistoryHeading.Text = $"直近の生成履歴（{Math.Min(3, rows.Count)}件／全{rows.Count}件）";
            RecentHistoryMessage.Text = "この保存先にはまだ生成履歴がありません。生成すると、ここに記録が表示されます。";
            RecentHistoryMessage.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (select.HasValue) History.SelectedItem = rows.FirstOrDefault(x => x.Id == select);
        }
        catch
        {
            History.ItemsSource = null;
            RecentHistory.ItemsSource = null;
            RecentHistoryHeading.Text = "直近の生成履歴";
            RecentHistoryMessage.Text = "履歴を確認できません。画面下部のエラー内容を確認してください。";
            RecentHistoryMessage.Visibility = Visibility.Visible;
            CopyButton.IsEnabled = false;
            throw;
        }
    }
    void HistoryChanged(object sender, SelectionChangedEventArgs e)
    {
        SelectNoteDraft((History.SelectedItem as HistoryRow)?.Id);
        HistoryPreview.Source = null; HistoryImageDescription.Text = "";
        try
        {
            if (History.SelectedItem is HistoryRow selected && journal is not null)
            {
                var generation = new VerificationService(journal).ReadGenerations().Single(x => x.EventId == selected.Id);
                var image = HistoryImage.Load(generation);
                HistoryPreview.Source = image.Image; HistoryImageDescription.Text = image.Description;
            }
            Details.Text = History.SelectedItem is HistoryRow row && journal is not null
                ? string.Join(Environment.NewLine + Environment.NewLine, journal.Read().Where(x => x.Entry.EventId == row.Id)
                    .Select(x => $"{x.Entry.Kind}  {x.Entry.RecordedUtc:O}\nID: {row.Id}\n{HistoryPayload(x.Entry)}")) : "";
        }
        catch (Exception ex) { Status.Text = "履歴検証に失敗: " + ex.Message; CopyButton.IsEnabled = false; }
    }
    static string HistoryPayload(Entry entry)
    {
        if (entry.Kind != "Generated") return entry.Payload;
        var generation = JsonSerializer.Deserialize<Generation>(entry.Payload);
        if (generation is null) return "生成記録を読めません";
        return $"表示日付：{generation.Stamp.DisplayDate:yyyy/MM/dd}\n上段文字：{generation.Stamp.Name}\n下段文字：{generation.Stamp.Bottom}\n" +
            (generation.Stamp.Renderer == RingCode.PlainRenderer ? "プレーン印影" : "幾何コード付き印影") +
            (generation.PngBase64 is null ? "\n画像本体は未保存" : "\n生成時のPNGを保存済み");
    }
    void NoteClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (journal is null || History.SelectedItem is not HistoryRow row) throw new InvalidOperationException("履歴を選択してください。");
            journal.Annotate(row.Id, NoteInput.Text); noteDrafts.Remove(row.Id); NoteInput.Clear(); RefreshHistory(row.Id); Status.Text = "注釈を追記しました。";
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
            if (dialog.ShowDialog() == true) { AtomicFile.Write(dialog.FileName, stream => stream.Write(JsonSerializer.SerializeToUtf8Bytes(original))); Status.Text = "認証情報付き原本を保存しました。秘密鍵は含みません。"; }
        }
        catch (Exception ex) { Status.Text = "原本を保存できません: " + ex.Message; }
    }
}
