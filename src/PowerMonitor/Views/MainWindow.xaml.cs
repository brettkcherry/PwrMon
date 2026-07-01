using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using PowerMonitor.Models;
using PowerMonitor.Services;
using ScottPlot;
using ScottPlot.Plottables;

namespace PowerMonitor.Views;

public partial class MainWindow : Window
{
    private static readonly ScottPlot.Color NetColor = ScottPlot.Color.FromHex("#F5B62E");
    private static readonly ScottPlot.Color CpuColor = ScottPlot.Color.FromHex("#58A6FF");
    private static readonly ScottPlot.Color GpuColor = ScottPlot.Color.FromHex("#BC8CFF");
    private static readonly ScottPlot.Color PctColor = ScottPlot.Color.FromHex("#3FB950");
    private static readonly ScottPlot.Color LoadColor = ScottPlot.Color.FromHex("#8B93A7");

    private DataLogger _netLog = null!, _cpuLog = null!, _gpuLog = null!, _pctLog = null!, _loadLog = null!;
    private VerticalLine _hoverLine = null!;

    // parallel history kept locally for hover readout + Y autoscale (single owner: UI thread)
    private readonly List<double> _times = new(), _net = new(), _cpu = new(), _gpu = new(), _pct = new(), _load = new();

    private bool _initializing = true;
    private bool _live = true;
    private SensorTier _lastTier = SensorTier.Probing;
    private BatteryStaticInfo? _staticInfo;
    private PowerSample? _lastSample;

    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => EnableDarkTitleBar(ThemeService.Current.IsDark);
        ThemeService.Changed += OnThemeChanged;
        Closed += (_, _) => ThemeService.Changed -= OnThemeChanged;

        Width = AppSettings.Current.WindowWidth;
        Height = AppSettings.Current.WindowHeight;

        SetupChart();
        SetupToolbar();
        BackfillHistoryAsync();
        LoadStaticInfoAsync();

        Chart.MouseMove += Chart_MouseMove;
        Chart.MouseLeave += (_, _) => { _hoverLine.IsVisible = false; HoverReadout.Text = " "; };
        Chart.PreviewMouseDown += (_, _) => SetLive(false);
        Chart.MouseWheel += (_, _) => SetLive(false);

        Closing += MainWindow_Closing;
        _initializing = false;
    }

    // ─────────────────────────── chart setup ───────────────────────────

    private HorizontalLine _zeroLine = null!;

    private void SetupChart()
    {
        var plot = Chart.Plot;
        plot.Legend.IsVisible = false;
        plot.Axes.DateTimeTicksBottom();

        _netLog = NewLogger(plot, NetColor, 1.8f);
        _cpuLog = NewLogger(plot, CpuColor, 1.4f);
        _gpuLog = NewLogger(plot, GpuColor, 1.4f);
        _pctLog = NewLogger(plot, PctColor, 1.4f);
        _loadLog = NewLogger(plot, LoadColor, 1.0f);
        _pctLog.Axes.YAxis = plot.Axes.Right;
        _loadLog.Axes.YAxis = plot.Axes.Right;

        _zeroLine = plot.Add.HorizontalLine(0);
        _zeroLine.LineWidth = 1;

        _hoverLine = plot.Add.VerticalLine(0);
        _hoverLine.LineWidth = 1;
        _hoverLine.IsVisible = false;

        plot.Axes.SetLimitsY(0, 105, plot.Axes.Right);

        ApplyChartTheme(ThemeService.Current);
    }

    private void OnThemeChanged(ThemePalette t)
    {
        ApplyChartTheme(t);
        EnableDarkTitleBar(t.IsDark);
        Chart.Refresh();
    }

    private void ApplyChartTheme(ThemePalette t)
    {
        var plot = Chart.Plot;
        plot.FigureBackground.Color = ScottPlot.Color.FromHex(t.ChartFigure);
        plot.DataBackground.Color = ScottPlot.Color.FromHex(t.ChartData);
        plot.Axes.Color(ScottPlot.Color.FromHex(t.TextDim));
        plot.Grid.MajorLineColor = ScottPlot.Color.FromHex(t.ChartGrid);
        _netLog.Color = ScottPlot.Color.FromHex(t.SeriesNet);
        _cpuLog.Color = ScottPlot.Color.FromHex(t.SeriesCpu);
        _gpuLog.Color = ScottPlot.Color.FromHex(t.SeriesGpu);
        _pctLog.Color = ScottPlot.Color.FromHex(t.SeriesPct);
        _loadLog.Color = ScottPlot.Color.FromHex(t.SeriesLoad);
        _zeroLine.Color = ScottPlot.Color.FromHex(t.ChartGrid).Lighten(0.2);
        _hoverLine.Color = ScottPlot.Color.FromHex(t.TextDim).WithAlpha(150);
        ChkGpu.Foreground = new System.Windows.Media.SolidColorBrush(ThemeService.ParseColor(t.SeriesGpu));
    }

    private static DataLogger NewLogger(Plot plot, ScottPlot.Color color, float width)
    {
        var logger = plot.Add.DataLogger();
        logger.Color = color;
        logger.LineWidth = width;
        logger.ManageAxisLimits = false;
        return logger;
    }

    private void SetupToolbar()
    {
        foreach (var v in new[] { 0.5, 1.0, 2.0, 5.0 })
            StatusInterval.Items.Add($"{v:0.#} s");
        StatusInterval.SelectedIndex = AppSettings.Current.SamplingIntervalSeconds switch
        {
            <= 0.5 => 0, <= 1 => 1, <= 2 => 2, _ => 3,
        };

        StatusPowerUnit.Items.Add("W");
        StatusPowerUnit.Items.Add("mW");
        StatusPowerUnit.SelectedIndex = AppSettings.Current.PowerUnit == PowerUnit.Watts ? 0 : 1;

        StatusEnergyUnit.Items.Add("Wh");
        StatusEnergyUnit.Items.Add("mAh");
        StatusEnergyUnit.SelectedIndex = AppSettings.Current.EnergyUnit == EnergyUnit.WattHours ? 0 : 1;

        ChkNet.IsChecked = AppSettings.Current.ChartShowNet;
        ChkCpu.IsChecked = AppSettings.Current.ChartShowCpu;
        ChkGpu.IsChecked = AppSettings.Current.ChartShowGpu;
        ChkPct.IsChecked = AppSettings.Current.ChartShowPercent;
        ChkLoad.IsChecked = AppSettings.Current.ChartShowCpuLoad;

        // the visual tree doesn't exist until Loaded, so the saved range pill is checked there
        Loaded += (_, _) =>
        {
            foreach (var rb in FindRadios())
                if (rb.Tag is string tag && int.TryParse(tag, out var m) && m == AppSettings.Current.ChartRangeMinutes)
                    rb.IsChecked = true;
        };
    }

    private IEnumerable<RadioButton> FindRadios()
    {
        var result = new List<RadioButton>();
        void Walk(DependencyObject d)
        {
            for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(d); i++)
            {
                var c = System.Windows.Media.VisualTreeHelper.GetChild(d, i);
                if (c is RadioButton rb && rb.GroupName == "Range") result.Add(rb);
                Walk(c);
            }
        }
        Walk(this);
        return result;
    }

    private void BackfillHistoryAsync()
    {
        Task.Run(() =>
        {
            var app = App.Current;
            var samples = app.History.LoadRecent(48);
            var events = app.History.LoadRecentEvents(48);
            Dispatcher.BeginInvoke(() =>
            {
                foreach (var s in samples) AppendToChart(s);
                foreach (var ev in events) AddEventMarker(ev);
                Log.Info($"backfilled {samples.Count} samples, {events.Count} events");
                UpdateAxes();
                Chart.Refresh();
            });
        });
    }

    private void LoadStaticInfoAsync()
    {
        Task.Run(() =>
        {
            var info = App.Current.Battery.ReadStatic();
            Dispatcher.BeginInvoke(() =>
            {
                _staticInfo = info;
                HpWear.Text = $"{info.WearPct:F1}%";
                HpWear.Foreground = info.WearPct > 25 ? (System.Windows.Media.Brush)FindResource("RedBrush")
                    : info.WearPct > 12 ? (System.Windows.Media.Brush)FindResource("OrangeBrush")
                    : (System.Windows.Media.Brush)FindResource("GreenBrush");
                HpDesign.Text = $"{info.DesignWh:F1} Wh";
                HpCycles.Text = info.CycleCount?.ToString() ?? "—";
                HpChem.Text = info.Chemistry.Length > 0 ? info.Chemistry : "—";
                HpChem.ToolTip = $"{info.Manufacturer} {info.DeviceName}".Trim();
            });
        });
    }

    // ─────────────────────────── live updates ───────────────────────────

    public void OnSample(PowerSample s, SessionStats stats, Estimates est)
    {
        _lastSample = s;
        UpdateHero(s, est);
        UpdateCards(s, stats, est);
        UpdateTier();

        AppendToChart(s);
        if (IsVisible && _live)
        {
            UpdateAxes();
            Chart.Refresh();
        }
    }

    private void UpdateHero(PowerSample s, Estimates est)
    {
        string state;
        System.Windows.Media.Brush brush;
        if (!s.HasBattery) { state = "NO BATTERY — SILICON ONLY"; brush = (System.Windows.Media.Brush)FindResource("TextDimBrush"); }
        else if (s.Charging) { state = "CHARGING"; brush = (System.Windows.Media.Brush)FindResource("GreenBrush"); }
        else if (s.Discharging) { state = "DISCHARGING"; brush = (System.Windows.Media.Brush)FindResource("OrangeBrush"); }
        else if (s.AcOnline) { state = s.BatteryPercent > 98 ? "PLUGGED IN — FULL" : "PLUGGED IN — IDLE"; brush = (System.Windows.Media.Brush)FindResource("BlueBrush"); }
        else { state = "IDLE"; brush = (System.Windows.Media.Brush)FindResource("TextDimBrush"); }

        HeroState.Text = state;
        HeroState.Foreground = brush;

        if (s.HasBattery)
        {
            HeroWatts.Text = UnitFormatter.Power(s.NetW, signed: true);
            HeroWatts.Foreground = brush;
            var sysNote = s.AcOnline && est.EstSystemW is double es2 ? $" • system ≈ {UnitFormatter.Power(es2)}" : "";
            HeroSub.Text = $"{UnitFormatter.Energy(s.RemainingWh, s.VoltageV)} of {UnitFormatter.Energy(s.FullChargeWh, s.VoltageV)} • {s.VoltageV:F2} V • {(s.AcOnline ? "on AC power" : "on battery")}{sysNote}";
            HeroPercent.Text = $"{s.BatteryPercent:F1}%";
            HeroEta.Text = s.Charging ? $"full in {UnitFormatter.Duration(est.TimeToFull)}"
                         : s.Discharging ? $"{UnitFormatter.Duration(est.TimeToEmpty)} remaining"
                         : " ";
        }
        else
        {
            HeroWatts.Text = s.CpuPackageW is double cw ? UnitFormatter.Power(cw) : "— W";
            HeroSub.Text = "no battery detected — showing CPU/iGPU silicon power";
            HeroPercent.Text = "";
            HeroEta.Text = "";
        }
    }

    private void UpdateCards(PowerSample s, SessionStats stats, Estimates est)
    {
        FlowIn.Text = s.Charging ? UnitFormatter.Power(s.ChargeRateW) : "—";
        FlowOut.Text = s.Discharging ? UnitFormatter.Power(s.DischargeRateW) : "—";
        FlowNet.Text = UnitFormatter.Power(s.NetW, signed: true);
        FlowCurrent.Text = s.HasBattery && Math.Abs(s.CurrentA) > 0.005 ? $"{s.CurrentA:+0.00;-0.00} A" : "—";

        EstEmpty.Text = UnitFormatter.Duration(est.TimeToEmpty);
        EstFull.Text = UnitFormatter.Duration(est.TimeToFull);
        EstDischargeRate.Text = est.SmoothedDischargeW > 0.1 ? UnitFormatter.Power(est.SmoothedDischargeW) : "—";
        EstChargeRate.Text = est.SmoothedChargeW > 0.1 ? UnitFormatter.Power(est.SmoothedChargeW) : "—";

        CpuPkg.Text = s.CpuPackageW is double p ? UnitFormatter.Power(p) : "🔒";
        CpuCoresPlatform.Text = s.CpuCoresW is double c
            ? $"{UnitFormatter.Power(c)} / {(s.CpuPlatformW is double pf ? UnitFormatter.Power(pf) : "—")}"
            : "—";
        CpuLoad.Text = s.CpuLoadPct is double l ? $"{l:F0}%" : "—";
        CpuTemp.Text = s.CpuTempC is double t ? $"{t:F0} °C" : "—";

        GpuPower.Text = s.IGpuW is double g ? UnitFormatter.Power(g) : "🔒";
        GpuLoad.Text = s.GpuLoadPct is double gl ? $"{gl:F0}%" : "—";
        GpuClock.Text = s.GpuClockMhz is double gc ? $"{gc:F0} MHz" : "—";

        BatRemaining.Text = s.HasBattery ? UnitFormatter.Energy(s.RemainingWh, s.VoltageV) : "—";
        BatFull.Text = s.HasBattery ? UnitFormatter.Energy(s.FullChargeWh, s.VoltageV) : "—";
        BatVoltage.Text = s.HasBattery ? $"{s.VoltageV:F2} V" : "—";
        BatState.Text = !s.HasBattery ? "none" : s.Charging ? "charging" : s.Discharging ? "discharging" : "idle";

        BudSystem.Text = est.EstSystemW is double sysW
            ? (est.IsSystemEstimate ? "≈ " : "") + UnitFormatter.Power(sysW)
            : "—";
        BudWall.Text = est.EstWallW is double wallW ? "≈ " + UnitFormatter.Power(wallW) : "—";
        BudSilicon.Text = s.CpuPackageW is double pkgW ? UnitFormatter.Power(pkgW) : "—";
        BudBaseline.Text = !double.IsNaN(est.LearnedBaselineW)
            ? "≈ " + UnitFormatter.Power(est.LearnedBaselineW)
            : "unplug to learn";

        SesEnergy.Text = $"{stats.EnergyOutWh:F1} / {stats.EnergyInWh:F1} Wh";
        SesAvg.Text = stats.AvgDischargeW > 0.1 ? UnitFormatter.Power(stats.AvgDischargeW) : "—";
        SesPeak.Text = stats.PeakDischargeW > 0.1
            ? $"{UnitFormatter.Power(stats.PeakDischargeW)} @ {stats.PeakDischargeTime:HH:mm}"
            : "—";
        SesOnBattery.Text = stats.TimeOnBattery.TotalSeconds > 5
            ? $"{(int)stats.TimeOnBattery.TotalHours}:{stats.TimeOnBattery.Minutes:D2}:{stats.TimeOnBattery.Seconds:D2}"
            : "—";
    }

    private void UpdateTier()
    {
        var tier = App.Current.Hardware.Tier;
        if (tier == _lastTier) return;
        _lastTier = tier;

        switch (tier)
        {
            case SensorTier.Full:
                Banner.Visibility = Visibility.Collapsed;
                StatusTier.Text = "⚡ full silicon telemetry";
                break;
            case SensorTier.EmiOnly:
                Banner.Visibility = Visibility.Collapsed;
                StatusTier.Text = "⚡ CPU/iGPU watts via Windows EMI — admin+PawnIO adds temps";
                break;
            case SensorTier.NeedsAdmin:
                Banner.Visibility = Visibility.Visible;
                BannerText.Text = "CPU and iGPU power sensors are locked for non-administrator processes. " +
                                  "Battery telemetry is unaffected.";
                BannerBtn1.Content = "Restart as admin";
                BannerBtn2.Visibility = Visibility.Collapsed;
                StatusTier.Text = "🔒 CPU/iGPU watts need admin";
                break;
            case SensorTier.DriverBlocked:
                Banner.Visibility = Visibility.Visible;
                BannerText.Text = "Windows Memory Integrity blocks the legacy MSR driver, so CPU/iGPU wattage can't be read. " +
                                  "Install PawnIO (a signed, Memory-Integrity-compatible sensor driver), then click Re-detect.";
                BannerBtn1.Content = "Get PawnIO";
                BannerBtn2.Content = "Re-detect";
                BannerBtn2.Visibility = Visibility.Visible;
                StatusTier.Text = "🔒 CPU/iGPU watts need PawnIO";
                break;
            case SensorTier.LhmFailed:
                Banner.Visibility = Visibility.Visible;
                BannerText.Text = "Hardware sensor library failed to initialize — CPU/iGPU telemetry unavailable (see logs). " +
                                  "Battery telemetry is unaffected.";
                BannerBtn1.Content = "Re-detect";
                BannerBtn2.Visibility = Visibility.Collapsed;
                StatusTier.Text = "sensor init failed";
                break;
            default:
                StatusTier.Text = "…";
                break;
        }
    }

    // ─────────────────────────── chart data ───────────────────────────

    // ≈72 h at 1 s sampling; beyond this the oldest quarter is trimmed so week-long
    // uptimes don't grow RAM without bound
    private const int MaxChartPoints = 260_000;
    private const int TrimTarget = 200_000;

    private void AppendToChart(PowerSample s)
    {
        var x = s.Time.LocalDateTime.ToOADate();

        // break chart lines across sleep/hibernate gaps
        if (s.GapBefore && _times.Count > 0)
        {
            var gapX = (_times[^1] + x) / 2;
            _netLog.Add(gapX, double.NaN);
            _pctLog.Add(gapX, double.NaN);
            _cpuLog.Add(gapX, double.NaN);
            _gpuLog.Add(gapX, double.NaN);
            _loadLog.Add(gapX, double.NaN);
        }

        _times.Add(x);
        _net.Add(s.NetW);
        _cpu.Add(s.CpuPackageW ?? double.NaN);
        _gpu.Add(s.IGpuW ?? double.NaN);
        _pct.Add(s.BatteryPercent);
        _load.Add(s.CpuLoadPct ?? double.NaN);

        _netLog.Add(x, s.NetW);
        _pctLog.Add(x, s.BatteryPercent);
        if (s.CpuPackageW is double cw) _cpuLog.Add(x, cw);
        if (s.IGpuW is double gw) _gpuLog.Add(x, gw);
        if (s.CpuLoadPct is double lw) _loadLog.Add(x, lw);

        if (_times.Count > MaxChartPoints) TrimChart();
    }

    private void TrimChart()
    {
        var remove = _times.Count - TrimTarget;
        _times.RemoveRange(0, remove);
        _net.RemoveRange(0, remove);
        _cpu.RemoveRange(0, remove);
        _gpu.RemoveRange(0, remove);
        _pct.RemoveRange(0, remove);
        _load.RemoveRange(0, remove);

        _netLog.Clear();
        _cpuLog.Clear();
        _gpuLog.Clear();
        _pctLog.Clear();
        _loadLog.Clear();
        for (var i = 0; i < _times.Count; i++)
        {
            _netLog.Add(_times[i], _net[i]);
            _pctLog.Add(_times[i], _pct[i]);
            if (!double.IsNaN(_cpu[i])) _cpuLog.Add(_times[i], _cpu[i]);
            if (!double.IsNaN(_gpu[i])) _gpuLog.Add(_times[i], _gpu[i]);
            if (!double.IsNaN(_load[i])) _loadLog.Add(_times[i], _load[i]);
        }
        Log.Info($"chart trimmed to {_times.Count} points");
    }

    public void OnPowerEvent(PowerEvent ev)
    {
        AddEventMarker(ev);
        if (IsVisible) Chart.Refresh();
    }

    private void AddEventMarker(PowerEvent ev)
    {
        var line = Chart.Plot.Add.VerticalLine(ev.Time.LocalDateTime.ToOADate());
        line.Color = ev.Kind switch
        {
            PowerEventKind.AcConnected => PctColor.WithAlpha(120),
            PowerEventKind.AcDisconnected => ScottPlot.Color.FromHex("#F0883E").WithAlpha(120),
            _ => LoadColor.WithAlpha(100),
        };
        line.LineWidth = 1;
        line.LinePattern = LinePattern.Dotted;
    }

    private void UpdateAxes()
    {
        if (_times.Count == 0) return;
        var now = DateTime.Now.ToOADate();
        var minutes = AppSettings.Current.ChartRangeMinutes;
        var xMin = now - minutes / 1440.0;
        Chart.Plot.Axes.SetLimitsX(xMin, now + minutes / 1440.0 * 0.02);

        // Y (left, watts): fit visible data of enabled watt series, always include 0
        var i0 = LowerBound(_times, xMin);
        double min = 0, max = 1;
        void Scan(List<double> ys, bool enabled)
        {
            if (!enabled) return;
            for (var i = i0; i < ys.Count; i++)
            {
                var v = ys[i];
                if (double.IsNaN(v)) continue;
                if (v < min) min = v;
                if (v > max) max = v;
            }
        }
        Scan(_net, ChkNet.IsChecked == true);
        Scan(_cpu, ChkCpu.IsChecked == true);
        Scan(_gpu, ChkGpu.IsChecked == true);
        var pad = Math.Max((max - min) * 0.1, 0.5);
        Chart.Plot.Axes.SetLimitsY(min - pad, max + pad);
        Chart.Plot.Axes.SetLimitsY(0, 105, Chart.Plot.Axes.Right);
    }

    private static int LowerBound(List<double> xs, double value)
    {
        int lo = 0, hi = xs.Count;
        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            if (xs[mid] < value) lo = mid + 1; else hi = mid;
        }
        return lo;
    }

    private void Chart_MouseMove(object sender, MouseEventArgs e)
    {
        if (_times.Count == 0) return;
        var pos = e.GetPosition(Chart);
        var pixel = new Pixel(pos.X * Chart.DisplayScale, pos.Y * Chart.DisplayScale);
        var coord = Chart.Plot.GetCoordinates(pixel);

        var i = LowerBound(_times, coord.X);
        if (i >= _times.Count) i = _times.Count - 1;
        if (i > 0 && Math.Abs(_times[i - 1] - coord.X) < Math.Abs(_times[i] - coord.X)) i--;

        _hoverLine.Position = _times[i];
        _hoverLine.IsVisible = true;

        var t = DateTime.FromOADate(_times[i]);
        var parts = new List<string> { t.ToString("HH:mm:ss") };
        if (!double.IsNaN(_net[i])) parts.Add($"net {UnitFormatter.Power(_net[i], signed: true)}");
        if (!double.IsNaN(_cpu[i])) parts.Add($"CPU {UnitFormatter.Power(_cpu[i])}");
        if (!double.IsNaN(_gpu[i])) parts.Add($"iGPU {UnitFormatter.Power(_gpu[i])}");
        if (!double.IsNaN(_pct[i])) parts.Add($"{_pct[i]:F1}%");
        if (!double.IsNaN(_load[i])) parts.Add($"load {_load[i]:F0}%");
        HoverReadout.Text = string.Join("  •  ", parts);

        if (!_live) Chart.Refresh();
    }

    // ─────────────────────────── toolbar handlers ───────────────────────────

    private void Range_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string tag } && int.TryParse(tag, out var minutes))
        {
            AppSettings.Current.ChartRangeMinutes = minutes;
            if (!_initializing) AppSettings.Save();
            SetLive(true);
            UpdateAxes();
            Chart.Refresh();
        }
    }

    private void Series_Toggled(object sender, RoutedEventArgs e)
    {
        if (_netLog is null) return;
        _netLog.IsVisible = ChkNet.IsChecked == true;
        _cpuLog.IsVisible = ChkCpu.IsChecked == true;
        _gpuLog.IsVisible = ChkGpu.IsChecked == true;
        _pctLog.IsVisible = ChkPct.IsChecked == true;
        _loadLog.IsVisible = ChkLoad.IsChecked == true;
        var s = AppSettings.Current;
        s.ChartShowNet = _netLog.IsVisible;
        s.ChartShowCpu = _cpuLog.IsVisible;
        s.ChartShowGpu = _gpuLog.IsVisible;
        s.ChartShowPercent = _pctLog.IsVisible;
        s.ChartShowCpuLoad = _loadLog.IsVisible;
        if (!_initializing) AppSettings.Save();
        UpdateAxes();
        Chart.Refresh();
    }

    private void Live_Checked(object sender, RoutedEventArgs e) => SetLive(true);

    private void SetLive(bool live)
    {
        _live = live;
        // IsChecked="True" in XAML raises Checked during InitializeComponent, before
        // named controls are assigned — bail until the window is fully constructed.
        if (LiveToggle is null || Chart is null) return;
        LiveToggle.IsChecked = live;
        LiveToggle.Content = live ? "● LIVE" : "○ PAUSED";
        if (live)
        {
            UpdateAxes();
            Chart.Refresh();
        }
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "CSV files|*.csv",
            FileName = $"powermonitor-{DateTime.Now:yyyyMMdd-HHmm}.csv",
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            var limits = Chart.Plot.Axes.GetLimits();
            var from = DateTime.FromOADate(limits.Left);
            var to = DateTime.FromOADate(limits.Right);
            var rows = App.Current.History.ExportRange(from, to, dlg.FileName);
            HoverReadout.Text = $"exported {rows:N0} samples → {dlg.FileName}";
        }
        catch (Exception ex)
        {
            Log.Error("export", ex);
            MessageBox.Show(this, ex.Message, "Export failed");
        }
    }

    private void Interval_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing) return;
        var seconds = StatusInterval.SelectedIndex switch { 0 => 0.5, 1 => 1.0, 2 => 2.0, _ => 5.0 };
        AppSettings.Current.SamplingIntervalSeconds = seconds;
        AppSettings.Save();
        App.Current.Sampler.SetInterval(seconds);
    }

    private void Units_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing) return;
        AppSettings.Current.PowerUnit = StatusPowerUnit.SelectedIndex == 1 ? PowerUnit.Milliwatts : PowerUnit.Watts;
        AppSettings.Current.EnergyUnit = StatusEnergyUnit.SelectedIndex == 1 ? EnergyUnit.MilliampHours : EnergyUnit.WattHours;
        AppSettings.Save();
    }

    private void SesReset_Click(object sender, RoutedEventArgs e) => App.Current.Sampler.ResetSession();

    private void MiniGraph_Click(object sender, RoutedEventArgs e) => App.Current.ToggleMiniGraph();

    private void Settings_Click(object sender, RoutedEventArgs e) => OpenSettings();

    public void OpenSettings()
    {
        var win = new SettingsWindow { Owner = this };
        win.ShowDialog();
    }

    private void BannerBtn1_Click(object sender, RoutedEventArgs e)
    {
        switch (_lastTier)
        {
            case SensorTier.NeedsAdmin:
                StartupHelper.RestartElevated(); // new instance signals us to exit via --replace
                break;
            case SensorTier.DriverBlocked:
                _ = InstallPawnIoAsync();
                break;
            case SensorTier.LhmFailed:
                RequestRedetect();
                break;
        }
    }

    private const string PawnIoDownloadUrl =
        "https://github.com/namazso/PawnIO.Setup/releases/latest/download/PawnIO_setup.exe";

    /// <summary>Downloads the official PawnIO installer (with explicit consent — it's a kernel
    /// driver) and runs it; its own wizard + UAC handle the actual install.</summary>
    private async Task InstallPawnIoAsync()
    {
        var consent = MessageBox.Show(this,
            "PawnIO is a signed kernel driver that lets Windows read CPU/iGPU power sensors while " +
            "Memory Integrity is enabled.\n\n" +
            $"PowerMonitor will download the official installer from:\n{PawnIoDownloadUrl}\n\n" +
            "and launch it. Continue?",
            "Install PawnIO", MessageBoxButton.OKCancel, MessageBoxImage.Information);
        if (consent != MessageBoxResult.OK) return;

        BannerBtn1.IsEnabled = false;
        BannerBtn1.Content = "Downloading…";
        try
        {
            var dest = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "PawnIO_setup.exe");
            using (var http = new System.Net.Http.HttpClient())
            {
                http.Timeout = TimeSpan.FromMinutes(2);
                var bytes = await http.GetByteArrayAsync(PawnIoDownloadUrl);
                if (bytes.Length < 100_000)
                    throw new InvalidOperationException($"download too small ({bytes.Length} bytes)");
                await System.IO.File.WriteAllBytesAsync(dest, bytes);
            }

            BannerBtn1.Content = "Installing…";
            var proc = Process.Start(new ProcessStartInfo(dest) { UseShellExecute = true });
            if (proc is not null)
            {
                await proc.WaitForExitAsync();
                RequestRedetect();
            }
        }
        catch (Exception ex)
        {
            Log.Error("pawnio install", ex);
            // fall back to the website so the user can grab it manually
            Process.Start(new ProcessStartInfo("https://pawnio.eu/") { UseShellExecute = true });
        }
        finally
        {
            BannerBtn1.IsEnabled = true;
            BannerBtn1.Content = "Get PawnIO";
        }
    }

    private void BannerBtn2_Click(object sender, RoutedEventArgs e) => RequestRedetect();

    private void RequestRedetect()
    {
        App.Current.Sampler.RequestHardwareReinit();
        _lastTier = SensorTier.Probing;
        StatusTier.Text = "re-detecting…";
    }

    // ─────────────────────────── window plumbing ───────────────────────────

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        AppSettings.Current.WindowWidth = Width;
        AppSettings.Current.WindowHeight = Height;
        AppSettings.Save();
        if (!AppSettings.Current.CloseToTray)
        {
            App.Current.ExitApp();
        }
        else if (!AppSettings.Current.SlimMode)
        {
            e.Cancel = true;
            Hide();
        }
        // slim mode: let the window actually close — chart + history buffers are freed,
        // the app lives on in the tray, and reopening rebuilds from CSV backfill
    }

    private void EnableDarkTitleBar(bool dark)
    {
        try
        {
            var handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero) return;
            var value = dark ? 1 : 0;
            DwmSetWindowAttribute(handle, 20 /*DWMWA_USE_IMMERSIVE_DARK_MODE*/, ref value, sizeof(int));
        }
        catch { /* cosmetic only */ }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
}
