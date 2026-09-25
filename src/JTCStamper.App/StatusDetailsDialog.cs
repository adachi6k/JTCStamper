using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace JTCStamper.App;

// A snapshot: keep a long error readable even if a background operation updates the status bar.
internal sealed class StatusDetailsDialog : Window
{
    internal TextBox Message { get; }

    internal StatusDetailsDialog(Window owner, string message)
    {
        Owner = owner; Title = "現在の状態";
        Width = 560; Height = 320; MinWidth = 320; MinHeight = 200;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
        FontFamily = owner.FontFamily; FontSize = owner.FontSize;
        SetResourceReference(BackgroundProperty, "SolidBackgroundFillColorBaseBrush");
        SetResourceReference(ForegroundProperty, "TextFillColorPrimaryBrush");
        var panel = new DockPanel { Margin = new Thickness(16) };
        var close = new Button
        {
            Content = "閉じる", IsCancel = true, IsDefault = true, MinWidth = 80,
            HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0)
        };
        close.Click += (_, _) => Close();
        DockPanel.SetDock(close, Dock.Bottom); panel.Children.Add(close);
        Message = new TextBox
        {
            Text = string.IsNullOrEmpty(message) ? "現在、状態メッセージはありません。" : message,
            IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        AutomationProperties.SetName(Message, "状態メッセージの全文");
        panel.Children.Add(Message); Content = panel;
        Loaded += (_, _) => { Message.Focus(); Message.CaretIndex = 0; };
    }
}
