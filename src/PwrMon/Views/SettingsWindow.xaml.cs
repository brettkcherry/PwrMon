using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using PwrMon.Models;
using PwrMon.Services;

namespace PwrMon.Views;

public partial class SettingsWindow : Window
{
    private readonly bool _elevated = App.Current.Hardware.IsElevated;
    private bool _initializing = true;

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
        };
        // ComboBox only fires SelectionChanged on commit (Enter/click), not while arrowing
        // through an open dropdown — advance the selection ourselves so each arrow press
        // previews live through the handler above, same as a click would.
        WireArrowPreview(CmbTheme);

        var allFonts = ThemeService.InstalledFonts().ToList();

        SetupFontCombo(CmbFont, ThemeService.CuratedNumeralFonts().ToList(), allFonts, AppSettings.Current.NumeralFont, fontName =>
        {
            AppSettings.Current.NumeralFont = fontName;
            AppSettings.Save();
            ThemeService.ApplyNumeralFont(fontName);
        });

        SetupFontCombo(CmbTextFont, ThemeService.CuratedTextFonts().ToList(), allFonts, AppSettings.Current.TextFont, fontName =>
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

        _initializing = false;
    }

    private const string ShowAllFontsSentinel = "All installed fonts…";

    /// <summary>Populates a font ComboBox with the curated list (plus the saved choice, if it
    /// happens to be outside the curated set) and an escape hatch to every installed font.</summary>
    private void SetupFontCombo(System.Windows.Controls.ComboBox combo, List<string> curated, List<string> allFonts, string savedFont, Action<string> onCommit)
    {
        var items = new List<string>(curated);
        if (!items.Any(f => f.Equals(savedFont, StringComparison.OrdinalIgnoreCase)) &&
            allFonts.Any(f => f.Equals(savedFont, StringComparison.OrdinalIgnoreCase)))
            items.Insert(0, savedFont);
        items.Add(ShowAllFontsSentinel);

        foreach (var f in items) combo.Items.Add(f);
        combo.SelectedItem = items.FirstOrDefault(f => f.Equals(savedFont, StringComparison.OrdinalIgnoreCase))
                            ?? items.FirstOrDefault(f => f == "Segoe UI") ?? items[0];

        combo.SelectionChanged += (_, _) =>
        {
            if (_initializing || combo.SelectedItem is not string chosen) return;
            if (chosen == ShowAllFontsSentinel)
            {
                combo.Items.Clear();
                foreach (var f in allFonts) combo.Items.Add(f);
                combo.SelectedItem = allFonts.FirstOrDefault(f => f.Equals(savedFont, StringComparison.OrdinalIgnoreCase));
                combo.IsDropDownOpen = true;
                return;
            }
            onCommit(chosen);
        };
        WireArrowPreview(combo);
    }

    /// <summary>ComboBox only fires SelectionChanged on commit; while the dropdown is open,
    /// arrow keys just move the highlight. Advance SelectedIndex ourselves so each arrow
    /// press previews live through whatever SelectionChanged handler is already wired.</summary>
    private static void WireArrowPreview(System.Windows.Controls.ComboBox combo)
    {
        combo.PreviewKeyDown += (_, e) =>
        {
            if (!combo.IsDropDownOpen) return;
            var delta = e.Key switch { Key.Down => 1, Key.Up => -1, _ => 0 };
            if (delta == 0) return;
            var next = combo.SelectedIndex + delta;
            if (next < 0 || next >= combo.Items.Count) return;
            combo.SelectedIndex = next;
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
