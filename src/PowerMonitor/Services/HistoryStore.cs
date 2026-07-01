using System.Globalization;
using System.IO;
using System.Text;
using PowerMonitor.Models;

namespace PowerMonitor.Services;

/// <summary>
/// Persists samples and power events to daily CSV files under
/// %LocalAppData%\PowerMonitor\history and reloads recent history on startup so
/// the chart survives restarts. Writes are buffered and flushed every ~15 s.
/// </summary>
public sealed class HistoryStore : IDisposable
{
    private const string Header = "time,charge_w,discharge_w,cpu_w,igpu_w,cpu_load,percent,remaining_wh,voltage_v,ac,gap,platform_w";
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private readonly object _gate = new();
    private readonly StringBuilder _buffer = new();
    private DateTime _lastFlush = DateTime.UtcNow;
    private string? _bufferDay;

    public static string HistoryDir => Path.Combine(AppSettings.Dir, "history");
    private static string EventsFile => Path.Combine(HistoryDir, "events.csv");

    public void Append(PowerSample s)
    {
        var line = string.Create(Inv,
            $"{s.Time:yyyy-MM-ddTHH:mm:ss.fffzzz},{s.ChargeRateW:F3},{s.DischargeRateW:F3},{Opt(s.CpuPackageW)},{Opt(s.IGpuW)},{Opt(s.CpuLoadPct)},{s.BatteryPercent:F2},{s.RemainingWh:F3},{s.VoltageV:F3},{(s.AcOnline ? 1 : 0)},{(s.GapBefore ? 1 : 0)},{Opt(s.CpuPlatformW)}");
        lock (_gate)
        {
            _buffer.AppendLine(line);
            _bufferDay ??= s.Time.ToString("yyyy-MM-dd");
            if ((DateTime.UtcNow - _lastFlush).TotalSeconds >= 15 || s.Time.ToString("yyyy-MM-dd") != _bufferDay)
                FlushLocked();
        }
    }

    public void AppendEvent(PowerEvent e)
    {
        try
        {
            lock (_gate)
            {
                Directory.CreateDirectory(HistoryDir);
                File.AppendAllText(EventsFile,
                    string.Create(Inv, $"{e.Time:yyyy-MM-ddTHH:mm:ss.fffzzz},{e.Kind}{Environment.NewLine}"));
            }
        }
        catch (Exception ex) { Log.Error("event append", ex); }
    }

    public void Flush()
    {
        lock (_gate) FlushLocked();
    }

    private void FlushLocked()
    {
        if (_buffer.Length == 0) return;
        try
        {
            Directory.CreateDirectory(HistoryDir);
            var file = Path.Combine(HistoryDir, $"history-{_bufferDay}.csv");
            var addHeader = !File.Exists(file);
            using var w = new StreamWriter(file, append: true);
            if (addHeader) w.WriteLine(Header);
            w.Write(_buffer.ToString());
            _buffer.Clear();
            _bufferDay = null;
            _lastFlush = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            Log.Error("history flush", ex);
            _buffer.Clear(); // don't grow unbounded on a broken disk
        }
    }

    /// <summary>Loads samples from the last <paramref name="hours"/> hours across daily files.</summary>
    public List<PowerSample> LoadRecent(double hours = 48)
    {
        var result = new List<PowerSample>();
        var cutoff = DateTimeOffset.Now.AddHours(-hours);
        try
        {
            if (!Directory.Exists(HistoryDir)) return result;
            var files = Directory.GetFiles(HistoryDir, "history-*.csv").OrderBy(f => f).TakeLast(4);
            foreach (var file in files)
            {
                foreach (var line in File.ReadLines(file))
                {
                    if (line.Length == 0 || line[0] == 't') continue; // header
                    var s = ParseLine(line);
                    if (s is not null && s.Time >= cutoff) result.Add(s);
                }
            }
            result.Sort((a, b) => a.Time.CompareTo(b.Time));
        }
        catch (Exception ex) { Log.Error("history load", ex); }
        return result;
    }

    public List<PowerEvent> LoadRecentEvents(double hours = 48)
    {
        var result = new List<PowerEvent>();
        var cutoff = DateTimeOffset.Now.AddHours(-hours);
        try
        {
            if (!File.Exists(EventsFile)) return result;
            foreach (var line in File.ReadLines(EventsFile))
            {
                var parts = line.Split(',');
                if (parts.Length < 2) continue;
                if (DateTimeOffset.TryParse(parts[0], Inv, DateTimeStyles.None, out var t) &&
                    Enum.TryParse<PowerEventKind>(parts[1], out var kind) && t >= cutoff)
                    result.Add(new PowerEvent(t, kind));
            }
        }
        catch (Exception ex) { Log.Error("events load", ex); }
        return result;
    }

    private static PowerSample? ParseLine(string line)
    {
        try
        {
            var p = line.Split(',');
            if (p.Length < 11) return null;
            var charge = double.Parse(p[1], Inv);
            var discharge = double.Parse(p[2], Inv);
            return new PowerSample
            {
                Time = DateTimeOffset.Parse(p[0], Inv),
                HasBattery = true,
                ChargeRateW = charge,
                DischargeRateW = discharge,
                Charging = charge > 0.01,
                Discharging = discharge > 0.01,
                CpuPackageW = OptParse(p[3]),
                IGpuW = OptParse(p[4]),
                CpuLoadPct = OptParse(p[5]),
                BatteryPercent = double.Parse(p[6], Inv),
                RemainingWh = double.Parse(p[7], Inv),
                VoltageV = double.Parse(p[8], Inv),
                AcOnline = p[9] == "1",
                GapBefore = p[10] == "1",
                CpuPlatformW = p.Length > 11 ? OptParse(p[11]) : null,
            };
        }
        catch { return null; }
    }

    /// <summary>Deletes daily files older than the retention window.</summary>
    public void CleanupOldFiles()
    {
        try
        {
            if (!Directory.Exists(HistoryDir)) return;
            var cutoff = DateTime.Now.AddDays(-AppSettings.Current.HistoryRetentionDays);
            foreach (var file in Directory.GetFiles(HistoryDir, "history-*.csv"))
            {
                var name = Path.GetFileNameWithoutExtension(file); // history-yyyy-MM-dd
                if (DateTime.TryParseExact(name[8..], "yyyy-MM-dd", Inv, DateTimeStyles.None, out var day) &&
                    day < cutoff)
                {
                    File.Delete(file);
                    Log.Info($"retention: deleted {name}");
                }
            }
        }
        catch (Exception ex) { Log.Error("retention cleanup", ex); }
    }

    /// <summary>Copies all persisted samples in [from, to] into one CSV for the user.</summary>
    public int ExportRange(DateTimeOffset from, DateTimeOffset to, string destination)
    {
        Flush();
        var rows = 0;
        using var w = new StreamWriter(destination, append: false);
        w.WriteLine(Header);
        if (Directory.Exists(HistoryDir))
        {
            foreach (var file in Directory.GetFiles(HistoryDir, "history-*.csv").OrderBy(f => f))
            {
                foreach (var line in File.ReadLines(file))
                {
                    if (line.Length == 0 || line[0] == 't') continue;
                    var comma = line.IndexOf(',');
                    if (comma <= 0) continue;
                    if (DateTimeOffset.TryParse(line[..comma], Inv, DateTimeStyles.None, out var t) &&
                        t >= from && t <= to)
                    {
                        w.WriteLine(line);
                        rows++;
                    }
                }
            }
        }
        return rows;
    }

    private static string Opt(double? v) => v?.ToString("F3", Inv) ?? "";

    private static double? OptParse(string s) =>
        s.Length > 0 && double.TryParse(s, NumberStyles.Float, Inv, out var v) ? v : null;

    public void Dispose() => Flush();
}
