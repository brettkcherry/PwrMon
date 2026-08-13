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
    /// <summary>Whole samples rather than one series' values: switching series has to redraw the
    /// history that's already on screen, not start collecting it again from empty.</summary>
    private readonly List<PowerSample> _points = new();
    private PowerSample? _last;
    private Estimates? _lastEst;

    /// <summary>How far the window may travel during a press and still count as a click. Covers
    /// the pixel or two of hand tremor between button down and up.</summary>
    private const double ClickSlopPx = 3;

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
        Topmost = s.MiniGraphAlwaysOnTop;
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
        RefreshReadout();   // the series colour comes from the palette too
    }

    public void OnSample(PowerSample s, Estimates est)
    {
        _points.Add(s);
        _last = s;
        _lastEst = est;

        var cutoff = DateTimeOffset.Now.AddSeconds(-AppSettings.Current.MiniGraphWindowSeconds - 5);
        while (_points.Count > 0 && _points[0].Time < cutoff)
            _points.RemoveAt(0);

        RefreshReadout();
        Redraw();
    }

    /// <summary>The value the given series contributes for one sample, or null when this machine
    /// or this moment doesn't have it (no battery, no RAPL driver, off AC for wall input).</summary>
    private static double? Value(PowerSample s, MiniGraphSeries series) => series switch
    {
        MiniGraphSeries.Net => s.HasBattery ? s.NetW : null,
        MiniGraphSeries.Cpu => s.CpuPackageW,
        MiniGraphSeries.Gpu => s.IGpuW,
        MiniGraphSeries.Wall => s.EstWallW,
        MiniGraphSeries.Percent => s.HasBattery ? s.BatteryPercent : null,
        MiniGraphSeries.Load => s.CpuLoadPct,
        MiniGraphSeries.CpuTemp => s.CpuTempC,
        MiniGraphSeries.DriveTemp => s.DriveTempC,
        _ => null,
    };

    private static string Format(MiniGraphSeries series, double v) => series switch
    {
        MiniGraphSeries.Net => UnitFormatter.Power(v, signed: true),
        MiniGraphSeries.Cpu or MiniGraphSeries.Gpu or MiniGraphSeries.Wall => UnitFormatter.Power(v),
        MiniGraphSeries.Percent or MiniGraphSeries.Load => $"{v:F0}%",
        _ => $"{v:F0} °C",
    };

    /// <summary>Net keeps its charge/discharge colouring — that signal is the whole point of it.
    /// Every other series wears the colour it already has on the main history chart.</summary>
    private Brush SeriesBrush(MiniGraphSeries series, PowerSample s)
    {
        var t = ThemeService.Current;
        var hex = series switch
        {
            MiniGraphSeries.Cpu => t.SeriesCpu,
            MiniGraphSeries.Gpu => t.SeriesGpu,
            MiniGraphSeries.Wall => t.SeriesWall,
            MiniGraphSeries.Percent => t.SeriesPct,
            MiniGraphSeries.Load => t.SeriesLoad,
            MiniGraphSeries.CpuTemp => t.Red,
            MiniGraphSeries.DriveTemp => t.Orange,
            _ => null,
        };
        if (hex is not null) return new SolidColorBrush(ThemeService.ParseColor(hex));
        return s.Charging ? ChargeBrush : s.Discharging ? DischargeBrush : IdleBrush;
    }

    private void RefreshReadout()
    {
        if (_last is not { } s || _lastEst is not { } est) return;
        var series = AppSettings.Current.MiniGraphSeries;
        var v = Value(s, series);

        var rateStale = series == MiniGraphSeries.Net && s.HasBattery && (s.Charging || s.Discharging)
                        && UnitFormatter.IsStale(s.RateAge);
        MiniWatts.Text = v is null ? "—"
            : rateStale ? UnitFormatter.Power(v.Value, signed: true, stale: true)
            : Format(series, v.Value);

        var brush = SeriesBrush(series, s);
        MiniWatts.Foreground = brush;
        Spark.Stroke = brush;

        MiniSub.Text = s.Charging ? "charging" : s.Discharging ? "on battery" : s.AcOnline ? "AC" : "";
        MiniPct.Text = s.HasBattery
            ? $"{s.BatteryPercent:F0}%" + (s.Discharging && est.TimeToEmpty is not null ? $" • {UnitFormatter.Duration(est.TimeToEmpty)}" : "")
            : "";
    }

    private void Redraw()
    {
        var wPx = GraphArea.ActualWidth;
        var hPx = GraphArea.ActualHeight;
        if (wPx < 10 || hPx < 10 || _points.Count < 2) return;

        var series = AppSettings.Current.MiniGraphSeries;
        var windowSec = AppSettings.Current.MiniGraphWindowSeconds;
        var now = DateTimeOffset.Now;
        var t0 = now.AddSeconds(-windowSec);

        // seed from the first in-window point, not a fixed (0, 1) sentinel — the old sentinel
        // meant a steady discharge (always < 1) never moved `max` off 1, and a steady charge
        // (always > 0) never moved `min` off 0, so the plot was scaled against an arbitrary
        // fixed baseline instead of the data: real fluctuations got squashed into a sliver of
        // the graph instead of using its full height. This is why the line "meant nothing."
        double? minSeen = null, maxSeen = null;
        foreach (var s in _points)
        {
            if (s.Time < t0 || Value(s, series) is not { } v) continue;
            if (minSeen is null || v < minSeen) minSeen = v;
            if (maxSeen is null || v > maxSeen) maxSeen = v;
        }
        if (minSeen is null || maxSeen is null)
        {
            Spark.Points = new PointCollection();
            MaxLabel.Text = MinLabel.Text = "";
            ZeroLine.Visibility = Visibility.Collapsed;
            return;
        }
        var rawMin = minSeen.Value;
        var rawMax = maxSeen.Value;

        MaxLabel.Text = Format(series, rawMax);
        MinLabel.Text = Format(series, rawMin);

        var span = Math.Max(rawMax - rawMin, 1);
        var min = rawMin - span * 0.08;
        var max = rawMax + span * 0.08;
        span = max - min;

        var pts = new PointCollection();
        foreach (var s in _points)
        {
            if (s.Time < t0 || Value(s, series) is not { } v) continue;
            var x = (s.Time - t0).TotalSeconds / windowSec * wPx;
            var y = hPx - (v - min) / span * hPx;
            pts.Add(new Point(x, y));
        }
        Spark.Points = pts;

        var zeroY = hPx - (0 - min) / span * hPx;
        ZeroLine.X1 = 0; ZeroLine.X2 = wPx;
        ZeroLine.Y1 = ZeroLine.Y2 = zeroY;
        ZeroLine.Visibility = zeroY >= 0 && zeroY <= hPx ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Move on drag, cycle the plotted series on click. <see cref="Window.DragMove"/> is
    /// modal — it returns only once the button comes back up — so there is no click event to
    /// listen for separately: the two are told apart afterwards, by whether the window actually
    /// travelled. Under <see cref="ClickSlopPx"/> it was a click, and the window is put back so
    /// the click can't nudge it by the pixel of tremor that got it there.</summary>
    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed) return;

        var (x0, y0) = (Left, Top);
        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // a click fast enough that the button was already up by the time we got here —
            // nothing was dragged, so fall through and treat it as the click it was
        }

        if (Math.Abs(Left - x0) < ClickSlopPx && Math.Abs(Top - y0) < ClickSlopPx)
        {
            Left = x0;
            Top = y0;
            CycleSeries();
            return;
        }

        AppSettings.Current.MiniGraphX = Left;
        AppSettings.Current.MiniGraphY = Top;
        AppSettings.Save();
    }

    /// <summary>Advances to the next series the user has opted into, skipping any this machine
    /// can't currently measure — cycling onto a permanently empty graph reads as a bug, not as a
    /// sensor that isn't there. Does nothing until at least two series qualify.</summary>
    private void CycleSeries()
    {
        var cycle = AppSettings.Current.MiniGraphCycle
            .Distinct()
            .Where(HasData)
            .OrderBy(x => x)
            .ToList();
        if (cycle.Count < 2) return;

        // IndexOf returns -1 when the current series was un-ticked out of the cycle, which the
        // +1 turns into "start at the beginning" — the right answer for that case too.
        var next = cycle[(cycle.IndexOf(AppSettings.Current.MiniGraphSeries) + 1) % cycle.Count];
        AppSettings.Current.MiniGraphSeries = next;
        AppSettings.Save();
        RefreshReadout();
        Redraw();
    }

    private bool HasData(MiniGraphSeries series) => _points.Any(s => Value(s, series) is not null);

    /// <summary>Grows/shrinks the window directly from an edge/corner thumb — works regardless
    /// of ResizeMode since there's no window chrome to hand this off to. Dragging the west or
    /// north side also has to slide Left/Top so the opposite edge stays put, the way native
    /// edge-drag resize behaves.</summary>
    private void ResizeFromEdge(double dx, double dy, bool west, bool north)
    {
        if (west)
        {
            var newWidth = Math.Max(MinWidth, Width - dx);
            Left += Width - newWidth;
            Width = newWidth;
        }
        else if (dx != 0)
        {
            Width = Math.Max(MinWidth, Width + dx);
        }

        if (north)
        {
            var newHeight = Math.Max(MinHeight, Height - dy);
            Top += Height - newHeight;
            Height = newHeight;
        }
        else if (dy != 0)
        {
            Height = Math.Max(MinHeight, Height + dy);
        }
    }

    private void ResizeN_DragDelta(object sender, DragDeltaEventArgs e) => ResizeFromEdge(0, e.VerticalChange, west: false, north: true);
    private void ResizeS_DragDelta(object sender, DragDeltaEventArgs e) => ResizeFromEdge(0, e.VerticalChange, west: false, north: false);
    private void ResizeW_DragDelta(object sender, DragDeltaEventArgs e) => ResizeFromEdge(e.HorizontalChange, 0, west: true, north: false);
    private void ResizeE_DragDelta(object sender, DragDeltaEventArgs e) => ResizeFromEdge(e.HorizontalChange, 0, west: false, north: false);
    private void ResizeNW_DragDelta(object sender, DragDeltaEventArgs e) => ResizeFromEdge(e.HorizontalChange, e.VerticalChange, west: true, north: true);
    private void ResizeNE_DragDelta(object sender, DragDeltaEventArgs e) => ResizeFromEdge(e.HorizontalChange, e.VerticalChange, west: false, north: true);
    private void ResizeSW_DragDelta(object sender, DragDeltaEventArgs e) => ResizeFromEdge(e.HorizontalChange, e.VerticalChange, west: true, north: false);
    private void ResizeSE_DragDelta(object sender, DragDeltaEventArgs e) => ResizeFromEdge(e.HorizontalChange, e.VerticalChange, west: false, north: false);

    private void ResizeGrip_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        AppSettings.Current.MiniGraphWidth = Width;
        AppSettings.Current.MiniGraphHeight = Height;
        AppSettings.Current.MiniGraphX = Left;
        AppSettings.Current.MiniGraphY = Top;
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
        SyncMenuChecks();
    }

    private void WindowSecs_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tag } && int.TryParse(tag, out var secs))
        {
            AppSettings.Current.MiniGraphWindowSeconds = secs;
            AppSettings.Save();
            Redraw();
        }
        SyncMenuChecks();
    }

    private void ContextMenu_Opened(object sender, RoutedEventArgs e) => SyncMenuChecks();

    /// <summary>Clicking a checkable item toggles its own tick before the handler runs, so every
    /// handler re-syncs from settings rather than trusting what the click left behind.</summary>
    private void SyncMenuChecks()
    {
        var s = AppSettings.Current;
        MenuOpacity100.IsChecked = s.MiniGraphOpacityPct == 100;
        MenuOpacity85.IsChecked = s.MiniGraphOpacityPct == 85;
        MenuOpacity70.IsChecked = s.MiniGraphOpacityPct == 70;
        MenuOpacity50.IsChecked = s.MiniGraphOpacityPct == 50;
        MenuWin60.IsChecked = s.MiniGraphWindowSeconds == 60;
        MenuWin120.IsChecked = s.MiniGraphWindowSeconds == 120;
        MenuWin300.IsChecked = s.MiniGraphWindowSeconds == 300;
        MenuWin900.IsChecked = s.MiniGraphWindowSeconds == 900;
        MenuWin3600.IsChecked = s.MiniGraphWindowSeconds == 3600;
        MenuWin86400.IsChecked = s.MiniGraphWindowSeconds == 86400;
        MenuAlwaysOnTop.IsChecked = s.MiniGraphAlwaysOnTop;

        MenuCycleNet.IsChecked = s.MiniGraphCycle.Contains(MiniGraphSeries.Net);
        MenuCycleCpu.IsChecked = s.MiniGraphCycle.Contains(MiniGraphSeries.Cpu);
        MenuCycleGpu.IsChecked = s.MiniGraphCycle.Contains(MiniGraphSeries.Gpu);
        MenuCycleWall.IsChecked = s.MiniGraphCycle.Contains(MiniGraphSeries.Wall);
        MenuCyclePercent.IsChecked = s.MiniGraphCycle.Contains(MiniGraphSeries.Percent);
        MenuCycleLoad.IsChecked = s.MiniGraphCycle.Contains(MiniGraphSeries.Load);
        MenuCycleCpuTemp.IsChecked = s.MiniGraphCycle.Contains(MiniGraphSeries.CpuTemp);
        MenuCycleDriveTemp.IsChecked = s.MiniGraphCycle.Contains(MiniGraphSeries.DriveTemp);
    }

    /// <summary>Opts a series into or out of the click-cycle. Ticking one also jumps straight to
    /// it — picking a series from the menu should show it, not just enrol it for later.</summary>
    private void Cycle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string tag } || !Enum.TryParse<MiniGraphSeries>(tag, out var series))
            return;

        var cycle = AppSettings.Current.MiniGraphCycle;
        if (cycle.Contains(series))
        {
            cycle.Remove(series);
        }
        else
        {
            cycle.Add(series);
            if (HasData(series))
            {
                AppSettings.Current.MiniGraphSeries = series;
                RefreshReadout();
                Redraw();
            }
        }
        AppSettings.Save();
        SyncMenuChecks();
    }

    private void AlwaysOnTop_Click(object sender, RoutedEventArgs e)
    {
        App.Current.ToggleMiniGraphAlwaysOnTop();
        SyncMenuChecks();
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
    /// window can't be right-clicked anymore, so the only way back is the tray menu's "Mini graph
    /// click-through" item, which calls this directly (<see cref="App.SetMiniGraphClickThrough"/>).</summary>
    public void SetClickThrough(bool enabled)
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
