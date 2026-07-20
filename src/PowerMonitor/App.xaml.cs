using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using PowerMonitor.Models;
using PowerMonitor.Services;
using PowerMonitor.Views;

namespace PowerMonitor;

public partial class App : Application
{
    private const string MutexName = "PowerMonitor_SingleInstance";
    private const string ShowSignalName = "PowerMonitor_ShowSignal";
    private const string ExitSignalName = "PowerMonitor_ExitSignal";

    private Mutex? _mutex;
    private EventWaitHandle? _showSignal, _exitSignal;
    private RegisteredWaitHandle? _showWait, _exitWait;

    public BatteryReader Battery { get; private set; } = null!;
    public HardwareReader Hardware { get; private set; } = null!;
    public Sampler Sampler { get; private set; } = null!;
    public HistoryStore History { get; private set; } = null!;
    public TrayService Tray { get; private set; } = null!;

    private MainWindow? _mainWindow;
    private MiniGraphWindow? _miniGraph;
    private bool _exiting;

    public static new App Current => (App)Application.Current;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var replace = e.Args.Contains("--replace");
        var minimized = e.Args.Contains("--minimized") || AppSettings.Current.StartMinimized;

        _mutex = new Mutex(true, MutexName, out var isFirst);
        if (!isFirst)
        {
            if (replace)
            {
                // an elevated restart is taking over: ask the old instance to die, wait for the mutex
                using var exit = new EventWaitHandle(false, EventResetMode.AutoReset, ExitSignalName);
                exit.Set();
                if (!_mutex.WaitOne(TimeSpan.FromSeconds(8)))
                {
                    MessageBox.Show("PowerMonitor is already running and did not exit.", "PowerMonitor");
                    Shutdown();
                    return;
                }
            }
            else
            {
                // just poke the running instance to show itself
                using var show = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSignalName);
                show.Set();
                Shutdown();
                return;
            }
        }

        AppSettings.Load();
        ThemeService.Apply(AppSettings.Current.Theme);
        ThemeService.ApplyNumeralFont(AppSettings.Current.NumeralFont);
        ThemeService.ApplyTextFont(AppSettings.Current.TextFont);
        Log.Info($"=== PowerMonitor starting (args: {string.Join(' ', e.Args)}) ===");

        DispatcherUnhandledException += OnDispatcherException;
        AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
            Log.Error("fatal", (ex.ExceptionObject as Exception) ?? new Exception("unknown"));

        // the WinForms tray icon pumps its own handlers; exceptions there bypass
        // DispatcherUnhandledException and pop the ancient modal ThreadExceptionDialog —
        // route them to the log and keep running, same policy as the WPF handler.
        // (must be set before the first WinForms handle exists, i.e. before TrayService)
        System.Windows.Forms.Application.SetUnhandledExceptionMode(
            System.Windows.Forms.UnhandledExceptionMode.CatchException);
        System.Windows.Forms.Application.ThreadException += (_, ex) =>
            Log.Error("winforms ui exception", ex.Exception);

        // cross-instance signals
        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSignalName);
        _exitSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ExitSignalName);
        _showWait = ThreadPool.RegisterWaitForSingleObject(_showSignal,
            (_, _) => Dispatcher.BeginInvoke(ShowDashboard), null, -1, false);
        _exitWait = ThreadPool.RegisterWaitForSingleObject(_exitSignal,
            (_, _) => Dispatcher.BeginInvoke(() => ExitApp()), null, -1, false);

        Battery = new BatteryReader();
        Hardware = new HardwareReader();
        History = new HistoryStore();
        Sampler = new Sampler(Battery, Hardware);
        Tray = new TrayService();

        Tray.OpenRequested += ShowDashboard;
        Tray.SettingsRequested += () => { ShowDashboard(); _mainWindow?.OpenSettings(); };
        Tray.ExitRequested += () => ExitApp();
        Tray.MiniGraphToggleRequested += ToggleMiniGraph;

        Sampler.SampleReady += (s, stats, est) =>
        {
            History.Append(s);
            Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
            {
                if (_exiting) return;
                Tray.Update(s, est);
                _mainWindow?.OnSample(s, stats, est);
                _miniGraph?.OnSample(s, est);
            });
        };
        Sampler.PowerEventRaised += ev =>
        {
            History.AppendEvent(ev);
            Dispatcher.BeginInvoke(() => _mainWindow?.OnPowerEvent(ev));
        };

        SystemEvents.PowerModeChanged += OnPowerModeChanged;

        History.CleanupOldFiles();

        // dashboard is created lazily (ShowDashboard) so slim/minimized starts stay light
        if (!minimized && !AppSettings.Current.SlimMode)
            ShowDashboard();

        if (AppSettings.Current.MiniGraphEnabled)
            ToggleMiniGraph(forceOn: true);

        Sampler.Start();
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
            Sampler.InjectEvent(PowerEventKind.Resumed);
    }

    public void ShowDashboard()
    {
        if (_exiting) return;
        if (_mainWindow is null)
        {
            History.Flush(); // so the chart backfill includes samples still in the write buffer
            _mainWindow = new MainWindow();
            _mainWindow.Closed += (_, _) => _mainWindow = null;
        }
        _mainWindow.Show();
        if (_mainWindow.WindowState == WindowState.Minimized)
            _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
    }

    public void ToggleMiniGraph() => ToggleMiniGraph(null);

    private void ToggleMiniGraph(bool? forceOn)
    {
        var turnOn = forceOn ?? _miniGraph is null;
        if (turnOn && _miniGraph is null)
        {
            _miniGraph = new MiniGraphWindow();
            _miniGraph.Closed += (_, _) => _miniGraph = null;
            _miniGraph.Show();
        }
        else if (!turnOn && _miniGraph is not null)
        {
            _miniGraph.Close();
            _miniGraph = null;
        }
        AppSettings.Current.MiniGraphEnabled = turnOn;
        AppSettings.Save();
    }

    public bool IsMiniGraphOpen => _miniGraph is not null;

    public void ExitApp()
    {
        if (_exiting) return;
        _exiting = true;
        Log.Info("shutting down");
        try
        {
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            _showWait?.Unregister(null);
            _exitWait?.Unregister(null);
            Sampler.Dispose();
            History.Dispose();   // flushes
            Hardware.Dispose();
            Battery.Dispose();
            Tray.Dispose();
        }
        catch (Exception ex) { Log.Error("shutdown", ex); }
        Shutdown();
    }

    private void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("ui exception", e.Exception);
        e.Handled = true; // keep the monitor alive; errors are in the log
    }
}
