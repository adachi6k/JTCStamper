using System.Windows;
namespace JTCStamper.App;

public partial class MainWindow
{
    (string Name, string Bottom, bool Plain) savedSettings;
    readonly Dictionary<Guid, string> noteDrafts = [];
    Guid? noteEventId;
    (string Name, string Bottom, bool Plain) EditableSettings => (NameInput.Text, BottomInput.Text, PlainMode.IsChecked == true);
    internal bool HasUnsavedSettings => EditableSettings != savedSettings;
    internal bool HasUnsavedNotes => !string.IsNullOrWhiteSpace(NoteInput.Text) || noteDrafts.Any(x => x.Key != noteEventId && !string.IsNullOrWhiteSpace(x.Value));

    bool ConfirmDiscard(bool settings, bool notes, string action)
    {
        var pending = new List<string>();
        if (settings && HasUnsavedSettings) pending.Add("未保存の印面設定（上段文字・下段文字・プレーン設定）");
        if (notes && HasUnsavedNotes) pending.Add("まだ履歴へ追記していない注釈");
        return pending.Count == 0 || MessageBox.Show(this, string.Join("\n", pending) +
            "があります。破棄して" + action + "しますか？\n保存する場合はキャンセルし、設定の保存または注釈の追記を行ってください。",
            "未保存の入力", MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel) == MessageBoxResult.OK;
    }
    void SelectNoteDraft(Guid? next)
    {
        if (noteEventId is Guid previous)
        {
            if (string.IsNullOrWhiteSpace(NoteInput.Text)) noteDrafts.Remove(previous);
            else noteDrafts[previous] = NoteInput.Text;
        }
        noteEventId = next;
        NoteInput.Text = next is Guid id && noteDrafts.TryGetValue(id, out var draft) ? draft : "";
    }
    internal void ClearNoteDrafts()
    {
        noteDrafts.Clear(); noteEventId = null; NoteInput.Clear();
    }
}
