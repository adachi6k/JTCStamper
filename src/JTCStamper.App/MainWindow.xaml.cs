using System.IO;
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
    async void DeleteHistoryClick(object sender, RoutedEventArgs e) => await DeleteHistoryAsync(count =>
        MessageBox.Show(this,
            $"現在の保存先の生成履歴{count}件と、コピー記録・注釈をすべて削除します。\n\n保存先：{storageRoot}\n\n元に戻せません。削除した記録は照合に使えなくなります。別途保存した画像・原本、鍵、印面設定は削除しません。\n\n削除しますか？",
            "生成履歴をすべて削除", MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel) == MessageBoxResult.OK);

    internal Task<bool> DeleteHistoryAsync(Func<int, bool> confirm)
    {
        if (journal is null) { Status.Text = "履歴の保存先を開いてください。"; return Task.FromResult(false); }
        return RunStoreOperationAsync("削除する履歴を確認しています…", "履歴の削除処理に失敗しました：", async () =>
        {
            var source = journal;
            var counts = await Task.Run(() => { var records = source.Read(); return (Total: records.Count, Generated: records.Count(x => x.Entry.Kind == "Generated")); });
            if (counts.Total == 0) { Status.Text = "削除する履歴はありません。"; return; }
            if (closeAfterStoreOperation) return;
            if (!confirm(counts.Generated)) { Status.Text = "履歴の削除を取り消しました。"; return; }
            Exception? failure = null;
            try { await Task.Run(source.DeleteAllHistory); }
            catch (Exception ex) { failure = ex; }
            InstallVerification(); ClearNoteDrafts(); Details.Text = "";
            await RefreshHistoryAsync();
            if (failure is not null) throw new IOException("一部の新しい記録は削除済みの可能性があります。" + failure.Message, failure);
            Status.Text = "現在の保存先の生成履歴・コピー記録・注釈をすべて削除しました。";
        });
    }
    void ShowHistoryClick(object sender, RoutedEventArgs e) => MainTabs.SelectedItem = HistoryTab;
    void StatusDetailsClick(object sender, RoutedEventArgs e) => new StatusDetailsDialog(this, Status.Text).ShowDialog();
    VerificationView? verification;
    void InstallVerification()
    {
        verification?.CancelPending();
        verification = new VerificationView(journal!, storageRoot);
        verification.HistoryRequested += async id =>
        {
            if (await RefreshHistoryOperationAsync(id)) MainTabs.SelectedItem = HistoryTab;
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
        Closing += OnStoreAwareClosing;
        dateTimer.Tick += (_, _) => RefreshToday();
        Activated += (_, _) => RefreshToday();
        dateTimer.Start();
        appearanceLoadError = appearanceError;
        InitializationPending = SwitchJournalAsync(dataRoot);
        Closed += (_, _) => { storeClosed = true; CancelHistoryDetails(); dateTimer.Stop(); verification?.CancelPending(); journal?.Dispose(); };
    }
    void OpenSettingsClick(object sender, RoutedEventArgs e)
    {
        if (!CanStartStoreOperation()) return;
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
        if (!CanStartStoreOperation()) return;
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
    async void DefaultHistoryClick(object sender, RoutedEventArgs e) => await SwitchJournalAsync(AppContext.BaseDirectory);
    async void ChooseHistoryClick(object sender, RoutedEventArgs e)
    {
        if (!CanStartStoreOperation()) return;
        var dialog = new OpenFolderDialog { Title = "履歴と鍵を保存するフォルダー" };
        if (dialog.ShowDialog() == true) await SwitchJournalAsync(dialog.FolderName);
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
    async void CopyClick(object sender, RoutedEventArgs e) => await CopyAsync(new WindowsClipboard());

    async void RecentHistoryClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: Guid id }) return;
        if (await RefreshHistoryOperationAsync(id))
        {
            MainTabs.SelectedItem = HistoryTab;
            if (History.SelectedItem is not null) History.ScrollIntoView(History.SelectedItem);
        }
    }
    void HistoryChanged(object sender, SelectionChangedEventArgs e)
    {
        SelectNoteDraft((History.SelectedItem as HistoryRow)?.Id);
        HistoryPreview.Source = null; HistoryImageDescription.Text = "";
        CancelHistoryDetails();
        Details.Text = "";
        if (journal is null || History.SelectedItem is not HistoryRow selected) return;
        var cancellation = new CancellationTokenSource();
        historyDetailsCancellation = cancellation;
        HistoryImageDescription.Text = "履歴を検証しています…（別の履歴を選ぶと切り替わります）";
        HistoryDetailsPending = LoadHistoryDetailsAsync(journal, selected, cancellation);
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
    async void NoteClick(object sender, RoutedEventArgs e)
    {
        if (journal is null || History.SelectedItem is not HistoryRow row) { Status.Text = "履歴を選択してください。"; return; }
        var text = NoteInput.Text;
        await RunStoreOperationAsync("注釈を追記しています…", "追記できません: ", async () =>
        {
            await Task.Run(() => journal.Annotate(row.Id, text));
            noteDrafts.Remove(row.Id); NoteInput.Clear();
            await RefreshHistoryAsync(row.Id); Status.Text = "注釈を追記しました。";
        });
    }
    async void ExportClick(object sender, RoutedEventArgs e)
    {
        if (!CanStartStoreOperation()) return;
        if (journal is null || History.SelectedItem is not HistoryRow row) { Status.Text = "履歴を選択してください。"; return; }
        var dialog = new SaveFileDialog { Filter = "JTC原本|*.jtc", FileName = row.Id + ".jtc" };
        if (dialog.ShowDialog() != true) return;
        await RunStoreOperationAsync("原本を保存しています…", "原本を保存できません: ", async () =>
        {
            await Task.Run(() =>
            {
                var original = journal.Read().Single(x => x.Entry.EventId == row.Id && x.Entry.Kind == "Generated").Signed;
                AtomicFile.Write(dialog.FileName, stream => stream.Write(JsonSerializer.SerializeToUtf8Bytes(original)));
            });
            Status.Text = "認証情報付き原本を保存しました。秘密鍵は含みません。";
        });
    }
}
