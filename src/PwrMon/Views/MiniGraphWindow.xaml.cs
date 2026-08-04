using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using PwrMon.Models;
using PwrMon.Services;

namespace PwrMon.Views;

/// <summary>
/// Borderless always-on-top sparkline of recent net battery power (CPU package power on
/// desktops). Drag to move; position/opacity persist. Optional click-through mode.
/// </summary>
public partial class MiniGraphWindow : Window
{
    private readonly List<(DateTimeOffset t, double w)> _points = new();

    private Brush ChargeBrush => (Brush)FindResource("GreenBrush");
    private Brush DischargeBrush => (Brush)FindResource("OrangeBrush");
    private Brush IdleBrush => (Brush)FindResource("TextDimBrush");

    public MiniGraphWindow()
    {
        InitializeComponent();
        ApplyTheme(ThemeService.Current);
        ThemeService.Changed += ApplyTheme;
        Closed += (_, _) => ThemeService.Changed -= ApplyTheme;

        var s = AppSettings.Current;
        Opacity = s.MiniGraphOpacityPct / 100.0;
        Width = Math.Max(MinWidth, s.MiniGraphWidth);
        Height = Math.Max(MinHeight, s.MiniGraphHeight);
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

    private void ApplyTheme(ThemePalette t)
    {
        RootBorder.Background = new SolidColorBrush(ThemeService.ParseColor(t.MiniBg));
        RootBorder.BorderBrush = new SolidColorBrush(ThemeService.ParseColor(t.CardBorder));
        ZeroLine.Stroke = new SolidColorBrush(ThemeService.ParseColor(t.ChartGrid));
    }

    public void OnSample(PowerSample s, Estimates est)
    {
        var w = s.HasBattery ? s.NetW : (s.CpuPackageW ?? double.NaN);
        if (!double.IsNaN(w)) _points.Add((s.Time, w));

        var cutoff = DateTimeOffset.Now.AddSeconds(-AppSettings.Current.MiniGraphWindowSeconds - 5);
        while (_points.Count > 0 && _points[0].t < cutoff)
            _points.RemoveAt(0);

        var rateStale = s.HasBattery && (s.Charging || s.Discharging) && UnitFormatter.IsStale(s.RateAge);
        MiniWatts.Text = UnitFormatter.Power(w is double.NaN ? 0 : w, signed: s.HasBattery, stale: rateStale);
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

        // seed from the first in-window point, not a fixed (0, 1) sentinel — the old sentinel
        // meant a steady discharge (always < 1) never moved `max` off 1, and a steady charge
        // (always > 0) never moved `min` off 0, so the plot was scaled against an arbitrary
        // fixed baseline instead of the data: real fluctuations got squashed into a sliver of
        // the graph instead of using its full height. This is why the line "meant nothing."
        double? minSeen = null, maxSeen = null;
        foreach (var (t, v) in _points)
        {
            if (t < t0) continue;
            if (minSeen is null || v < minSeen) minSeen = v;
            if (maxSeen is null || v > maxSeen) maxSeen = v;
        }
        if (minSeen is null || maxSeen is null) return;
        var rawMin = minSeen.Value;
        var rawMax = maxSeen.Value;

        MaxLabel.Text = UnitFormatter.Power(rawMax, signed: true);
        MinLabel.Text = UnitFormatter.Power(rawMin, signed: true);

        var span = Math.Max(rawMax - rawMin, 1);
        var min = rawMin - span * 0.08;
        var max = rawMax + span * 0.08;
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

    /// <summary>Grows/shrinks the window directly from the corner grip — works regardless of
    /// ResizeMode since there's no window chrome to hand this off to.</summary>
    private void ResizeGrip_DragDelta(object sender, DragDeltaEventArgs e)
    {
        Width = Math.Max(MinWidth, Width + e.HorizontalChange);
        Height = Math.Max(MinHeight, Height + e.VerticalChange);
    }

    private void ResizeGrip_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        AppSettings.Current.MiniGraphWidth = Width;
        AppSettings.Current.MiniGraphHeight = Height;
        AppSettings.Save();
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

    private void Exit_Click(object sender, RoutedEventArgs e) => App.Current.ExitApp();

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
