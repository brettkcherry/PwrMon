using System.Drawing;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using PwrMon.Models;

namespace PwrMon.Services;

/// <summary>
/// System-tray presence: a dynamically rendered icon showing the live wattage (or battery %),
/// color-coded by power state, plus the tray context menu. Must live on the UI thread.
/// </summary>
public sealed class TrayService : IDisposable
{
    private readonly NotifyIcon _icon;
    private IntPtr _currentIconHandle = IntPtr.Zero;
    private string _lastRendered = "";

    public event Action? OpenRequested;
    public event Action? MiniGraphToggleRequested;
    public event Action? MiniGraphClickThroughToggleRequested;
    public event Action? MiniGraphAlwaysOnTopToggleRequested;
    public event Action? SettingsRequested;
    public event Action? ExitRequested;

    private readonly ToolStripMenuItem _miniGraphItem;
    private readonly ToolStripMenuItem _miniGraphClickThroughItem;
    private readonly ToolStripMenuItem _miniGraphAlwaysOnTopItem;
    private readonly ToolStripMenuItem _trayWattsItem;
    private readonly ToolStripMenuItem _trayPercentItem;

    public TrayService()
    {
        var menu = new ContextMenuStrip();
        var open = new ToolStripMenuItem("Open dashboard", null, (_, _) => OpenRequested?.Invoke());
        open.Font = new Font(open.Font, System.Drawing.FontStyle.Bold);
        _miniGraphItem = new ToolStripMenuItem("Mini graph", null, (_, _) => MiniGraphToggleRequested?.Invoke());
        _miniGraphClickThroughItem = new ToolStripMenuItem("Mini graph click-through", null, (_, _) => MiniGraphClickThroughToggleRequested?.Invoke());
        _miniGraphAlwaysOnTopItem = new ToolStripMenuItem("Mini graph always on top", null, (_, _) => MiniGraphAlwaysOnTopToggleRequested?.Invoke());
        _trayWattsItem = new ToolStripMenuItem("Tray shows watts", null, (_, _) => SetTrayDisplay(TrayDisplay.Watts));
        _trayPercentItem = new ToolStripMenuItem("Tray shows battery %", null, (_, _) => SetTrayDisplay(TrayDisplay.Percent));

        menu.Items.Add(open);
        menu.Items.Add(_miniGraphItem);
        menu.Items.Add(_miniGraphAlwaysOnTopItem);
        menu.Items.Add(_miniGraphClickThroughItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_trayWattsItem);
        menu.Items.Add(_trayPercentItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Settings…", null, (_, _) => SettingsRequested?.Invoke()));
        menu.Items.Add(new ToolStripMenuItem("Exit", null, (_, _) => ExitRequested?.Invoke()));
        menu.Opening += (_, _) => SyncMenuChecks();

        _icon = new NotifyIcon
        {
            Visible = true,
            ContextMenuStrip = menu,
            Text = "PwrMon",
        };
        _icon.DoubleClick += (_, _) => OpenRequested?.Invoke();
        // clicking a drain alert should land on the chart that shows what's happening
        _icon.BalloonTipClicked += (_, _) => OpenRequested?.Invoke();

        RenderIcon("…", "", System.Drawing.Color.White);
    }

    private static void SetTrayDisplay(TrayDisplay d)
    {
        AppSettings.Current.TrayDisplay = d;
        AppSettings.Save();
    }

    private void SyncMenuChecks()
    {
        _miniGraphItem.Checked = AppSettings.Current.MiniGraphEnabled;
        _miniGraphClickThroughItem.Checked = AppSettings.Current.MiniGraphClickThrough;
        _miniGraphAlwaysOnTopItem.Checked = AppSettings.Current.MiniGraphAlwaysOnTop;
        _trayWattsItem.Checked = AppSettings.Current.TrayDisplay == TrayDisplay.Watts;
        _trayPercentItem.Checked = AppSettings.Current.TrayDisplay == TrayDisplay.Percent;
    }

    public void Update(PowerSample s, Estimates est)
    {
        string text, unit;
        System.Drawing.Color color;

        // unit glyph ("w"/"%") rendered small beside every numeric value so the icon reads on
        // its own, without hovering for the tooltip — a bare "3" could be watts, percent, anything.
        if (!s.HasBattery)
        {
            (text, unit) = s.CpuPackageW is double cpu ? (FormatWatts(cpu), "w") : ("PC", "");
            color = System.Drawing.Color.White;
        }
        else if (AppSettings.Current.TrayDisplay == TrayDisplay.Percent)
        {
            text = Math.Round(s.BatteryPercent).ToString("0");
            unit = "%";
            color = s.Charging ? System.Drawing.Color.LimeGreen
                  : s.BatteryPercent < 20 ? System.Drawing.Color.OrangeRed
                  : System.Drawing.Color.White;
        }
        else if (s.Discharging)
        {
            // on battery: discharge rate IS total system draw (measured)
            text = FormatWatts(s.DischargeRateW);
            unit = "w";
            color = s.DischargeRateW > 60 ? System.Drawing.Color.OrangeRed : System.Drawing.Color.Orange;
        }
        else if (s.AcOnline)
        {
            // on AC the battery flow is trickle noise — show estimated system draw instead
            // (the power-budget number), falling back to CPU package watts pre-baseline
            var w = est.EstSystemW ?? s.CpuPackageW;
            (text, unit) = w is double sys ? (FormatWatts(sys), "w") : ("AC", "");
            color = s.Charging ? System.Drawing.Color.LimeGreen : System.Drawing.Color.LightSkyBlue;
        }
        else
        {
            text = FormatWatts(Math.Abs(s.NetW));
            unit = "w";
            color = System.Drawing.Color.LightGray;
        }

        var key = text + unit + color.ToArgb();
        if (key != _lastRendered)
        {
            RenderIcon(text, unit, color);
            _lastRendered = key;
        }

        var state = s.Charging ? "Charging"
                  : s.Discharging && s.AcOnline ? "DRAINING on AC!"
                  : s.Discharging ? "Discharging"
                  : s.AcOnline ? "Plugged in" : "Idle";
        var rate = s.Charging || s.Discharging ? $" {UnitFormatter.Power(s.NetW, signed: true)}"
                 : s.AcOnline && est.EstSystemW is double es ? $" sys ≈ {UnitFormatter.Power(es)}"
                 : "";
        var eta = s.Charging && est.TimeToFull is not null ? $" • full in {UnitFormatter.Duration(est.TimeToFull)}"
                : s.Discharging && est.TimeToEmpty is not null ? $" • {UnitFormatter.Duration(est.TimeToEmpty)} left"
                : "";
        var tip = $"PwrMon — {state}{rate} • {s.BatteryPercent:F0}%{eta}";
        _icon.Text = tip.Length <= 63 ? tip : tip[..63];
    }

    /// <summary>Pushes a balloon/toast from the tray icon. The only thing in PwrMon that
    /// interrupts the user, and deliberately so — see <see cref="DrainAlertService"/>.</summary>
    public void ShowAlert(string title, string body)
    {
        try
        {
            _icon.BalloonTipTitle = title;
            _icon.BalloonTipText = body;
            _icon.BalloonTipIcon = ToolTipIcon.Warning;
            _icon.ShowBalloonTip(30_000); // the shell caps this; it's a request, not a promise
        }
        catch (Exception ex) { Log.Error($"tray alert: {ex.Message}"); }
    }

    private static string FormatWatts(double w) =>
        w < 9.95 ? w.ToString("0.#") : Math.Round(w).ToString("0");

    /// <summary>Renders <paramref name="text"/> with <paramref name="unit"/> appended (e.g.
    /// "21w") as one string in one font, sized as big as will fit — no length-based tiers, no
    /// guessing: measure once at an arbitrary probe size, then scale linearly to whichever of
    /// width/height is the binding constraint. DrawString also wraps text that overflows its
    /// layout rect by default, which is what turned "35w" into "35" over "w" before — NoWrap
    /// below prevents that regardless of how tight the fit is.</summary>
    private void RenderIcon(string text, string unit, System.Drawing.Color color)
    {
        var combined = text + unit;
        var size = 32; // shell scales down; rendering large keeps digits crisp on high DPI
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(System.Drawing.Color.Transparent);
            g.TextRenderingHint = TextRenderingHint.AntiAlias;

            const float probeSize = 64f; // any value works — we solve for the linear scale factor
            var margin = size - 0.5f; // as tight as it gets without risking edge clipping
            float fontSize;
            using (var probe = new Font("Segoe UI", probeSize, System.Drawing.FontStyle.Bold, GraphicsUnit.Pixel))
            {
                var measured = g.MeasureString(combined, probe, PointF.Empty, StringFormat.GenericTypographic);
                var scale = Math.Min(margin / measured.Width, margin / measured.Height);
                fontSize = probeSize * scale;
            }

            using var font = new Font("Segoe UI", fontSize, System.Drawing.FontStyle.Bold, GraphicsUnit.Pixel);
            using var brush = new SolidBrush(color);
            var sf = (StringFormat)StringFormat.GenericTypographic.Clone();
            sf.Alignment = StringAlignment.Center;
            sf.LineAlignment = StringAlignment.Center;
            sf.FormatFlags |= StringFormatFlags.NoWrap;
            g.DrawString(combined, font, brush, new RectangleF(0, 0, size, size), sf);
        }

        var hIcon = bmp.GetHicon();
        _icon.Icon = Icon.FromHandle(hIcon);
        if (_currentIconHandle != IntPtr.Zero)
            DestroyIcon(_currentIconHandle); // GDI icon handles leak unless explicitly destroyed
        _currentIconHandle = hIcon;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        if (_currentIconHandle != IntPtr.Zero)
        {
            DestroyIcon(_currentIconHandle);
            _currentIconHandle = IntPtr.Zero;
        }
    }
}
