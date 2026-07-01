using System.Diagnostics;
using System.IO;
using System.Windows;
using PowerMonitor.Models;
using PowerMonitor.Services;

namespace PowerMonitor.Views;

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

        foreach (var f in new[] { "Bahnschrift", "Cascadia Mono", "Segoe UI" })
            CmbFont.Items.Add(f);
        CmbFont.SelectedItem = AppSettings.Current.NumeralFont;
        if (CmbFont.SelectedItem is null) CmbFont.SelectedIndex = 0;
        CmbFont.SelectionChanged += (_, _) =>
        {
            if (_initializing || CmbFont.SelectedItem is not string fontName) return;
            AppSettings.Current.NumeralFont = fontName;
            AppSettings.Save();
            ThemeService.ApplyNumeralFont(fontName);
        };

        ChkCloseToTray.IsChecked = AppSettings.Current.CloseToTray;
        ChkStartMinimized.IsChecked = AppSettings.Current.StartMinimized;
        ChkSlimMode.IsChecked = AppSettings.Current.SlimMode;

        ChkAutostart.IsChecked = StartupHelper.IsRunKeyEnabled() || StartupHelper.IsElevatedTaskEnabled();
        ChkAutostartElevated.IsChecked = StartupHelper.IsElevatedTaskEnabled();
        ChkAutostartElevated.IsEnabled = _elevated;
        AutostartNote.Text = _elevated
            ? "Creates a Task Scheduler entry with highest privileges."
            : "Run PowerMonitor as administrator to enable the elevated option.";

        foreach (var d in new[] { 1, 3, 7, 14, 30, 60 })
            CmbRetention.Items.Add(d);
        CmbRetention.SelectedItem = new[] { 1, 3, 7, 14, 30, 60 }
            .OrderBy(d => Math.Abs(d - AppSettings.Current.HistoryRetentionDays)).First();

        HistoryPathText.Text = $"CSV files: {HistoryStore.HistoryDir}";

        ChkCloseToTray.Click += (_, _) => SaveBehavior();
        ChkStartMinimized.Click += (_, _) => SaveBehavior();
        ChkSlimMode.Click += (_, _) => SaveBehavior();
        CmbRetention.SelectionChanged += (_, _) => SaveBehavior();

        _initializing = false;
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
            MessageBox.Show(this, "Could not configure autostart — see logs.", "PowerMonitor");
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
