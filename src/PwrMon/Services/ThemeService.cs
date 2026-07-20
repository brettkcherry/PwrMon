using System.Windows;
using System.Windows.Media;
using PwrMon.Models;

namespace PwrMon.Services;

public sealed record ThemePalette(
    string Name, bool IsDark,
    string Bg, string Card, string CardBorder, string Text, string TextDim,
    string Accent, string Green, string Orange, string Red, string Blue,
    string ChartFigure, string ChartData, string ChartGrid,
    string SeriesNet, string SeriesCpu, string SeriesGpu, string SeriesPct, string SeriesLoad,
    string MiniBg);

/// <summary>
/// Runtime theming: every themed color lives as a SolidColorBrush in App resources; applying a
/// palette mutates the brush colors in place so all StaticResource consumers update live.
/// Chart/mini-graph colors are pushed via the <see cref="Changed"/> event.
/// </summary>
public static class ThemeService
{
    public static readonly ThemePalette[] All =
    {
        new("Volt", true, "#0F1115", "#171A21", "#242A36", "#E8EAF0", "#8B93A7",
            "#F5B62E", "#3FB950", "#F0883E", "#F85149", "#58A6FF",
            "#171A21", "#12151B", "#232833",
            "#F5B62E", "#58A6FF", "#BC8CFF", "#3FB950", "#8B93A7", "#EE12151B"),
        new("Glacier", true, "#1B1F27", "#20252F", "#2C3340", "#E4EAF2", "#8894A8",
            "#7FB3E8", "#A3BE8C", "#D08770", "#BF616A", "#81A1C1",
            "#20252F", "#161A21", "#262C38",
            "#7FB3E8", "#B48EAD", "#88C0D0", "#A3BE8C", "#6C7686", "#EE161A21"),
        new("Synth", true, "#16101F", "#1D1529", "#2E2140", "#EFE6F7", "#9C8FB3",
            "#E86BB0", "#6BE8A3", "#E8A36B", "#E85C6B", "#9D7BE8",
            "#1D1529", "#120D1A", "#2E2140",
            "#E86BB0", "#9D7BE8", "#6BC8E8", "#6BE8A3", "#8A7F9E", "#EE120D1A"),
        new("Phosphor", true, "#0A0F0A", "#101710", "#1D2B1D", "#D8F0D8", "#6FA070",
            "#4AE05C", "#4AE05C", "#E0B34A", "#E05C4A", "#4AC8E0",
            "#101710", "#0C120C", "#1D2B1D",
            "#4AE05C", "#4AC8E0", "#A8E04A", "#2E8B3D", "#557055", "#EE0C120C"),
        new("OLED void", true, "#000000", "#0C0C0E", "#1E1E22", "#E8EAF0", "#77808F",
            "#F5B62E", "#3FB950", "#F0883E", "#F85149", "#58A6FF",
            "#000000", "#060608", "#17181C",
            "#F5B62E", "#58A6FF", "#BC8CFF", "#3FB950", "#8B93A7", "#F2000000"),
        new("Paper", false, "#F5F2EA", "#FFFFFF", "#DDD6C7", "#3D3929", "#8A8471",
            "#C15F3C", "#3B6D11", "#BA7517", "#A32D2D", "#185FA5",
            "#FFFFFF", "#FBF9F3", "#E8E2D4",
            "#C15F3C", "#185FA5", "#7F77DD", "#3B6D11", "#888780", "#F2FFFFFF"),
    };

    public static ThemePalette Current { get; private set; } = All[0];

    /// <summary>Raised on the UI thread after a palette is applied (windows restyle charts etc.).</summary>
    public static event Action<ThemePalette>? Changed;

    public static void Apply(string name)
    {
        var palette = All.FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) ?? All[0];
        Current = palette;

        SetBrush("BgBrush", palette.Bg);
        SetBrush("CardBrush", palette.Card);
        SetBrush("CardBorderBrush", palette.CardBorder);
        SetBrush("TextBrush", palette.Text);
        SetBrush("TextDimBrush", palette.TextDim);
        SetBrush("AccentBrush", palette.Accent);
        SetBrush("GreenBrush", palette.Green);
        SetBrush("OrangeBrush", palette.Orange);
        SetBrush("RedBrush", palette.Red);
        SetBrush("BlueBrush", palette.Blue);

        Changed?.Invoke(palette);
    }

    public static void ApplyNumeralFont(string family)
    {
        Application.Current.Resources["NumeralFontFamily"] =
            new FontFamily($"{family}, Segoe UI");
    }

    public static void ApplyTextFont(string family)
    {
        Application.Current.Resources["TextFontFamily"] =
            new FontFamily($"{family}, Segoe UI");
    }

    /// <summary>All installed font family names, for the settings pickers.</summary>
    public static IEnumerable<string> InstalledFonts() =>
        System.Windows.Media.Fonts.SystemFontFamilies
            .Select(f => f.Source)
            .Where(n => !n.StartsWith("Global", StringComparison.Ordinal))
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase);

    private static void SetBrush(string key, string hex)
    {
        if (Application.Current.Resources[key] is SolidColorBrush brush && !brush.IsFrozen)
            brush.Color = (Color)ColorConverter.ConvertFromString(hex);
        else
            Application.Current.Resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
    }

    public static Color ParseColor(string hex) => (Color)ColorConverter.ConvertFromString(hex);
}
