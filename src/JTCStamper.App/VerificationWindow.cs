using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using JTCStamper.Core;
using Microsoft.Win32;

namespace JTCStamper.App;

public sealed class VerificationWindow : Window
{
    readonly VerificationService service;
    readonly TextBlock result = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 12) };
    readonly ListBox candidates = new();
    readonly Button show = new() { Content = "選択した生成履歴を開く", IsEnabled = false };
    public Guid? SelectedEventId { get; private set; }
    public VerificationWindow(Journal journal, string storageRoot)
    {
        service = new(journal);
        Title = "印影の照合"; Width = 760; Height = 510; MinWidth = 580; MinHeight = 380;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new DockPanel { Margin = new Thickness(16) }; Content = panel;
        var top = new StackPanel(); DockPanel.SetDock(top, Dock.Top); panel.Children.Add(top);
        top.Children.Add(new TextBlock { Text = "照合先：" + storageRoot, TextWrapping = TextWrapping.Wrap });
        top.Children.Add(new TextBlock { Text = "原本（.jtc）は認証情報を確認します。PNGはファイルの完全一致で照合します。\n画像の加工・再保存、切り抜き、Officeからの取り出しに対応する画像解析は未実装です。",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) });
        var buttons = new WrapPanel(); top.Children.Add(buttons);
        var file = new Button { Content = "ファイルを選択…" }; file.Click += Open; buttons.Children.Add(file);
        var clipboard = new Button { Content = "クリップボードのPNGを照合" }; clipboard.Click += FromClipboard; buttons.Children.Add(clipboard);
        top.Children.Add(result);
        var bottom = new StackPanel(); DockPanel.SetDock(bottom, Dock.Bottom); panel.Children.Add(bottom);
        bottom.Children.Add(show);
        bottom.Children.Add(new TextBlock { Text = "一致は無断コピーの検出や真正性の証明ではありません。一致記録なしだけで偽造とは断定できません。",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) });
        candidates.SelectionChanged += (_, _) => show.IsEnabled = candidates.SelectedItem is Candidate;
        show.Click += (_, _) => { if (candidates.SelectedItem is Candidate item) { SelectedEventId = item.Generation.EventId; DialogResult = true; } };
        panel.Children.Add(candidates);
    }
    sealed record Candidate(Generation Generation)
    {
        public override string ToString() => $"{Generation.Stamp.Name} ／ 表示日付 {Generation.Stamp.DisplayDate:yyyy/MM/dd} ／ {Generation.Stamp.Bottom}\n生成 {Generation.CreatedUtc.ToLocalTime():yyyy/MM/dd HH:mm:ss} ／ {Generation.EventId}";
    }
    void Display(VerificationResult value)
    {
        candidates.ItemsSource = value.Matches.Select(x => new Candidate(x)).ToArray(); show.IsEnabled = false;
        result.Text = value.Label + "\n" + value.Explanation + (value.Matches.Count > 0 ? $"\n一致した履歴：{value.Matches.Count}件" : "");
    }
    void Failed(string reason) => Display(new(VerificationStatus.Indeterminate, reason, []));
    void Open(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "原本・PNG|*.jtc;*.png|原本|*.jtc|PNG画像|*.png", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            using var stream = File.OpenRead(dialog.FileName);
            var bytes = ReadLimited(stream, Path.GetExtension(dialog.FileName).Equals(".jtc", StringComparison.OrdinalIgnoreCase) ? 1024 * 1024 : 32 * 1024 * 1024);
            if (Path.GetExtension(dialog.FileName).Equals(".jtc", StringComparison.OrdinalIgnoreCase)) Display(service.Original(new UTF8Encoding(false, true).GetString(bytes)));
            else CheckPng(bytes);
        }
        catch (Exception ex) { Failed("ファイルを読み取れませんでした：" + ex.Message); }
    }
    void FromClipboard(object sender, RoutedEventArgs e)
    {
        try
        {
            var data = Clipboard.GetData("PNG");
            if (data is Stream stream) { if (stream.CanSeek) stream.Position = 0; CheckPng(ReadLimited(stream, 32 * 1024 * 1024)); }
            else if (data is byte[] bytes && bytes.Length <= 32 * 1024 * 1024) CheckPng(bytes);
            else Failed("クリップボードにPNG形式がありません。ビットマップのみの画像は今回の照合では扱えません。");
        }
        catch (Exception ex) { Failed("クリップボードを読み取れませんでした：" + ex.Message); }
    }
    void CheckPng(byte[] bytes)
    {
        if (bytes.Length < 8 || !bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
        { Failed("PNG形式として読み取れませんでした。"); return; }
        Display(service.Image(bytes));
    }
    static byte[] ReadLimited(Stream stream, int limit)
    {
        using var output = new MemoryStream(); var buffer = new byte[8192]; int count;
        while ((count = stream.Read(buffer)) > 0)
        {
            if (output.Length + count > limit) throw new InvalidDataException("ファイルがサイズ上限を超えています。");
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }
}
