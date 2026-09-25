using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using JTCStamper.Core;

namespace JTCStamper.App;

internal sealed class PassphraseDialog : Window
{
    readonly PasswordBox first = new() { MaxLength = 1024, Margin = new Thickness(0, 4, 0, 12) };
    readonly PasswordBox second = new() { MaxLength = 1024, Margin = new Thickness(0, 4, 0, 12) };
    readonly TextBlock error = new() { TextWrapping = TextWrapping.Wrap };
    readonly bool confirm;
    internal string Passphrase => first.Password;
    internal void Clear() { first.Clear(); second.Clear(); }

    void ShowError(string text)
    {
        error.Text = text;
        UIElementAutomationPeer.FromElement(error)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    internal PassphraseDialog(Window owner, bool confirm)
    {
        this.confirm = confirm;
        Owner = owner; Title = confirm ? "移行用バックアップのパスフレーズ" : "移行用バックアップを復元";
        Width = 460; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "SolidBackgroundFillColorBaseBrush");
        SetResourceReference(ForegroundProperty, "TextFillColorPrimaryBrush");
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock
        {
            Text = confirm ? "別のPCやWindowsユーザーへ移行できます。パスフレーズを忘れると復元できません。バックアップとは別の場所で安全に保管してください。"
                : "保存時のパスフレーズを入力してください。忘れた場合、リセットや復元はできません。",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12)
        });
        panel.Children.Add(new Label { Content = "パスフレーズ（12文字以上）", Target = first });
        AutomationProperties.SetName(first, "移行用パスフレーズ"); panel.Children.Add(first);
        if (confirm)
        {
            panel.Children.Add(new Label { Content = "確認のため再入力", Target = second });
            AutomationProperties.SetName(second, "移行用パスフレーズの確認"); panel.Children.Add(second);
        }
        AutomationProperties.SetLiveSetting(error, AutomationLiveSetting.Assertive);
        panel.Children.Add(error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var cancel = new Button { Content = "取消", IsCancel = true, MinWidth = 80 };
        var ok = new Button { Content = "続ける", IsDefault = true, MinWidth = 80, Margin = new Thickness(8, 0, 0, 0) };
        ok.Click += (_, _) =>
        {
            try
            {
                PortableBackup.ValidatePassphrase(first.Password);
                if (this.confirm && first.Password != second.Password) { ShowError("パスフレーズが一致しません。"); return; }
                DialogResult = true;
            }
            catch (ArgumentException ex) { ShowError(ex.Message); }
        };
        buttons.Children.Add(cancel); buttons.Children.Add(ok); panel.Children.Add(buttons);
        Content = panel;
        Loaded += (_, _) => first.Focus();
    }
}
