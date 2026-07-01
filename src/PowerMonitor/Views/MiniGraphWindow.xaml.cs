using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using PowerMonitor.Models;
using PowerMonitor.Services;

namespace PowerMonitor.Views;

/// <summary>
/// Borderless always-on-top sparkline of recent net battery power (CPU package power on
/// desktops). Drag to move; position/opacity persist. Optional click-through mode.
/// </summary>
public partial class MiniGraphWindow : Window
{
    private readonly List<(DateTimeOffset t, double w)> _points = new();

    private static readonly Brush ChargeBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x3F, 0xB9, 0x50));
    private static readonly Brush DischargeBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF0, 0x88, 0x3E));
    private static readonly Brush IdleBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x8B, 0x93, 0xA7));

    public MiniGraphWindow()
    {
        InitializeComponent();

        var s = AppSettings.Current;
        Opacity = s.MiniGraphOpacityPct / 100.0;
        if (!double.IsNaN(s.MiniGraphX) && !double.IsNaN(s.MiniGraphY))
        {
            // only restore a position that is still on a connected screen
            var virtualBounds = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                                         SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
            if (virtualBounds.Contains(new Point(s.MiniGraphX + 40, s.MiniGraphY + 20)))
            {
                Left = s.MiniGraphX;
                Top = s.MiniGraphY;
            }
        }
        else
        {
            Left = SystemParameters.WorkArea.Right - Width - 16;
            Top = SystemParameters.WorkArea.Top + 16;
        }

        SourceInitialized += (_, _) =>
        {
            if (AppSettings.Current.MiniGraphClickThrough) SetClickThrough(true);
        };
        GraphArea.SizeChanged += (_, _) => Redraw();
    }

    public void OnSample(PowerSample s, Estimates est)
    {
        var w = s.HasBattery ? s.NetW : (s.CpuPackageW ?? double.NaN);
        if (!double.IsNaN(w)) _points.Add((s.Time, w));

        var cutoff = DateTimeOffset.Now.AddSeconds(-AppSettings.Current.MiniGraphWindowSeconds - 5);
        while (_points.Count > 0 && _points[0].t < cutoff)
            _points.RemoveAt(0);

        MiniWatts.Text = UnitFormatter.Power(w is double.NaN ? 0 : w, signed: s.HasBattery);
        var brush = s.Charging ? ChargeBrush : s.Discharging ? DischargeBrush : IdleBrush;
        MiniWatts.Foreground = brush;
        Spark.Stroke = brush;

        MiniSub.Text = s.Charging ? "charging" : s.Discharging ? "on battery" : s.AcOnline ? "AC" : "";
        MiniPct.Text = s.HasBattery
            ? $"{s.BatteryPercent:F0}%" + (s.Discharging && est.TimeToEmpty is not null ? $" • {UnitFormatter.Duration(est.TimeToEmpty)}" : "")
            : "";

        Redraw();
    }

    private void Redraw()
    {
        var wPx = GraphArea.ActualWidth;
        var hPx = GraphArea.ActualHeight;
        if (wPx < 10 || hPx < 10 || _points.Count < 2) return;

        var windowSec = AppSettings.Current.MiniGraphWindowSeconds;
        var now = DateTimeOffset.Now;
        var t0 = now.AddSeconds(-windowSec);

        double min = 0, max = 1;
        foreach (var (t, v) in _points)
        {
            if (t < t0) continue;
            if (v < min) min = v;
            if (v > max) max = v;
        }
        var span = Math.Max(max - min, 1);
        min -= span * 0.08;
        max += span * 0.08;
        span = max - min;

        var pts = new PointCollection();
        foreach (var (t, v) in _points)
        {
            if (t < t0) continue;
            var x = (t - t0).TotalSeconds / windowSec * wPx;
            var y = hPx - (v - min) / span * hPx;
            pts.Add(new Point(x, y));
        }
        Spark.Points = pts;

        var zeroY = hPx - (0 - min) / span * hPx;
        ZeroLine.X1 = 0; ZeroLine.X2 = wPx;
        ZeroLine.Y1 = ZeroLine.Y2 = zeroY;
        ZeroLine.Visibility = zeroY >= 0 && zeroY <= hPx ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
            AppSettings.Current.MiniGraphX = Left;
            AppSettings.Current.MiniGraphY = Top;
            AppSettings.Save();
        }
    }

    private void OpenDashboard_Click(object sender, RoutedEventArgs e) => App.Current.ShowDashboard();

    private void Opacity_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tag } && int.TryParse(tag, out var pct))
        {
            Opacity = pct / 100.0;
            AppSettings.Current.MiniGraphOpacityPct = pct;
            AppSettings.Save();
        }
    }

    private void WindowSecs_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tag } && int.TryParse(tag, out var secs))
        {
            AppSettings.Current.MiniGraphWindowSeconds = secs;
            AppSettings.Save();
            Redraw();
        }
    }

    private void ClickThrough_Click(object sender, RoutedEventArgs e)
    {
        AppSettings.Current.MiniGraphClickThrough = true;
        AppSettings.Save();
        SetClickThrough(true);
    }

    private void Hide_Click(object sender, RoutedEventArgs e) => App.Current.ToggleMiniGraph();

    /// <summary>WS_EX_TRANSPARENT: clicks pass through to whatever is underneath. Once enabled the
    /// window can't be right-clicked anymore — the tray menu's mini-graph toggle is the way back.</summary>
    private void SetClickThrough(bool enabled)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        var style = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, enabled ? style | WS_EX_TRANSPARENT : style & ~WS_EX_TRANSPARENT);
    }

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
}
