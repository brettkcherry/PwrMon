using PwrMon.Services;

namespace PwrMon.Tests;

/// <summary>
/// Palette invariants. These exist because the series colours are hand-picked per theme and
/// there are thirteen of them: adding a series (wall input, 2026-08-13) means thirteen new
/// judgement calls, and "it looked fine in Volt" is not evidence about Meadow. Distance is
/// measured in CIELAB ΔE rather than RGB, because RGB distance badly misjudges how separable
/// two colours actually look.
/// </summary>
public class ThemePaletteTests
{
    /// <summary>ΔE below ~15 is where two lines on a chart start being mistakable for one
    /// another at 1–2 px stroke width. Not a perceptual-science threshold — a floor chosen to
    /// catch a careless pick, which is what this guards against.</summary>
    private const double MinDeltaE = 15;

    public static TheoryData<string> ThemeNames()
    {
        var data = new TheoryData<string>();
        foreach (var t in ThemeService.All) data.Add(t.Name);
        return data;
    }

    [Theory]
    [MemberData(nameof(ThemeNames))]
    public void Every_watts_axis_series_is_visually_separable(string themeName)
    {
        var t = ThemeService.All.Single(p => p.Name == themeName);

        // The left-axis (watts) series are the ones drawn on top of each other and therefore
        // the ones that must never collide. Percent/load live on the right axis in their own
        // 0–105 band, but they're included anyway: they still share the plot area. CPU/Drive
        // temperature reuse the theme's global Red/Orange (MainWindow.ApplyChartTheme) rather
        // than a dedicated series slot — and share the right-hand axis with Pct/Load — so a
        // palette can pass "the six named series are distinct" while Pct still lands on top of
        // Drive °C. Caught by hand once already (Phosphor's fix nearly picked amber for Pct,
        // which is exactly Drive °C's color); this closes the gap instead of relying on eyes.
        var series = new (string Name, string Hex)[]
        {
            ("net", t.SeriesNet), ("cpu", t.SeriesCpu), ("gpu", t.SeriesGpu),
            ("pct", t.SeriesPct), ("load", t.SeriesLoad), ("wall", t.SeriesWall),
            ("cpuTemp", t.Red), ("driveTemp", t.Orange),
        };

        var failures = new List<string>();
        for (var i = 0; i < series.Length; i++)
            for (var j = i + 1; j < series.Length; j++)
            {
                // Phosphor deliberately reuses one green for Accent and Green; series slots
                // are still expected to be distinct from each other.
                var d = DeltaE(series[i].Hex, series[j].Hex);
                if (d < MinDeltaE)
                    failures.Add($"{series[i].Name}({series[i].Hex}) vs {series[j].Name}({series[j].Hex}) ΔE={d:F1}");
            }

        Assert.True(failures.Count == 0, $"{themeName}: {string.Join("; ", failures)}");
    }

    [Theory]
    [MemberData(nameof(ThemeNames))]
    public void Wall_series_is_readable_against_the_plot_background(string themeName)
    {
        var t = ThemeService.All.Single(p => p.Name == themeName);
        var d = DeltaE(t.SeriesWall, t.ChartData);
        Assert.True(d > 25, $"{themeName}: wall {t.SeriesWall} on data background {t.ChartData} is ΔE={d:F1}");
    }

    // ── CIELAB (D65) ──

    private static double DeltaE(string hexA, string hexB)
    {
        var a = Lab(hexA);
        var b = Lab(hexB);
        return Math.Sqrt(Math.Pow(a.L - b.L, 2) + Math.Pow(a.A - b.A, 2) + Math.Pow(a.B - b.B, 2));
    }

    private static (double L, double A, double B) Lab(string hex)
    {
        // tolerate the #AARRGGBB form used by the mini-graph background entries
        var h = hex.TrimStart('#');
        if (h.Length == 8) h = h[2..];
        var r = Linear(Convert.ToInt32(h[..2], 16) / 255.0);
        var g = Linear(Convert.ToInt32(h.Substring(2, 2), 16) / 255.0);
        var b = Linear(Convert.ToInt32(h.Substring(4, 2), 16) / 255.0);

        var x = F((r * 0.4124 + g * 0.3576 + b * 0.1805) / 0.95047);
        var y = F(r * 0.2126 + g * 0.7152 + b * 0.0722);
        var z = F((r * 0.0193 + g * 0.1192 + b * 0.9505) / 1.08883);

        return (116 * y - 16, 500 * (x - y), 200 * (y - z));
    }

    private static double Linear(double c) => c > 0.04045 ? Math.Pow((c + 0.055) / 1.055, 2.4) : c / 12.92;
    private static double F(double c) => c > 0.008856 ? Math.Pow(c, 1.0 / 3.0) : 7.787 * c + 16.0 / 116.0;
}
