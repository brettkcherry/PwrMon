using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using PwrMon.Models;
using PwrMon.Services;
using ScottPlot;
using ScottPlot.Plottables;

namespace PwrMon.Views;

public partial class MainWindow : Window
{
    private static readonly ScottPlot.Color NetColor = ScottPlot.Color.FromHex("#F5B62E");
    private static readonly ScottPlot.Color CpuColor = ScottPlot.Color.FromHex("#58A6FF");
    private static readonly ScottPlot.Color GpuColor = ScottPlot.Color.FromHex("#BC8CFF");
    private static readonly ScottPlot.Color PctColor = ScottPlot.Color.FromHex("#3FB950");
    private static readonly ScottPlot.Color LoadColor = ScottPlot.Color.FromHex("#8B93A7");
    private static readonly ScottPlot.Color CpuTempColor = ScottPlot.Color.FromHex("#F85149");
    private static readonly ScottPlot.Color DriveTempColor = ScottPlot.Color.FromHex("#F0883E");

    private DataLogger _netLog = null!, _cpuLog = null!, _gpuLog = null!, _pctLog = null!, _loadLog = null!;
    private DataLogger _cpuTempLog = null!, _driveTempLog = null!;
    private VerticalLine _hoverLine = null!;

    // parallel history kept locally for hover readout + Y autoscale (single owner: UI thread)
    private readonly List<double> _times = new(), _net = new(), _cpu = new(), _gpu = new(), _pct = new(), _load = new();
    private readonly List<double> _cpuTemp = new(), _driveTemp = new();

    private bool _initializing = true;
    private bool _live = true;

    // live samples arriving while the backfill is still loading park here: appending them
    // first would put newer X ahead of older backfill rows, and DataLogger requires
    // ascending X (this race was the only crash of the v1.3.1 soak). Null once flushed.
    private List<PowerSample>? _pendingLive = new();

    // TradingView-style interaction: time is the only free axis, Y always auto-fits.
    private double _viewSpanDays;                       // visible time span (live view anchors it to now)
    private bool _panning;
    private Point _panStartPos;
    private double _panStartLeft;
    private const double MinSpanDays = 30.0 / 86400.0;  // can't zoom in past 30 s
    private const double FutureFrac = 0.02;             // breathing margin right of the newest sample
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
        SetupCardDrag();
        BackfillHistoryAsync();
        LoadStaticInfoAsync();

        _viewSpanDays = AppSettings.Current.ChartRangeMinutes / 1440.0;
        Chart.MouseMove += Chart_MouseMove;
        Chart.MouseLeave += (_, _) => { _hoverLine.IsVisible = false; HoverReadout.Text = " "; };
        Chart.MouseDown += Chart_MouseDown;
        Chart.MouseUp += Chart_MouseUp;
        Chart.MouseWheel += Chart_MouseWheel;

        Closing += MainWindow_Closing;
        // Ctrl+, — the cross-app convention for "open settings" (VS Code, Slack, Chrome, Discord)
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.OemComma && Keyboard.Modifiers == ModifierKeys.Control) OpenSettings();
        };
        _initializing = false;
    }

    // ─────────────────────────── chart setup ───────────────────────────

    private HorizontalLine _zeroLine = null!;

    private void SetupChart()
    {
        Chart.UserInputProcessor.IsEnabled = false; // we own pan/zoom (time-only, clamped)

        var plot = Chart.Plot;
        plot.Legend.IsVisible = false;
        plot.Axes.DateTimeTicksBottom();

        _netLog = NewLogger(plot, NetColor, 1.8f);
        _cpuLog = NewLogger(plot, CpuColor, 1.4f);
        _gpuLog = NewLogger(plot, GpuColor, 1.4f);
        _pctLog = NewLogger(plot, PctColor, 1.4f);
        _loadLog = NewLogger(plot, LoadColor, 1.0f);
        _cpuTempLog = NewLogger(plot, CpuTempColor, 1.2f);
        _driveTempLog = NewLogger(plot, DriveTempColor, 1.2f);
        _pctLog.Axes.YAxis = plot.Axes.Right;
        _loadLog.Axes.YAxis = plot.Axes.Right;
        // °C shares the right axis with the percentage series: both live in 0–105, so a third
        // axis would cost layout without earning anything. The checkbox labels carry the unit.
        _cpuTempLog.Axes.YAxis = plot.Axes.Right;
        _driveTempLog.Axes.YAxis = plot.Axes.Right;

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
        // Heat reuses each palette's existing red/orange rather than adding two more series
        // slots to all twelve themes — and they're already tuned per theme.
        _cpuTempLog.Color = ScottPlot.Color.FromHex(t.Red);
        _driveTempLog.Color = ScottPlot.Color.FromHex(t.Orange);
        _zeroLine.Color = ScottPlot.Color.FromHex(t.ChartGrid).Lighten(0.2);
        _hoverLine.Color = ScottPlot.Color.FromHex(t.TextDim).WithAlpha(150);
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

        // Checked/Unchecked fires Series_Toggled synchronously on each assignment below; that
        // handler is guarded on _initializing (still true here) so it can't read the other four
        // checkboxes' not-yet-set state and clobber AppSettings with it (that was the bug).
        ChkNet.IsChecked = AppSettings.Current.ChartShowNet;
        ChkCpu.IsChecked = AppSettings.Current.ChartShowCpu;
        ChkGpu.IsChecked = AppSettings.Current.ChartShowGpu;
        ChkPct.IsChecked = AppSettings.Current.ChartShowPercent;
        ChkLoad.IsChecked = AppSettings.Current.ChartShowCpuLoad;
        ChkCpuTemp.IsChecked = AppSettings.Current.ChartShowCpuTemp;
        ChkDriveTemp.IsChecked = AppSettings.Current.ChartShowDriveTemp;
        ApplySeriesVisibility();

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

    // ─────────────────────────── card rearranging ───────────────────────────

    private Border? _dragCard;
    private Point _dragStart;

    private void SetupCardDrag()
    {
        ApplySavedCardOrder();
        CardsPanel.AllowDrop = true;
        CardsPanel.DragOver += (_, e) => { e.Effects = DragDropEffects.Move; e.Handled = true; };
        foreach (var card in CardsPanel.Children.OfType<Border>())
        {
            card.AllowDrop = true;
            card.PreviewMouseLeftButtonDown += Card_MouseDown;
            card.PreviewMouseMove += Card_MouseMove;
            card.DragOver += Card_DragOver;
            card.GiveFeedback += Card_GiveFeedback; // fires on the drag source: moves the ghost
            card.Drop += (_, e) => e.Handled = true; // reorder already happened live in DragOver
        }
    }

    private void ApplySavedCardOrder()
    {
        var order = AppSettings.Current.CardOrder.Split(',', StringSplitOptions.RemoveEmptyEntries);
        if (order.Length == 0) return;
        var byKey = CardsPanel.Children.OfType<Border>().ToDictionary(b => (string)b.Tag);
        var index = 0;
        foreach (var key in order)
        {
            if (!byKey.TryGetValue(key, out var card)) continue;
            CardsPanel.Children.Remove(card);
            CardsPanel.Children.Insert(Math.Min(index, CardsPanel.Children.Count), card);
            index++;
        }
    }

    private void SaveCardOrder()
    {
        AppSettings.Current.CardOrder =
            string.Join(',', CardsPanel.Children.OfType<Border>().Select(b => (string)b.Tag));
        AppSettings.Save();
    }

    private void Card_MouseDown(object sender, MouseButtonEventArgs e)
    {
        // don't hijack clicks on interactive children (e.g. the session Reset button)
        if (HasButtonAncestor(e.OriginalSource as DependencyObject)) return;
        _dragCard = (Border)sender;
        _dragStart = e.GetPosition(this);
        _grabOffset = e.GetPosition(_dragCard);
    }

    private void Card_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragCard is null || e.LeftButton != MouseButtonState.Pressed) return;
        var pos = e.GetPosition(this);
        if (Math.Abs(pos.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        var card = _dragCard;
        _dragCard = null;
        ShowGhost(card);
        card.Opacity = 0; // card fully lifts out; the empty slot marks where it will land
        try
        {
            DragDrop.DoDragDrop(card, new DataObject(CardDragFormat, card), DragDropEffects.Move);
        }
        finally
        {
            card.Opacity = 1.0;
            RemoveGhost();
            SaveCardOrder(); // live reordering already placed it; persist the final layout
        }
    }

    // ── drag ghost: a floating snapshot of the card that rides the cursor ──

    private Point _grabOffset;
    private DragGhostAdorner? _ghost;
    private AdornerLayer? _ghostLayer;

    private void ShowGhost(Border card)
    {
        try
        {
            var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this).PixelsPerDip;
            var w = Math.Max(1, (int)(card.ActualWidth * dpi));
            var h = Math.Max(1, (int)(card.ActualHeight * dpi));
            var snapshot = new System.Windows.Media.Imaging.RenderTargetBitmap(
                w, h, 96 * dpi, 96 * dpi, System.Windows.Media.PixelFormats.Pbgra32);
            // render via VisualBrush: RTB.Render(element) bakes in the element's layout
            // offset, which pushes every card except the top-left one outside the bitmap
            var visual = new System.Windows.Media.DrawingVisual();
            using (var ctx = visual.RenderOpen())
                ctx.DrawRectangle(new System.Windows.Media.VisualBrush(card), null,
                    new Rect(0, 0, card.ActualWidth, card.ActualHeight));
            snapshot.Render(visual);
            snapshot.Freeze();

            _ghostLayer = AdornerLayer.GetAdornerLayer(CardsPanel);
            if (_ghostLayer is null) return;
            var accent = ((System.Windows.Media.SolidColorBrush)FindResource("AccentBrush")).Color;
            _ghost = new DragGhostAdorner(CardsPanel, snapshot,
                new Size(card.ActualWidth, card.ActualHeight), _grabOffset, accent);
            _ghostLayer.Add(_ghost);
            if (GetCursorPos(out var pt)) // ghost visible from the very first frame
                _ghost.UpdatePosition(CardsPanel.PointFromScreen(new Point(pt.X, pt.Y)));
        }
        catch (Exception ex)
        {
            Log.Error("drag ghost", ex);
            _ghost = null;
        }
    }

    private void RemoveGhost()
    {
        if (_ghost is not null && _ghostLayer is not null)
            _ghostLayer.Remove(_ghost);
        _ghost = null;
        _ghostLayer = null;
    }

    private void Card_GiveFeedback(object sender, GiveFeedbackEventArgs e)
    {
        if (_ghost is null) return;
        if (GetCursorPos(out var pt))
            _ghost.UpdatePosition(CardsPanel.PointFromScreen(new Point(pt.X, pt.Y)));
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Win32Point pt);

    [StructLayout(LayoutKind.Sequential)]
    private struct Win32Point { public int X; public int Y; }

    /// <summary>Renders a frozen snapshot of the dragged card, outlined in the theme accent,
    /// at the cursor position (in CardsPanel coordinates). Hit-test invisible.</summary>
    private sealed class DragGhostAdorner : Adorner
    {
        private readonly System.Windows.Media.ImageSource _snapshot;
        private readonly Size _size;
        private readonly Point _grabOffset;
        private Point _position = new(double.NaN, double.NaN);

        public DragGhostAdorner(UIElement adorned, System.Windows.Media.ImageSource snapshot,
            Size size, Point grabOffset, System.Windows.Media.Color accent) : base(adorned)
        {
            _snapshot = snapshot;
            _size = size;
            _grabOffset = grabOffset;
            IsHitTestVisible = false;
        }

        public void UpdatePosition(Point mouseInPanel)
        {
            _position = new Point(mouseInPanel.X - _grabOffset.X, mouseInPanel.Y - _grabOffset.Y);
            InvalidateVisual();
        }

        protected override void OnRender(System.Windows.Media.DrawingContext dc)
        {
            if (double.IsNaN(_position.X)) return;
            dc.DrawImage(_snapshot, new Rect(_position, _size));
        }
    }

    private const string CardDragFormat = "PwrMon.Card";

    /// <summary>Live reflow: as the drag passes over a card, the dragged card immediately
    /// takes its new slot and the others slide out of the way (FLIP animation).</summary>
    private void Card_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
        if (e.Data.GetData(CardDragFormat) is not Border dragged || sender is not Border target || dragged == target)
            return;

        var targetIndex = CardsPanel.Children.IndexOf(target);
        var draggedIndex = CardsPanel.Children.IndexOf(dragged);
        if (targetIndex < 0 || draggedIndex < 0) return;

        var before = e.GetPosition(target).X < target.ActualWidth / 2;
        var desired = before ? targetIndex : targetIndex + 1;
        if (draggedIndex < desired) desired--; // account for the removal shift
        if (desired == draggedIndex) return;

        AnimateReflow(() =>
        {
            CardsPanel.Children.Remove(dragged);
            CardsPanel.Children.Insert(Math.Clamp(desired, 0, CardsPanel.Children.Count), dragged);
        });
    }

    /// <summary>FLIP: record card positions, apply the layout change, then animate each card
    /// from its old offset back to zero — the reflow visibly slides instead of teleporting.</summary>
    private void AnimateReflow(Action mutateLayout)
    {
        var cards = CardsPanel.Children.OfType<Border>().ToList();
        var before = cards.ToDictionary(c => c, c => c.TranslatePoint(new Point(0, 0), CardsPanel));

        mutateLayout();
        CardsPanel.UpdateLayout();

        foreach (var card in cards)
        {
            var b = before[card];
            var a = card.TranslatePoint(new Point(0, 0), CardsPanel);
            double dx = b.X - a.X, dy = b.Y - a.Y;
            if (Math.Abs(dx) < 1 && Math.Abs(dy) < 1) continue;

            var transform = new System.Windows.Media.TranslateTransform(dx, dy);
            card.RenderTransform = transform;
            var ease = new System.Windows.Media.Animation.QuadraticEase();
            transform.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty,
                new System.Windows.Media.Animation.DoubleAnimation(dx, 0, TimeSpan.FromMilliseconds(170)) { EasingFunction = ease });
            transform.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty,
                new System.Windows.Media.Animation.DoubleAnimation(dy, 0, TimeSpan.FromMilliseconds(170)) { EasingFunction = ease });
        }
    }

    private static bool HasButtonAncestor(DependencyObject? d)
    {
        while (d is not null)
        {
            if (d is System.Windows.Controls.Primitives.ButtonBase) return true;
            d = d is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
                ? System.Windows.Media.VisualTreeHelper.GetParent(d)
                : LogicalTreeHelper.GetParent(d);
        }
        return false;
    }

    private void BackfillHistoryAsync()
    {
        Task.Run(() =>
        {
            var app = App.Current;
            var samples = new List<PowerSample>();
            var events = new List<PowerEvent>();
            try
            {
                samples = app.History.LoadRecent(48);
                events = app.History.LoadRecentEvents(48);
            }
            catch (Exception ex) { Log.Error("backfill load", ex); }
            Dispatcher.BeginInvoke(() =>
            {
                foreach (var s in samples) AppendToChart(s);

                // flush the live samples parked during the load (they're newer than the
                // backfill); skip any the disk read already covered via a periodic flush
                var parked = _pendingLive ?? new();
                _pendingLive = null;
                var lastX = _times.Count > 0 ? _times[^1] : double.MinValue;
                foreach (var s in parked)
                    if (s.Time.LocalDateTime.ToOADate() > lastX)
                        AppendToChart(s);

                foreach (var ev in events) AddEventMarker(ev);
                Log.Info($"backfilled {samples.Count} samples (+{parked.Count} parked live), {events.Count} events");
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

        if (_pendingLive is not null) _pendingLive.Add(s);
        else AppendToChart(s);
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
        else if (s.Discharging && s.AcOnline) { state = "PLUGGED IN — DRAINING"; brush = (System.Windows.Media.Brush)FindResource("RedBrush"); }
        else if (s.Discharging) { state = "DISCHARGING"; brush = (System.Windows.Media.Brush)FindResource("OrangeBrush"); }
        else if (s.AcOnline) { state = s.BatteryPercent > 98 ? "PLUGGED IN — FULL" : "PLUGGED IN — IDLE"; brush = (System.Windows.Media.Brush)FindResource("BlueBrush"); }
        else { state = "IDLE"; brush = (System.Windows.Media.Brush)FindResource("TextDimBrush"); }

        HeroState.Text = state;
        HeroState.Foreground = brush;

        // HeroWatts/State get their brush fresh above; HeroSub/Percent/Eta are only ever
        // wired via a XAML StaticResource, which can go stale across a theme switch if the
        // window has lived through several — refresh them here too so nothing gets left behind.
        var textBrush = (System.Windows.Media.Brush)FindResource("TextBrush");
        var dimBrush = (System.Windows.Media.Brush)FindResource("TextDimBrush");

        if (s.HasBattery)
        {
            HeroWatts.Text = UnitFormatter.Power(s.NetW, signed: true);
            HeroWatts.Foreground = brush;
            var sysNote = s.AcOnline && est.EstSystemW is double es2 ? $" • system ≈ {UnitFormatter.Power(es2)}" : "";
            HeroSub.Text = $"{UnitFormatter.Energy(s.RemainingWh, s.VoltageV)} of {UnitFormatter.Energy(s.FullChargeWh, s.VoltageV)} • {s.VoltageV:F2} V • {(s.AcOnline ? "on AC power" : "on battery")}{sysNote}";
            HeroSub.Foreground = dimBrush;
            HeroPercent.Text = $"{s.BatteryPercent:F1}%";
            HeroPercent.Foreground = textBrush;
            HeroEta.Text = s.Charging ? $"full in {UnitFormatter.Duration(est.TimeToFull)}"
                         : s.Discharging ? $"{UnitFormatter.Duration(est.TimeToEmpty)} remaining"
                         : " ";
            HeroEta.Foreground = dimBrush;
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

        // The lock glyph means "your tier can't reach this", not "no data" — CPU-side temps
        // ride the same driver as CPU watts, while the drive is readable in every tier.
        ThermCpu.Text = s.CpuTempC is double tc ? $"{tc:F0} °C" : "🔒";
        ThermCoreMax.Text = s.CpuTempMaxC is double tm ? $"{tm:F0} °C" : "—";
        ThermTjMax.Text = s.CpuTjMaxDeltaC is double td ? $"{td:F0} °C" : "—";
        ThermDrive.Text = s.DriveTempC is double dt ? $"{dt:F0} °C" : "—";
        ThermDriveLabel.Text = App.Current.Sampler.DriveVolume is string vol ? $"Drive ({vol})" : "Drive";

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
                // Watts already work here with no admin needed — that's the good case, not an
                // error — so this banner offers an upgrade rather than warning about a problem.
                // It's what makes the THERMAL card's 🔒 rows (CPU package, hottest core,
                // throttle headroom) actionable instead of a dead end.
                Banner.Visibility = Visibility.Visible;
                BannerText.Text = "CPU/iGPU watts are already live. Restart as administrator to add CPU " +
                                  "temperature and throttle headroom too.";
                BannerBtn1.Content = "Restart as admin";
                BannerBtn2.Visibility = Visibility.Collapsed;
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
            _cpuTempLog.Add(gapX, double.NaN);
            _driveTempLog.Add(gapX, double.NaN);
        }

        _times.Add(x);
        _net.Add(s.NetW);
        _cpu.Add(s.CpuPackageW ?? double.NaN);
        _gpu.Add(s.IGpuW ?? double.NaN);
        _pct.Add(s.BatteryPercent);
        _load.Add(s.CpuLoadPct ?? double.NaN);
        _cpuTemp.Add(s.CpuTempC ?? double.NaN);
        _driveTemp.Add(s.DriveTempC ?? double.NaN);

        _netLog.Add(x, s.NetW);
        _pctLog.Add(x, s.BatteryPercent);
        if (s.CpuPackageW is double cw) _cpuLog.Add(x, cw);
        if (s.IGpuW is double gw) _gpuLog.Add(x, gw);
        if (s.CpuLoadPct is double lw) _loadLog.Add(x, lw);
        if (s.CpuTempC is double ct) _cpuTempLog.Add(x, ct);
        if (s.DriveTempC is double dtv) _driveTempLog.Add(x, dtv);

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
        _cpuTemp.RemoveRange(0, remove);
        _driveTemp.RemoveRange(0, remove);

        _netLog.Clear();
        _cpuLog.Clear();
        _gpuLog.Clear();
        _pctLog.Clear();
        _loadLog.Clear();
        _cpuTempLog.Clear();
        _driveTempLog.Clear();
        for (var i = 0; i < _times.Count; i++)
        {
            _netLog.Add(_times[i], _net[i]);
            _pctLog.Add(_times[i], _pct[i]);
            if (!double.IsNaN(_cpu[i])) _cpuLog.Add(_times[i], _cpu[i]);
            if (!double.IsNaN(_gpu[i])) _gpuLog.Add(_times[i], _gpu[i]);
            if (!double.IsNaN(_load[i])) _loadLog.Add(_times[i], _load[i]);
            if (!double.IsNaN(_cpuTemp[i])) _cpuTempLog.Add(_times[i], _cpuTemp[i]);
            if (!double.IsNaN(_driveTemp[i])) _driveTempLog.Add(_times[i], _driveTemp[i]);
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

    /// <summary>Live view: current span anchored to now (the "go to realtime" window).</summary>
    private void UpdateAxes()
    {
        if (_times.Count == 0) return;
        var now = DateTime.Now.ToOADate();
        var xMin = now - _viewSpanDays;
        var xMax = now + _viewSpanDays * FutureFrac;
        Chart.Plot.Axes.SetLimitsX(xMin, xMax);
        FitY(xMin, xMax);
    }

    /// <summary>Y (left, watts): fit visible data of enabled watt series, always include 0.</summary>
    private void FitY(double xMin, double xMax)
    {
        var i0 = LowerBound(_times, xMin);
        var i1 = LowerBound(_times, xMax);
        double min = 0, max = 1;
        void Scan(List<double> ys, bool enabled)
        {
            if (!enabled) return;
            var end = Math.Min(i1, ys.Count);
            for (var i = i0; i < end; i++)
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

    // ─────────────────────────── pan / zoom (time axis only) ───────────────────────────

    /// <summary>Widest allowed view: all loaded data (at least 5 min for a fresh start).</summary>
    private double MaxSpanDays()
    {
        var dataSpan = _times.Count > 0 ? DateTime.Now.ToOADate() - _times[0] : 0;
        return Math.Max(dataSpan * (1 + FutureFrac), 5.0 / 1440.0);
    }

    /// <summary>Apply a paused view window, clamped so the data can never leave the screen.</summary>
    private void ApplyView(double left, double span)
    {
        var now = DateTime.Now.ToOADate();
        var maxRight = now + span * FutureFrac;
        var minLeft = _times[0];
        // right edge wins when the window is wider than the data (empty space stays on the left)
        left = Math.Clamp(left, Math.Min(minLeft, maxRight - span), maxRight - span);
        _viewSpanDays = span;
        Chart.Plot.Axes.SetLimitsX(left, left + span);
        FitY(left, left + span);
        Chart.Refresh();
    }

    private void Chart_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_times.Count == 0) return;
        var limits = Chart.Plot.Axes.GetLimits();
        var span = limits.Right - limits.Left;
        if (span <= 0) return;

        // zoom around the moment under the cursor
        var pos = e.GetPosition(Chart);
        var anchor = Chart.Plot.GetCoordinates(new Pixel(pos.X * Chart.DisplayScale, pos.Y * Chart.DisplayScale)).X;
        var frac = (anchor - limits.Left) / span;

        var newSpan = Math.Clamp(span * (e.Delta > 0 ? 1 / 1.2 : 1.2), MinSpanDays, MaxSpanDays());
        SetLive(false);
        ApplyView(anchor - frac * newSpan, newSpan);
        e.Handled = true;
    }

    private void Chart_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || _times.Count == 0) return;
        _panning = true;
        _panStartPos = e.GetPosition(Chart);
        _panStartLeft = Chart.Plot.Axes.GetLimits().Left;
        SetLive(false);
        Chart.CaptureMouse();
    }

    private void Chart_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_panning) return;
        _panning = false;
        Chart.ReleaseMouseCapture();
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

        if (_panning)
        {
            var plotWidthPx = Chart.Plot.RenderManager.LastRender.DataRect.Width;
            if (plotWidthPx > 0)
            {
                var dxDays = (pos.X - _panStartPos.X) * Chart.DisplayScale / plotWidthPx * _viewSpanDays;
                ApplyView(_panStartLeft - dxDays, _viewSpanDays);
            }
            return;
        }

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
        if (!double.IsNaN(_cpuTemp[i])) parts.Add($"CPU {_cpuTemp[i]:F0} °C");
        if (!double.IsNaN(_driveTemp[i])) parts.Add($"drive {_driveTemp[i]:F0} °C");
        HoverReadout.Text = string.Join("  •  ", parts);

        if (!_live) Chart.Refresh();
    }

    // ─────────────────────────── toolbar handlers ───────────────────────────

    private void Range_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string tag } && int.TryParse(tag, out var minutes))
        {
            AppSettings.Current.ChartRangeMinutes = minutes;
            _viewSpanDays = minutes / 1440.0;
            if (!_initializing) AppSettings.Save();
            SetLive(true);
            UpdateAxes();
            Chart.Refresh();
        }
    }

    private void Series_Toggled(object sender, RoutedEventArgs e)
    {
        // during SetupToolbar the five checkboxes are set one at a time, each firing this
        // handler; bail so it can't read the others' not-yet-applied state (see SetupToolbar).
        if (_initializing) return;
        ApplySeriesVisibility();
        var s = AppSettings.Current;
        s.ChartShowNet = _netLog.IsVisible;
        s.ChartShowCpu = _cpuLog.IsVisible;
        s.ChartShowGpu = _gpuLog.IsVisible;
        s.ChartShowPercent = _pctLog.IsVisible;
        s.ChartShowCpuLoad = _loadLog.IsVisible;
        s.ChartShowCpuTemp = _cpuTempLog.IsVisible;
        s.ChartShowDriveTemp = _driveTempLog.IsVisible;
        AppSettings.Save();
        UpdateAxes();
        Chart.Refresh();
    }

    private void ApplySeriesVisibility()
    {
        _netLog.IsVisible = ChkNet.IsChecked == true;
        _cpuLog.IsVisible = ChkCpu.IsChecked == true;
        _gpuLog.IsVisible = ChkGpu.IsChecked == true;
        _pctLog.IsVisible = ChkPct.IsChecked == true;
        _loadLog.IsVisible = ChkLoad.IsChecked == true;
        _cpuTempLog.IsVisible = ChkCpuTemp.IsChecked == true;
        _driveTempLog.IsVisible = ChkDriveTemp.IsChecked == true;
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
            FileName = $"pwrmon-{DateTime.Now:yyyyMMdd-HHmm}.csv",
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
            case SensorTier.EmiOnly:
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
    private const string PawnIoHomeUrl = "https://pawnio.eu/";

    /// <summary>Downloads the official PawnIO installer (with explicit consent — it's a kernel
    /// driver), verifies its Authenticode signature, and runs it; its own wizard + UAC handle
    /// the actual install.</summary>
    private async Task InstallPawnIoAsync()
    {
        var consent = MessageBox.Show(this,
            "PawnIO is a signed kernel driver that lets Windows read CPU/iGPU power sensors while " +
            "Memory Integrity is enabled.\n\n" +
            $"PwrMon will download the official installer from:\n{PawnIoDownloadUrl}\n\n" +
            "check its digital signature, then show you who signed it before running anything. " +
            "Continue?",
            "Install PawnIO", MessageBoxButton.OKCancel, MessageBoxImage.Information);
        if (consent != MessageBoxResult.OK) return;

        BannerBtn1.IsEnabled = false;
        BannerBtn1.Content = "Downloading…";

        // Stage into a freshly created, randomly named directory. A fixed path under %TEMP%
        // is predictable and pre-creatable, which would let another process running as this
        // user swap the installer between the write and the launch — and the launch is the
        // step that raises UAC, so that swap would be an elevation.
        var stage = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "PwrMon-pawnio-" + Guid.NewGuid().ToString("N"));
        try
        {
            System.IO.Directory.CreateDirectory(stage);
            var dest = System.IO.Path.Combine(stage, "PawnIO_setup.exe");

            using (var http = new System.Net.Http.HttpClient())
            {
                http.Timeout = TimeSpan.FromMinutes(2);
                var bytes = await http.GetByteArrayAsync(PawnIoDownloadUrl);
                if (bytes.Length < 100_000)
                    throw new InvalidOperationException($"download too small ({bytes.Length} bytes)");
                await System.IO.File.WriteAllBytesAsync(dest, bytes);
            }

            BannerBtn1.Content = "Verifying…";
            if (!Authenticode.TryVerify(dest, out var signer, out var why))
            {
                Log.Error($"pawnio signature check failed: {why}");
                MessageBox.Show(this,
                    $"The downloaded installer failed its signature check — {why}.\n\n" +
                    "PwrMon will not run it. Opening the PawnIO website so you can download it " +
                    "yourself if you want to.",
                    "Install PawnIO", MessageBoxButton.OK, MessageBoxImage.Error);
                OpenPawnIoSite();
                return;
            }

            // A valid signature is not the same as the *expected* signer. Show who actually
            // signed it and let the user make that call — PwrMon is about to hand this binary
            // an elevation prompt.
            var proceed = MessageBox.Show(this,
                $"Downloaded and signature-verified.\n\nSigned by:\n    {signer}\n\n" +
                "Run the installer? It will ask for administrator rights itself.",
                "Install PawnIO", MessageBoxButton.OKCancel, MessageBoxImage.Question);
            if (proceed != MessageBoxResult.OK) return;

            BannerBtn1.Content = "Installing…";
            var proc = Process.Start(new ProcessStartInfo(dest) { UseShellExecute = true });
            if (proc is not null)
            {
                await proc.WaitForExitAsync();
                RequestRedetect();
            }
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            Log.Info("pawnio install: UAC declined"); // user's choice, not an error
        }
        catch (Exception ex)
        {
            Log.Error("pawnio install", ex);
            OpenPawnIoSite(); // fall back to the website so the user can grab it manually
        }
        finally
        {
            try { System.IO.Directory.Delete(stage, recursive: true); } catch { /* installer may still hold it */ }
            BannerBtn1.IsEnabled = true;
            BannerBtn1.Content = "Get PawnIO";
        }
    }

    private static void OpenPawnIoSite() =>
        Process.Start(new ProcessStartInfo(PawnIoHomeUrl) { UseShellExecute = true });

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
