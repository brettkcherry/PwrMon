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
    string SeriesWall,
    string MiniBg);

/// <summary>
/// Runtime theming: every themed color lives as a SolidColorBrush in App resources; applying a
/// palette mutates the brush colors in place so all StaticResource consumers update live.
/// Chart/mini-graph colors are pushed via the <see cref="Changed"/> event.
/// </summary>
public static class ThemeService
{
    // Wall-input series colour (SeriesWall), added 2026-08-13. Picked per theme rather than
    // reusing an existing slot: wall is the outermost envelope on the watts axis and is often
    // drawn directly above Net and CPU, so it has to separate from both at a glance. The rule
    // followed for every palette below: take the one hue family the theme wasn't already
    // spending on a series, and keep it at similar luminance to Net so neither dominates.
    public static readonly ThemePalette[] All =
    {
        new("Volt", true, "#0F1115", "#171A21", "#242A36", "#E8EAF0", "#8B93A7",
            "#F5B62E", "#3FB950", "#F0883E", "#F85149", "#58A6FF",
            "#171A21", "#12151B", "#232833",
            "#F5B62E", "#58A6FF", "#BC8CFF", "#3FB950", "#8B93A7",
            "#4FD1C5", "#EE12151B"),          // teal: the gap between Volt's amber and blue
        new("Glacier", true, "#1B1F27", "#20252F", "#2C3340", "#E4EAF2", "#8894A8",
            "#7FB3E8", "#A3BE8C", "#D08770", "#BF616A", "#81A1C1",
            "#20252F", "#161A21", "#262C38",
            "#7FB3E8", "#B48EAD", "#88C0D0", "#A3BE8C", "#6C7686",
            "#EBCB8B", "#EE161A21"),          // Nord's own aurora yellow, the unused one
        new("Synth", true, "#16101F", "#1D1529", "#2E2140", "#EFE6F7", "#9C8FB3",
            "#E86BB0", "#6BE8A3", "#E8A36B", "#E85C6B", "#9D7BE8",
            "#1D1529", "#120D1A", "#2E2140",
            "#E86BB0", "#9D7BE8", "#6BC8E8", "#6BE8A3", "#8A7F9E",
            "#E8D26B", "#EE120D1A"),          // the missing corner of Synth's neon wheel
        new("Phosphor", true, "#0A0F0A", "#101710", "#1D2B1D", "#D8F0D8", "#6FA070",
            "#4AE05C", "#4AE05C", "#E0B34A", "#E05C4A", "#4AC8E0",
            "#101710", "#0C120C", "#1D2B1D",
            "#4AE05C", "#4AC8E0", "#A8E04A", "#B968E8", "#557055",
            "#4AE0B3", "#EE0C120C"),          // Pct: violet — was #2E8B3D, same green family as Net
                                               // (bright vs dark shade of one CRT phosphor hue, same
                                               // failure as Meadow's Net/Pct). Amber gold was the
                                               // obvious swap but Drive °C already owns amber
                                               // (#E0B34A) on this same right-hand axis, so violet —
                                               // the one hue nothing else in Phosphor touches. Spring
                                               // green-teal wall sits between CRT green and cyan.
        new("OLED void", true, "#000000", "#0C0C0E", "#1E1E22", "#E8EAF0", "#77808F",
            "#F5B62E", "#3FB950", "#F0883E", "#F85149", "#58A6FF",
            "#000000", "#060608", "#17181C",
            "#F5B62E", "#58A6FF", "#BC8CFF", "#3FB950", "#8B93A7",
            "#4FD1C5", "#F2000000"),          // same family as Volt, its parent palette
        // Redshift — night-watch red: the lighting an observatory dome or a submarine control
        // room switches to before dark work ("rig for red"), because long-wavelength light spares
        // the eye's dark adaptation while blue-white light destroys it. So: no blue-white
        // anywhere — the background is a red-cast black, the text is warm rose rather than
        // white, and the whole palette lives on the red→amber arc. It stops short of true
        // monochrome for the same reason Phosphor does: Green/Orange/Red/Blue carry battery
        // *state* (charging / discharging / alarm / idle), and collapsing them into one hue
        // would cost real meaning. They're separated within the warm band instead — amber for
        // good, burnt orange for drain, hot pink-red for alarm, quiet rose for idle — with
        // one nebula violet for the iGPU series, the single concession to legibility.
        new("Redshift", true, "#0E0608", "#170A0D", "#2E1418", "#F2CBC4", "#B27B75",
            "#FF5A45", "#F5C860", "#D9822B", "#FF3355", "#E58BB0",
            "#170A0D", "#0B0406", "#2A1216",
            "#FF5A45", "#E58BB0", "#B368D6", "#F5C860", "#B27B75",
            "#FF9E7A", "#EE0B0406"),          // pale peach — stays inside the no-blue-light rule

        // ─── 3 mid-tone themes: genuine middle luminance, not a dimmed dark or tinted light ───
        new("Dusk", true, "#574C63", "#665A73", "#7A6D87", "#F3EDF7", "#C8BBD2",
            "#F291CC", "#7FE0A8", "#F0A868", "#F27878", "#8FB8F0",
            "#665A73", "#5C5069", "#8A7D97",
            "#F291CC", "#8FB8F0", "#B79AE8", "#7FE0A8", "#C8BBD2",
            "#F0D48A", "#EE665A73"),          // warm gold against an all-cool series set
        new("Slate", true, "#5B6169", "#666D76", "#7A828C", "#F2F4F6", "#C7CDD4",
            "#6FB2E8", "#7ED09A", "#E8A468", "#E8807A", "#4E8FC7",
            "#666D76", "#5F656D", "#7A828C",
            "#6FB2E8", "#A98FBD", "#6FCBD6", "#7ED09A", "#C7CDD4",
            "#E0C088", "#EE666D76"),          // sand: the only warm note on a cool-grey field
        new("Canyon", true, "#6B5D52", "#786A5D", "#8C7D6E", "#FBF3EA", "#D4C4B4",
            "#F0B255", "#8FC98A", "#E8935A", "#E27D6E", "#7FAAD6",
            "#786A5D", "#6F6155", "#8C7D6E",
            "#F0B255", "#7FAAD6", "#B79AC4", "#8FC98A", "#D4C4B4",
            "#5FBFB0", "#EE786A5D"),          // desert teal — the cool counterweight to the clay

        // ─── Light themes last, deliberately ───
        // The picker renders this array in order, and people cycle it top-to-bottom with a
        // dark theme already applied and their eyes adapted to it. A white background part-way
        // down the list is a flashbang; parked at the end, the list ramps monotonically from
        // near-black up to white, so the only bright step is the one you opt into.
        // Paper belongs here too — it's the oldest of the four, but it's every bit as bright.
        // Light themes need the wall colour dark enough to read on white, so these are
        // deeper/more saturated than their dark-theme counterparts rather than lighter.
        new("Paper", false, "#F5F2EA", "#FFFFFF", "#DDD6C7", "#3D3929", "#8A8471",
            "#C15F3C", "#3B6D11", "#BA7517", "#A32D2D", "#185FA5",
            "#FFFFFF", "#FBF9F3", "#E8E2D4",
            "#C15F3C", "#185FA5", "#7F77DD", "#3B6D11", "#888780",
            "#0F7B7B", "#F2FFFFFF"),          // deep teal, ink-weight on paper white
        // 3 light siblings, riffing on existing dark families rather than new directions
        new("Chalk", false, "#F3F3EF", "#FFFFFF", "#E2E0D4", "#2B2B26", "#8D8C82",
            "#B8860B", "#2E7D32", "#BF5B22", "#B33A3A", "#2568B0",
            "#FFFFFF", "#FAFAF6", "#E4E2D6",
            "#B8860B", "#2568B0", "#7B5FBF", "#2E7D32", "#8D8C82",
            "#A03E6F", "#F2FFFFFF"),          // plum: no magenta anywhere else in Chalk
        new("Frost", false, "#EFF3F7", "#FFFFFF", "#D7E1EA", "#223140", "#7C8B99",
            "#2472A4", "#4C9B76", "#B4652C", "#B23B3B", "#3E7CB1",
            "#FFFFFF", "#F7FAFC", "#DEE7EE",
            "#2472A4", "#9B7CA8", "#4FA8AE", "#4C9B76", "#7C8B99",
            "#9A7B1F", "#F2FFFFFF"),          // dark gold, the one warm hue Frost lacks
        new("Meadow", false, "#F1F6EE", "#FFFFFF", "#DCE7D5", "#263A22", "#7E8F76",
            "#2F7A32", "#2F7A32", "#B36A1E", "#B23A34", "#2E6FA8",
            "#FFFFFF", "#F8FBF6", "#E1EAD9",
            "#2F7A32", "#2C7C93", "#6E9B2E", "#C9A227", "#7E8F76",
            "#7A4A96", "#F2FFFFFF"),          // Pct: mustard gold — was #1F5C28, too close to Net's
                                               // dark green (both plotted lines, near-identical
                                               // hue and lightness). Violet wall unchanged.
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
        SetBrush("GpuBrush", palette.SeriesGpu);
        SetBrush("WallBrush", palette.SeriesWall);

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

    // Researched, not guessed: ranked by actual 2026 usage/ranking data for this app's
    // audience (Windows power users who already have dev tooling installed). Top tier is
    // the 2026 "everyone actually uses this" set — JetBrains Mono and Fira Code are the
    // #1/#2 most-used programming fonts industry-wide, Cascadia Mono/Code ships with Windows
    // Terminal and is bundled in Windows 11, Geist Mono is Vercel's fast-rising 2026 pick.
    // Next tier is Windows-native (Consolas ships with Windows/Office, Bahnschrift ships
    // with Windows itself — kept as the app default, Courier New is on every Windows
    // install ever). The rest are long-established, widely-distributed staples (Source Code
    // Pro, IBM Plex Mono, Roboto Mono, Ubuntu Mono, Space Mono, Inconsolata, Hack, Noto Sans
    // Mono, PT Mono, Overpass Mono, Red Hat Mono, DejaVu Sans Mono, Liberation Mono,
    // Anonymous Pro, Cousine, Input Mono, Victor Mono). No Nerd Font glyph-patched variants
    // (irrelevant outside a terminal), no macOS-only faces (Menlo/Monaco/SF Mono — never
    // installed on this Windows app's machines), no novelty/display faces, and — structurally,
    // not just by omission — no symbol/icon/dingbat fonts: see the blocklist in
    // <see cref="InstalledFonts"/>, which every list and the font search both draw from, so
    // a symbol font can't surface here even by typing its name.
    private static readonly string[] CuratedNumeralCandidates =
    {
        "JetBrains Mono", "Fira Code", "Cascadia Code", "Cascadia Mono", "Geist Mono",
        "Consolas", "Bahnschrift", "Courier New",
        "Source Code Pro", "IBM Plex Mono", "Roboto Mono", "Ubuntu Mono", "Space Mono",
        "Inconsolata", "Hack", "Noto Sans Mono", "PT Mono", "Overpass Mono", "Red Hat Mono",
        "DejaVu Sans Mono", "Liberation Mono", "Anonymous Pro", "Cousine", "Input Mono",
        "Victor Mono",
    };

    // Interface text: top tier is the 2026 "most-cited for SaaS/dashboard UI" set — Inter is
    // the current #1 UI/dashboard sans industry-wide, followed by Roboto, DM Sans, Manrope,
    // Plus Jakarta Sans, Geist (Vercel's UI sans) and Instrument Sans. Next tier is Windows-
    // native (Segoe UI ships with Windows itself — kept as the app default; Calibri, Corbel
    // and Candara are Microsoft's ClearType Font Collection, designed specifically for screen
    // legibility and on virtually every Windows machine). The rest are long-established,
    // near-ubiquitous web/UI staples (Open Sans, Noto Sans, IBM Plex Sans, Work Sans, Space
    // Grotesk, Source Sans Pro, Lato, Nunito Sans, Rubik, Karla, Mulish), plus Cascadia Code
    // and JetBrains Mono kept so a mono-everywhere look stays selectable in both pickers. No
    // serif faces (off this app's sans-only HUD aesthetic), no novelty/display faces, and —
    // structurally — no symbol/icon fonts; see the blocklist in <see cref="InstalledFonts"/>.
    private static readonly string[] CuratedTextCandidates =
    {
        "Inter", "Roboto", "DM Sans", "Manrope", "Plus Jakarta Sans", "Geist", "Instrument Sans",
        "Segoe UI", "Calibri", "Corbel", "Candara",
        "Open Sans", "Noto Sans", "IBM Plex Sans", "Work Sans", "Space Grotesk",
        "Source Sans Pro", "Lato", "Nunito Sans", "Rubik", "Karla", "Mulish",
        "Cascadia Code", "JetBrains Mono",
    };

    // Symbol/icon/dingbat fonts should never be a choice a user can land on — not in the
    // curated list, and not via search either, since search draws from the same source.
    // Matched as a case-insensitive substring against the family name.
    private static readonly string[] BlockedFontSubstrings =
    {
        "Wingdings", "Webdings", "Symbol", "Marlett", "MT Extra", "Bookshelf Symbol",
        "MDL2 Assets", "Fluent Icons", "Emoji", "Dingbats", "MS Outlook",
        "MS Reference Specialty", "OpenSymbol",
    };

    /// <summary>Curated numeral-font picks that are actually installed on this machine.</summary>
    public static IEnumerable<string> CuratedNumeralFonts() => Curated(CuratedNumeralCandidates);

    /// <summary>Curated interface-font picks that are actually installed on this machine.</summary>
    public static IEnumerable<string> CuratedTextFonts() => Curated(CuratedTextCandidates);

    private static IEnumerable<string> Curated(string[] candidates)
    {
        var installed = new HashSet<string>(InstalledFonts(), StringComparer.OrdinalIgnoreCase);
        return candidates.Where(installed.Contains).OrderBy(n => n, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Every installed font family name a user is allowed to reach — via the curated
    /// list or via search. Symbol/icon/dingbat fonts are filtered out here, structurally,
    /// rather than just left out of the curated picks, so there's no path (typed search
    /// included) that can land on one.</summary>
    public static IEnumerable<string> InstalledFonts() =>
        System.Windows.Media.Fonts.SystemFontFamilies
            .Select(f => f.Source)
            .Where(n => !n.StartsWith("Global", StringComparison.Ordinal))
            .Where(n => !BlockedFontSubstrings.Any(b => n.Contains(b, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase);

    // Every consumer now binds via DynamicResource (not StaticResource), so a plain replace
    // is enough — WPF re-resolves DynamicResource lookups by key on any change. The old
    // "mutate the existing brush in place, unless frozen" approach was the actual bug behind
    // "themes only change the chart, never the cards": WPF freezes a Style's Setter values
    // (Freezable objects) once that Style is sealed — which happens the first time it's
    // applied to an element — so after the Card/StatValue/etc. styles were sealed, this used
    // to silently fall into the "replace" branch every time, but every StaticResource that
    // had already resolved to the original (now-orphaned) brush instance never saw it.
    private static void SetBrush(string key, string hex) =>
        Application.Current.Resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));

    public static Color ParseColor(string hex) => (Color)ColorConverter.ConvertFromString(hex);
}
