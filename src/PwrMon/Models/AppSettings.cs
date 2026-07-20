using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PwrMon.Models;

public enum PowerUnit { Watts, Milliwatts }
public enum EnergyUnit { WattHours, MilliampHours }
public enum TrayDisplay { Watts, Percent }

/// <summary>Persisted user preferences. JSON in %LocalAppData%\PwrMon\settings.json.</summary>
public sealed class AppSettings
{
    public double SamplingIntervalSeconds { get; set; } = 1.0;
    public PowerUnit PowerUnit { get; set; } = PowerUnit.Watts;
    public EnergyUnit EnergyUnit { get; set; } = EnergyUnit.WattHours;
    public TrayDisplay TrayDisplay { get; set; } = TrayDisplay.Watts;
    public bool CloseToTray { get; set; } = true;
    public bool StartMinimized { get; set; }
    /// <summary>Slim mode: the dashboard window is created on demand and fully disposed on
    /// close, so the resident footprint is just tray + sampler (+ mini-graph).</summary>
    public bool SlimMode { get; set; }
    public int HistoryRetentionDays { get; set; } = 7;

    public bool MiniGraphEnabled { get; set; }
    public double MiniGraphX { get; set; } = double.NaN;
    public double MiniGraphY { get; set; } = double.NaN;
    public int MiniGraphOpacityPct { get; set; } = 85;
    public int MiniGraphWindowSeconds { get; set; } = 120;
    public bool MiniGraphClickThrough { get; set; }

    public bool ChartShowNet { get; set; } = true;
    public bool ChartShowCpu { get; set; } = true;
    public bool ChartShowGpu { get; set; } = true;
    public bool ChartShowPercent { get; set; } = true;
    public bool ChartShowCpuLoad { get; set; }
    public int ChartRangeMinutes { get; set; } = 15;

    public double WindowWidth { get; set; } = 1100;
    public double WindowHeight { get; set; } = 780;

    public string Theme { get; set; } = "Phosphor";
    /// <summary>Font family for the large live numerals (needs stable digit widths).</summary>
    public string NumeralFont { get; set; } = "Bahnschrift";
    /// <summary>Font family for all interface text (labels, titles, body).</summary>
    public string TextFont { get; set; } = "Segoe UI";

    /// <summary>Comma-separated card keys in display order; empty = default layout.</summary>
    public string CardOrder { get; set; } = "";

    /// <summary>Learned "system minus CPU package" draw in watts, measured during battery
    /// sessions and used to estimate total system/wall draw on AC. NaN until learned.</summary>
    public double LearnedSystemBaselineW { get; set; } = double.NaN;

    // ---- persistence ----

    public static string Dir { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PwrMon");

    private static string FilePath => Path.Combine(Dir, "settings.json");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        // MiniGraphX/Y default to NaN ("not yet placed"), which JSON rejects without this
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    [JsonIgnore]
    public static AppSettings Current { get; private set; } = new();

    /// <summary>Raised on the thread that called <see cref="Save"/>.</summary>
    public static event Action? Changed;

    public static void Load()
    {
        try
        {
            if (File.Exists(FilePath))
                Current = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOpts) ?? new AppSettings();
        }
        catch (Exception ex)
        {
            Services.Log.Error($"settings load failed, using defaults: {ex.Message}");
            Current = new AppSettings();
        }
        Current.SamplingIntervalSeconds = Math.Clamp(Current.SamplingIntervalSeconds, 0.5, 10);
        Current.HistoryRetentionDays = Math.Clamp(Current.HistoryRetentionDays, 1, 60);
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(Current, JsonOpts));
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch (Exception ex)
        {
            Services.Log.Error($"settings save failed: {ex.Message}");
        }
        Changed?.Invoke();
    }
}
