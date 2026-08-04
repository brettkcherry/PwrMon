using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using PwrMon.Models;
using PwrMon.Services;

namespace PwrMon.Views;

public partial class SettingsWindow : Window
{
    private readonly bool _elevated = App.Current.Hardware.IsElevated;
    private bool _initializing = true;
    private List<string> _allFonts = null!;
    private readonly Dictionary<ComboBox, (ListCollectionView View, List<string> Curated)> _fontCombos = new();

    // Everything the Revert button restores — the live-preview-as-you-go settings (theme,
    // fonts, behavior, retention). Autostart is deliberately excluded: its checkbox writes
    // a real registry/Task Scheduler entry immediately on click, not an in-memory preview,
    // so undoing it belongs to its own confirm flow, not a generic settings revert.
    private sealed record SettingsSnapshot(
        string Theme, string NumeralFont, string TextFont,
        bool CloseToTray, bool StartMinimized, bool SlimMode, int RetentionDays);
    private SettingsSnapshot _snapshot = null!;

    public SettingsWindow()
    {
        InitializeComponent();

        foreach (var t in ThemeService.All)
            CmbTheme.Items.Add(t.Name);
        CmbTheme.SelectedItem = ThemeService.Current.Name;
        CmbTheme.SelectionChanged += (_, _) =>
        {
            if (_initializing || CmbTheme.SelectedItem is not string themeName) return;
            AppSettings.Current.Theme = themeName;
            AppSettings.Save();
            ThemeService.Apply(themeName);
            RefreshRevertButton();
        };
        // ComboBox only fires SelectionChanged on commit (Enter/click), not while arrowing
        // through an open dropdown — advance the selection ourselves so each arrow press
        // previews live through the handler above, same as a click would.
        WireArrowPreview(CmbTheme);

        _allFonts = ThemeService.InstalledFonts().ToList();

        SetupFontCombo(CmbFont, ThemeService.CuratedNumeralFonts().ToList(), _allFonts, AppSettings.Current.NumeralFont, fontName =>
        {
            AppSettings.Current.NumeralFont = fontName;
            AppSettings.Save();
            ThemeService.ApplyNumeralFont(fontName);
        });

        SetupFontCombo(CmbTextFont, ThemeService.CuratedTextFonts().ToList(), _allFonts, AppSettings.Current.TextFont, fontName =>
        {
            AppSettings.Current.TextFont = fontName;
            AppSettings.Save();
            ThemeService.ApplyTextFont(fontName);
        });

        ChkCloseToTray.IsChecked = AppSettings.Current.CloseToTray;
        ChkStartMinimized.IsChecked = AppSettings.Current.StartMinimized;
        ChkSlimMode.IsChecked = AppSettings.Current.SlimMode;

        ChkAutostart.IsChecked = StartupHelper.IsRunKeyEnabled() || StartupHelper.IsElevatedTaskEnabled();
        ChkAutostartElevated.IsChecked = StartupHelper.IsElevatedTaskEnabled();
        ChkAutostartElevated.IsEnabled = _elevated;
        AutostartNote.Text = _elevated
            ? "Creates a Task Scheduler entry with highest privileges."
            : "Run PwrMon as administrator to enable the elevated option.";

        foreach (var d in new[] { 1, 3, 7, 14, 30, 60 })
            CmbRetention.Items.Add(d);
        CmbRetention.SelectedItem = new[] { 1, 3, 7, 14, 30, 60 }
            .OrderBy(d => Math.Abs(d - AppSettings.Current.HistoryRetentionDays)).First();

        HistoryPathText.Text = $"CSV files: {HistoryStore.HistoryDir}";

        var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        AboutVersion.Text = $"PwrMon {v?.Major}.{v?.Minor}.{v?.Build}";

        ChkCloseToTray.Click += (_, _) => SaveBehavior();
        ChkStartMinimized.Click += (_, _) => SaveBehavior();
        ChkSlimMode.Click += (_, _) => SaveBehavior();
        CmbRetention.SelectionChanged += (_, _) => SaveBehavior();

        _snapshot = new SettingsSnapshot(
            AppSettings.Current.Theme, AppSettings.Current.NumeralFont, AppSettings.Current.TextFont,
            AppSettings.Current.CloseToTray, AppSettings.Current.StartMinimized, AppSettings.Current.SlimMode,
            AppSettings.Current.HistoryRetentionDays);

        _initializing = false;
    }

    private void RefreshRevertButton()
    {
        if (_initializing) return;
        var s = AppSettings.Current;
        BtnRevert.IsEnabled = s.Theme != _snapshot.Theme
            || s.NumeralFont != _snapshot.NumeralFont
            || s.TextFont != _snapshot.TextFont
            || s.CloseToTray != _snapshot.CloseToTray
            || s.StartMinimized != _snapshot.StartMinimized
            || s.SlimMode != _snapshot.SlimMode
            || s.HistoryRetentionDays != _snapshot.RetentionDays;
    }

    private void Revert_Click(object sender, RoutedEventArgs e)
    {
        _initializing = true; // restoring controls below shouldn't re-fire their save handlers
        var s = AppSettings.Current;
        s.Theme = _snapshot.Theme;
        s.NumeralFont = _snapshot.NumeralFont;
        s.TextFont = _snapshot.TextFont;
        s.CloseToTray = _snapshot.CloseToTray;
        s.StartMinimized = _snapshot.StartMinimized;
        s.SlimMode = _snapshot.SlimMode;
        s.HistoryRetentionDays = _snapshot.RetentionDays;
        AppSettings.Save();

        ThemeService.Apply(_snapshot.Theme);
        ThemeService.ApplyNumeralFont(_snapshot.NumeralFont);
        ThemeService.ApplyTextFont(_snapshot.TextFont);

        CmbTheme.SelectedItem = _snapshot.Theme;
        SetFontComboSelection(CmbFont, _snapshot.NumeralFont);
        SetFontComboSelection(CmbTextFont, _snapshot.TextFont);
        ChkCloseToTray.IsChecked = _snapshot.CloseToTray;
        ChkStartMinimized.IsChecked = _snapshot.StartMinimized;
        ChkSlimMode.IsChecked = _snapshot.SlimMode;
        CmbRetention.SelectedItem = new[] { 1, 3, 7, 14, 30, 60 }
            .OrderBy(d => Math.Abs(d - _snapshot.RetentionDays)).First();

        _initializing = false;
        BtnRevert.IsEnabled = false;
    }

    /// <summary>Restores a font combo to its snapshot value: curated list (plus that font
    /// pinned in, in case it's outside the curated set) with the font selected.</summary>
    private void SetFontComboSelection(ComboBox combo, string font)
    {
        if (_fontCombos.TryGetValue(combo, out var entry))
        {
            entry.View.Filter = o => entry.Curated.Contains((string)o, StringComparer.OrdinalIgnoreCase)
                                   || string.Equals((string)o, font, StringComparison.OrdinalIgnoreCase);
            entry.View.Refresh();
        }
        combo.Text = font;
        combo.SelectedItem = _allFonts.FirstOrDefault(f => f.Equals(font, StringComparison.OrdinalIgnoreCase)) ?? font;
    }

    /// <summary>A short subsequence match ("cascmo" matches "Cascadia Mono") — not a strict
    /// substring, so a few well-chosen letters narrow the list without needing to be exact.</summary>
    private static bool FuzzyMatch(string text, string query)
    {
        var ti = 0;
        foreach (var qc in query)
        {
            ti = text.IndexOf(qc.ToString(), ti, StringComparison.OrdinalIgnoreCase);
            if (ti < 0) return false;
            ti++;
        }
        return true;
    }

    /// <summary>Font pickers are searchable: empty box shows the curated shortlist, typing
    /// fuzzy-filters across every installed font. This replaces the old "All installed
    /// fonts…" sentinel entirely — search reaches the full list without a list-replacing
    /// escape hatch, and without forcing the curated list to be huge to compensate.</summary>
    private void SetupFontCombo(ComboBox combo, List<string> curated, List<string> allFonts, string savedFont, Action<string> onCommit)
    {
        combo.IsEditable = true;
        combo.IsTextSearchEnabled = false; // default type-ahead just jumps to a prefix match; we filter instead
        combo.StaysOpenOnEdit = true;

        var view = new ListCollectionView(allFonts)
        {
            Filter = o => curated.Contains((string)o, StringComparer.OrdinalIgnoreCase)
                       || string.Equals((string)o, savedFont, StringComparison.OrdinalIgnoreCase),
        };
        combo.ItemsSource = view;
        _fontCombos[combo] = (view, curated);

        combo.Text = savedFont;
        combo.SelectedItem = allFonts.FirstOrDefault(f => f.Equals(savedFont, StringComparison.OrdinalIgnoreCase)) ?? savedFont;

        // arrow-preview (below) sets combo.Text as a side effect of moving SelectedIndex —
        // suppress the filter re-running on that, or each arrow press would collapse the
        // curated list down to just whatever's currently highlighted.
        var suppressFilter = false;
        combo.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) =>
        {
            // Only react to the user actually typing. The _initializing flag alone was not
            // enough and was the bug behind "the Interface font dropdown is already open every
            // time Settings opens": an editable ComboBox does not create its PART_EditableTextBox
            // until its template is applied, which happens at first layout — *after* the
            // constructor has already set _initializing = false. WPF then syncs that fresh
            // TextBox to the Text set on line 188 and raises TextChanged, which fell straight
            // through to the force-open below. Both font combos were opened this way; only the
            // second stayed open, because opening a ComboBox popup takes mouse capture and so
            // closes the first — which is exactly why the symptom looked specific to Interface
            // font rather than to both pickers.
            // Keyboard focus is the honest discriminator: a programmatic Text sync never has it,
            // and genuine typing always does. It also stops a programmatic Text set from
            // clobbering the filter that SetFontComboSelection installs immediately before it.
            if (_initializing || suppressFilter || !combo.IsKeyboardFocusWithin) return;
            var query = combo.Text ?? "";
            view.Filter = query.Length == 0
                ? o => curated.Contains((string)o, StringComparer.OrdinalIgnoreCase)
                : o => FuzzyMatch((string)o, query);
            view.Refresh();
            if (!combo.IsDropDownOpen) combo.IsDropDownOpen = true;
        }));

        combo.SelectionChanged += (_, _) =>
        {
            if (_initializing || combo.SelectedItem is not string chosen) return;
            onCommit(chosen);
            RefreshRevertButton();
        };
        WireArrowPreview(combo, setSuppressed: v => suppressFilter = v);
    }

    /// <summary>ComboBox only fires SelectionChanged on commit; while the dropdown is open,
    /// arrow keys just move the highlight. Advance SelectedIndex ourselves so each arrow
    /// press previews live through whatever SelectionChanged handler is already wired.
    /// <paramref name="setSuppressed"/>, if given, brackets the SelectedIndex change so the
    /// caller can ignore side effects it triggers (e.g. a font combo's Text sync).</summary>
    private static void WireArrowPreview(ComboBox combo, Action<bool>? setSuppressed = null)
    {
        combo.PreviewKeyDown += (_, e) =>
        {
            if (!combo.IsDropDownOpen) return;
            var delta = e.Key switch { Key.Down => 1, Key.Up => -1, _ => 0 };
            if (delta == 0) return;
            var next = combo.SelectedIndex + delta;
            if (next < 0 || next >= combo.Items.Count) return;
            setSuppressed?.Invoke(true);
            combo.SelectedIndex = next;
            setSuppressed?.Invoke(false);
            e.Handled = true;
        };
    }

    private void SaveBehavior()
    {
        if (_initializing) return;
        AppSettings.Current.CloseToTray = ChkCloseToTray.IsChecked == true;
        AppSettings.Current.StartMinimized = ChkStartMinimized.IsChecked == true;
        AppSettings.Current.SlimMode = ChkSlimMode.IsChecked == true;
        if (CmbRetention.SelectedItem is int days)
            AppSettings.Current.HistoryRetentionDays = days;
        AppSettings.Save();
        RefreshRevertButton();
    }

    private void Autostart_Click(object sender, RoutedEventArgs e)
    {
        if (_initializing) return;
        var enabled = ChkAutostart.IsChecked == true;
        var elevated = ChkAutostartElevated.IsChecked == true && _elevated;

        if (!enabled)
        {
            StartupHelper.Disable();
            ChkAutostartElevated.IsChecked = false;
            return;
        }

        // An elevated logon task runs whatever sits at this path with admin and no UAC prompt.
        // If the path is user-writable, anything running as the user could swap the exe and
        // inherit that. Refuse, and say why rather than failing into the generic message.
        if (elevated && StartupHelper.IsExePathUserWritable())
        {
            MessageBox.Show(this,
                "Elevated autostart isn't available for this copy of PwrMon.\n\n" +
                $"It's running from:\n{StartupHelper.ExePath}\n\n" +
                "That location can be written to without administrator rights, so a scheduled " +
                "task that launches it with admin at every logon would let any program running " +
                "as you replace PwrMon and gain administrator access.\n\n" +
                "Install PwrMon (or move it under Program Files) to use elevated autostart. " +
                "Normal autostart works from anywhere.",
                "PwrMon — elevated autostart unavailable", MessageBoxButton.OK, MessageBoxImage.Warning);
            ChkAutostartElevated.IsChecked = false;
            elevated = false;
        }

        if (!StartupHelper.Enable(elevated))
        {
            MessageBox.Show(this, "Could not configure autostart — see logs.", "PwrMon");
            ChkAutostart.IsChecked = StartupHelper.IsRunKeyEnabled() || StartupHelper.IsElevatedTaskEnabled();
            ChkAutostartElevated.IsChecked = StartupHelper.IsElevatedTaskEnabled();
        }
    }

    private void OpenHistory_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(HistoryStore.HistoryDir);
        Process.Start(new ProcessStartInfo("explorer.exe", HistoryStore.HistoryDir) { UseShellExecute = true });
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
