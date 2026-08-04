using PwrMon.Models;

namespace PwrMon.Services;

/// <summary>Formats power/energy/time values according to the user's unit preferences.</summary>
public static class UnitFormatter
{
    /// <summary>How long a raw fuel-gauge reading can hold its value before the display stops
    /// implying it's fresh. The gauge's own publish cadence is ~15–30 s (quantized, observed);
    /// this sits between the two so a genuinely unmoving reading is flagged promptly without
    /// falsely tripping on a normal-length gap between real updates.</summary>
    public const int StaleAfterSeconds = 20;

    /// <summary>True once a reading has held its value long enough that it's more likely the
    /// fuel gauge simply hasn't republished than that the draw is unchanged to the milliwatt.</summary>
    public static bool IsStale(TimeSpan age) => age.TotalSeconds > StaleAfterSeconds;

    /// <summary><paramref name="stale"/> drops Watts-mode precision to whole watts — a
    /// multimeter wouldn't print a tenths digit off a reading it knows hasn't refreshed. See
    /// MULTIMETER-STUDY.md §7.1.</summary>
    public static string Power(double watts, bool signed = false, bool stale = false)
    {
        var sign = signed && watts > 0.05 ? "+" : "";
        if (AppSettings.Current.PowerUnit == PowerUnit.Milliwatts)
            return $"{sign}{watts * 1000:N0} mW";
        var abs = Math.Abs(watts);
        var decimals = stale ? 0 : abs < 10 ? 2 : 1;
        return $"{sign}{watts.ToString($"F{decimals}")} W";
    }

    /// <summary>Energy amount; mAh conversion uses the supplied voltage (falls back to 11.1 V).</summary>
    public static string Energy(double wh, double voltageV)
    {
        if (AppSettings.Current.EnergyUnit == EnergyUnit.MilliampHours)
        {
            var v = voltageV > 1 ? voltageV : 11.1;
            return $"{wh / v * 1000:N0} mAh";
        }
        return $"{wh:F1} Wh";
    }

    public static string Duration(TimeSpan? t)
    {
        if (t is null) return "—";
        if (t.Value.TotalHours >= 24) return "> 1 day";
        if (t.Value.TotalMinutes < 1) return "< 1 min";
        return $"{(int)t.Value.TotalHours}:{t.Value.Minutes:D2}";
    }

    public static string Percent(double pct) => $"{pct:F1}%";
}
