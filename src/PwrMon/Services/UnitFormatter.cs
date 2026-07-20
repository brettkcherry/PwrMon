using PwrMon.Models;

namespace PwrMon.Services;

/// <summary>Formats power/energy/time values according to the user's unit preferences.</summary>
public static class UnitFormatter
{
    public static string Power(double watts, bool signed = false)
    {
        var sign = signed && watts > 0.05 ? "+" : "";
        if (AppSettings.Current.PowerUnit == PowerUnit.Milliwatts)
            return $"{sign}{watts * 1000:N0} mW";
        var abs = Math.Abs(watts);
        var decimals = abs < 10 ? 2 : 1;
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
