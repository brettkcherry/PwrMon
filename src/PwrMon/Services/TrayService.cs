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
    public event Action? SettingsRequested;
    public event Action? ExitRequested;

    private readonly ToolStripMenuItem _miniGraphItem;
    private readonly ToolStripMenuItem _trayWattsItem;
    private readonly ToolStripMenuItem _trayPercentItem;

    public TrayService()
    {
        var menu = new ContextMenuStrip();
        var open = new ToolStripMenuItem("Open dashboard", null, (_, _) => OpenRequested?.Invoke());
        open.Font = new Font(open.Font, System.Drawing.FontStyle.Bold);
        _miniGraphItem = new ToolStripMenuItem("Mini graph", null, (_, _) => MiniGraphToggleRequested?.Invoke());
        _trayWattsItem = new ToolStripMenuItem("Tray shows watts", null, (_, _) => SetTrayDisplay(TrayDisplay.Watts));
        _trayPercentItem = new ToolStripMenuItem("Tray shows battery %", null, (_, _) => SetTrayDisplay(TrayDisplay.Percent));

        menu.Items.Add(open);
        menu.Items.Add(_miniGraphItem);
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

        RenderIcon("…", System.Drawing.Color.White);
    }

    private static void SetTrayDisplay(TrayDisplay d)
    {
        AppSettings.Current.TrayDisplay = d;
        AppSettings.Save();
    }

    private void SyncMenuChecks()
    {
        _miniGraphItem.Checked = AppSettings.Current.MiniGraphEnabled;
        _trayWattsItem.Checked = AppSettings.Current.TrayDisplay == TrayDisplay.Watts;
        _trayPercentItem.Checked = AppSettings.Current.TrayDisplay == TrayDisplay.Percent;
    }

    public void Update(PowerSample s, Estimates est)
    {
        string text;
        System.Drawing.Color color;

        if (!s.HasBattery)
        {
            text = s.CpuPackageW is double cpu ? FormatWatts(cpu) : "PC";
            color = System.Drawing.Color.White;
        }
        else if (AppSettings.Current.TrayDisplay == TrayDisplay.Percent)
        {
            text = Math.Round(s.BatteryPercent).ToString("0");
            color = s.Charging ? System.Drawing.Color.LimeGreen
                  : s.BatteryPercent < 20 ? System.Drawing.Color.OrangeRed
                  : System.Drawing.Color.White;
        }
        else if (s.Discharging)
        {
            // on battery: discharge rate IS total system draw (measured)
            text = FormatWatts(s.DischargeRateW);
            color = s.DischargeRateW > 60 ? System.Drawing.Color.OrangeRed : System.Drawing.Color.Orange;
        }
        else if (s.AcOnline)
        {
            // on AC the battery flow is trickle noise — show estimated system draw instead
            // (the power-budget number), falling back to CPU package watts pre-baseline
            var w = est.EstSystemW ?? s.CpuPackageW;
            text = w is double sys ? FormatWatts(sys) : "AC";
            color = s.Charging ? System.Drawing.Color.LimeGreen : System.Drawing.Color.LightSkyBlue;
        }
        else
        {
            text = FormatWatts(Math.Abs(s.NetW));
            color = System.Drawing.Color.LightGray;
        }

        var key = text + color.ToArgb();
        if (key != _lastRendered)
        {
            RenderIcon(text, color);
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

    private static string FormatWatts(double w) =>
        w < 9.95 ? w.ToString("0.#") : Math.Round(w).ToString("0");

    private void RenderIcon(string text, System.Drawing.Color color)
    {
        var size = 32; // shell scales down; rendering large keeps digits crisp on high DPI
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(System.Drawing.Color.Transparent);
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
            var fontSize = text.Length switch
            {
                <= 2 => size * 0.72f,
                3 => size * 0.52f,
                _ => size * 0.44f,
            };
            using var font = new Font("Segoe UI", fontSize, System.Drawing.FontStyle.Bold, GraphicsUnit.Pixel);
            using var brush = new SolidBrush(color);
            var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(text, font, brush, new RectangleF(0, 0, size, size + 1), sf);
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
