using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Interop;
using System.Windows.Media;

namespace JTCStamper.App;

public partial class MainWindow
{
    HwndSource? captionSource;
    bool captionPressed;
    const int HitMaximize = 9;

    void InitializeCaption()
    {
        SourceInitialized += (_, _) =>
        {
            captionSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            captionSource?.AddHook(CaptionMessages);
        };
        StateChanged += (_, _) => UpdateCaptionState();
        Closed += (_, _) => { captionSource?.RemoveHook(CaptionMessages); captionSource = null; };
        UpdateCaptionState();
    }
    void UpdateCaptionState()
    {
        bool restored = WindowState == WindowState.Maximized;
        MaximizeGlyph.Data = Geometry.Parse(restored ? "M2,0 H10 V8 M0,2 H8 V10 H0 Z" : "M0,0 H10 V10 H0 Z");
        string label = restored ? "元のサイズに戻す" : "最大化";
        MaximizeButton.ToolTip = label;
        AutomationProperties.SetName(MaximizeButton, label);
    }
    void MinimizeClick(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);
    void MaximizeClick(object sender, RoutedEventArgs e) => ToggleMaximize();
    void CloseCaptionClick(object sender, RoutedEventArgs e) => SystemCommands.CloseWindow(this);
    void ToggleMaximize()
    {
        if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this);
        else SystemCommands.MaximizeWindow(this);
    }
    bool OverMaximize(Point screen)
    {
        if (!MaximizeButton.IsVisible || !MaximizeButton.IsEnabled) return false;
        var local = MaximizeButton.PointFromScreen(screen);
        return new Rect(MaximizeButton.RenderSize).Contains(local);
    }
    bool CursorOverMaximize() => GetCursorPos(out var p) && OverMaximize(new Point(p.X, p.Y));
    static Point ScreenPoint(IntPtr packed)
    {
        long value = packed.ToInt64();
        return new Point(unchecked((short)(value & 0xFFFF)), unchecked((short)((value >> 16) & 0xFFFF)));
    }
    IntPtr CaptionMessages(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (message)
        {
            case 0x0084: // WM_NCHITTEST: expose the actual maximize rectangle to Windows 11 Snap.
                if (OverMaximize(ScreenPoint(lParam))) { handled = true; return new IntPtr(HitMaximize); }
                break;
            case 0x00A0: // WM_NCMOUSEMOVE: let Windows continue processing Snap hover.
                if (wParam.ToInt64() == HitMaximize)
                {
                    if (!captionPressed) MaximizeButton.Tag = "CaptionHover";
                    var tracking = new MouseTracking { Size = (uint)Marshal.SizeOf<MouseTracking>(), Flags = 0x12, Window = hwnd };
                    TrackMouseEvent(ref tracking); // TME_LEAVE | TME_NONCLIENT
                }
                else if (!captionPressed) MaximizeButton.Tag = null;
                break;
            case 0x02A2: // WM_NCMOUSELEAVE
                if (!captionPressed) MaximizeButton.Tag = null;
                break;
            case 0x00A1: // WM_NCLBUTTONDOWN
            case 0x00A3: // WM_NCLBUTTONDBLCLK
                if (wParam.ToInt64() == HitMaximize)
                {
                    captionPressed = true;
                    MaximizeButton.Tag = "CaptionPressed";
                    SetCapture(hwnd);
                    handled = true;
                }
                break;
            case 0x0200: // WM_MOUSEMOVE while captured; leaving the button cancels the visual press.
                if (captionPressed) MaximizeButton.Tag = CursorOverMaximize() ? "CaptionPressed" : null;
                break;
            case 0x0202: // WM_LBUTTONUP after non-client capture
            case 0x00A2: // WM_NCLBUTTONUP
                if (captionPressed)
                {
                    bool activate = CursorOverMaximize();
                    captionPressed = false;
                    ReleaseCapture();
                    MaximizeButton.Tag = null;
                    handled = true;
                    if (activate) ToggleMaximize();
                }
                break;
            case 0x0215: // WM_CAPTURECHANGED
                captionPressed = false; MaximizeButton.Tag = null;
                break;
            case 0x001F: // WM_CANCELMODE
                if (captionPressed) { captionPressed = false; ReleaseCapture(); MaximizeButton.Tag = null; }
                break;
        }
        return IntPtr.Zero;
    }
    [StructLayout(LayoutKind.Sequential)] struct CursorPoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct MouseTracking { public uint Size, Flags; public IntPtr Window; public uint HoverTime; }
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] static extern bool GetCursorPos(out CursorPoint point);
    [DllImport("user32.dll")] static extern IntPtr SetCapture(IntPtr window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] static extern bool ReleaseCapture();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] static extern bool TrackMouseEvent(ref MouseTracking tracking);
}
