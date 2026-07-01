using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PowerMonitor.Models;

public enum PowerUnit { Watts, Milliwatts }
public enum EnergyUnit { WattHours, MilliampHours }
public enum TrayDisplay { Watts, Percent }

/// <summary>Persisted user preferences. JSON in %LocalAppData%\PowerMonitor\settings.json.</summary>
public sealed class AppSettings
{
    public double SamplingIntervalSeconds { get; set; } = 1.0;
    public PowerUnit PowerUnit { get; set; } = PowerUnit.Watts;
    public EnergyUnit EnergyUnit { get; set; } = EnergyUnit.WattHours;
    public TrayDisplay TrayDisplay { get; set; } = TrayDisplay.Watts;
    public bool CloseToTray { get; set; } = true;
    public bool StartMinimized { get; set; }
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

    // ---- persistence ----

    public static string Dir { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PowerMonitor");

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
