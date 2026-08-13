using System.Security.AccessControl;
using System.Security.Principal;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using PwrMon.Models;
using PwrMon.Services;
using PwrMon.Views;

namespace PwrMon;

public partial class App : Application
{
    private const string MutexName = "PwrMon_SingleInstance";
    private const string ShowSignalName = "PwrMon_ShowSignal";
    private const string ExitSignalName = "PwrMon_ExitSignal";

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
    private DrainAlertService _drainAlerts = null!;
    private bool _exiting;

    public static new App Current => (App)Application.Current;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var replace = e.Args.Contains("--replace");

        _mutex = new Mutex(true, MutexName, out var isFirst);
        if (!isFirst)
        {
            switch (WindowLifecycle.DecideSecondInstanceAction(replace))
            {
                case WindowLifecycle.SecondInstanceAction.SignalExitAndTakeOver:
                    // an elevated restart is taking over: ask the old instance to die, wait for the mutex.
                    // ExitApp releases the mutex before Shutdown, but if the old instance died some
                    // other way (crash, killed) the wait completes as "abandoned" instead of
                    // returning true — that's not a failure here, just an unclean prior exit.
                    TrySignal(ExitSignalName);
                    bool acquired;
                    try { acquired = _mutex.WaitOne(TimeSpan.FromSeconds(8)); }
                    catch (AbandonedMutexException) { acquired = true; }
                    if (!acquired)
                    {
                        MessageBox.Show("PwrMon is already running and did not exit.", "PwrMon");
                        Shutdown();
                        return;
                    }
                    break;

                case WindowLifecycle.SecondInstanceAction.SignalShowAndExit:
                    // just poke the running instance to show itself
                    TrySignal(ShowSignalName);
                    Shutdown();
                    return;
            }
        }

        AppSettings.Load();

        // must be read after Load: AppSettings.Current is a defaults instance until then, so
        // computing this earlier silently ignored the user's saved StartMinimized preference.
        var showOnStartup = WindowLifecycle.ShouldShowDashboardOnStartup(e.Args, AppSettings.Current.StartMinimized);

        ThemeService.Apply(AppSettings.Current.Theme);
        ThemeService.ApplyNumeralFont(AppSettings.Current.NumeralFont);
        ThemeService.ApplyTextFont(AppSettings.Current.TextFont);
        Log.Info($"=== PwrMon starting (args: {string.Join(' ', e.Args)}) ===");

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
        _showSignal = CreateUserScopedEvent(ShowSignalName);
        _exitSignal = CreateUserScopedEvent(ExitSignalName);
        _showWait = ThreadPool.RegisterWaitForSingleObject(_showSignal,
            (_, _) => Dispatcher.BeginInvoke(ShowDashboard), null, -1, false);
        _exitWait = ThreadPool.RegisterWaitForSingleObject(_exitSignal, (_, _) =>
            Dispatcher.BeginInvoke(() =>
            {
                // worth a log line: otherwise a takeover looks like an unexplained shutdown
                Log.Info("exit requested over the cross-instance signal (--replace takeover)");
                ExitApp();
            }), null, -1, false);

        Battery = new BatteryReader();
        Hardware = new HardwareReader();
        History = new HistoryStore();
        Sampler = new Sampler(Battery, Hardware);
        Tray = new TrayService();

        Tray.OpenRequested += ShowDashboard;
        Tray.SettingsRequested += () => { ShowDashboard(); _mainWindow?.OpenSettings(); };
        Tray.ExitRequested += () => ExitApp();
        Tray.MiniGraphToggleRequested += ToggleMiniGraph;
        Tray.MiniGraphClickThroughToggleRequested += ToggleMiniGraphClickThrough;

        _drainAlerts = new DrainAlertService((title, body) => Tray.ShowAlert(title, body));

        Sampler.SampleReady += (s, stats, est) =>
        {
            History.Append(s);
            Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
            {
                if (_exiting) return;
                Tray.Update(s, est);
                _drainAlerts.OnSample(s, est);
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
        Log.CleanupOldFiles(AppSettings.Current.HistoryRetentionDays);

        // The dashboard is created lazily (ShowDashboard) so a minimized start stays light.
        // Slim mode deliberately does NOT suppress this: it means "free the dashboard when you
        // close it" — which is what its Settings checkbox says — not "launch into the tray".
        // Gating the initial show on it too made launching the exe look like nothing happened.
        // See WindowLifecycle.ShouldShowDashboardOnStartup for the pinned decision table.
        if (showOnStartup)
            ShowDashboard();

        if (AppSettings.Current.MiniGraphEnabled)
            ToggleMiniGraph(forceOn: true);

        Sampler.Start();
    }

    /// <summary>
    /// Creates a cross-instance signal with an explicit DACL granting only the current user,
    /// instead of inheriting whatever default DACL the process token happens to carry.
    /// A same-user process can still open these — but it could already call TerminateProcess
    /// on us, so that isn't a boundary this can enforce. What it does buy: no access from
    /// other accounts on a shared machine, and a stated intent rather than an inherited default.
    /// </summary>
    private static EventWaitHandle CreateUserScopedEvent(string name)
    {
        var security = new EventWaitHandleSecurity();
        security.AddAccessRule(new EventWaitHandleAccessRule(
            WindowsIdentity.GetCurrent().User!,
            EventWaitHandleRights.FullControl,
            AccessControlType.Allow));
        return EventWaitHandleAcl.Create(false, EventResetMode.AutoReset, name, out _, security);
    }

    /// <summary>Pokes a running instance. Uses OpenExisting so a stray call can never create
    /// the event itself — creating it here would leave an unsecured handle behind.</summary>
    private static bool TrySignal(string name)
    {
        try
        {
            using var h = EventWaitHandle.OpenExisting(name); // asks for Modify | Synchronize
            h.Set();
            return true;
        }
        catch (WaitHandleCannotBeOpenedException) { return false; } // nobody listening
        catch (UnauthorizedAccessException) { return false; }       // not ours to signal
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

    /// <summary>The only way to turn click-through off once it's on: the window itself can no
    /// longer be right-clicked to reach its own menu at that point.</summary>
    private void ToggleMiniGraphClickThrough()
    {
        var enabled = !AppSettings.Current.MiniGraphClickThrough;
        AppSettings.Current.MiniGraphClickThrough = enabled;
        AppSettings.Save();
        _miniGraph?.SetClickThrough(enabled);
    }

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
        // Release rather than abandon: a --replace takeover is waiting on this mutex, and an
        // abandoned wait throws AbandonedMutexException before any handler is registered in
        // the new process's OnStartup, crashing it silently (see 2026-08-04 incident).
        try { _mutex?.ReleaseMutex(); } catch (Exception ex) { Log.Error("release mutex", ex); }
        Shutdown();
    }

    private void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("ui exception", e.Exception);
        e.Handled = true; // keep the monitor alive; errors are in the log
    }
}
