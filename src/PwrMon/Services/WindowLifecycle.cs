namespace PwrMon.Services;

/// <summary>
/// Pure decision logic extracted from App.OnStartup and MainWindow_Closing so the window/tray
/// state machine can be unit tested without spinning up WPF windows, a mutex, or the registry.
/// Two real bugs have already come out of this exact surface (2026-08-07: slim mode silently
/// suppressed the first-launch window; StartMinimized was read before AppSettings.Load()), so
/// every branch here is pinned by a test rather than trusted by inspection.
/// </summary>
public static class WindowLifecycle
{
    /// <summary>What OnStartup should do when a second copy of PwrMon launches while one is
    /// already running (the named mutex was not acquired).</summary>
    public enum SecondInstanceAction
    {
        /// <summary>Ask the running instance to show its window, then exit quietly. The normal
        /// case: someone double-clicked the exe or the Start-menu shortcut again.</summary>
        SignalShowAndExit,

        /// <summary>An elevated relaunch is taking over. Signal the old instance to exit and
        /// wait for the mutex to free up before continuing OnStartup in this process.</summary>
        SignalExitAndTakeOver,
    }

    /// <summary>Mirrors the branch in App.OnStartup that runs when isFirstInstance is false.
    /// isFirstInstance itself isn't a parameter — when it's true, OnStartup falls straight
    /// through instead of consulting this decision at all.</summary>
    public static SecondInstanceAction DecideSecondInstanceAction(bool replaceRequested) =>
        replaceRequested ? SecondInstanceAction.SignalExitAndTakeOver : SecondInstanceAction.SignalShowAndExit;

    /// <summary>Whether OnStartup should call ShowDashboard() for the very first window of this
    /// run. Mirrors App.OnStartup exactly: the dashboard starts hidden only when asked to,
    /// either via the --minimized launch argument (used by the installer's autostart entry) or
    /// the persisted "start minimized to tray" setting. Slim mode does not participate — it
    /// governs what happens when the window is later closed, not whether it opens at launch.</summary>
    public static bool ShouldShowDashboardOnStartup(IReadOnlyCollection<string> args, bool startMinimizedSetting)
    {
        var minimized = args.Contains("--minimized") || startMinimizedSetting;
        return !minimized;
    }

    /// <summary>What MainWindow_Closing should do when the user closes the dashboard window,
    /// as a function of the two independent settings that govern it.</summary>
    public enum CloseAction
    {
        /// <summary>CloseToTray is off: closing the window quits the whole app.</summary>
        ExitApp,

        /// <summary>CloseToTray is on, SlimMode is off: cancel the close and hide instead, so
        /// the chart/history buffers stay warm for an instant reopen.</summary>
        CancelAndHide,

        /// <summary>CloseToTray is on, SlimMode is on: let the close proceed. The window and
        /// its buffers are actually disposed; the app lives on via tray + sampler only, and
        /// reopening rebuilds the dashboard from CSV backfill.</summary>
        AllowCloseKeepRunning,
    }

    public static CloseAction DecideCloseAction(bool closeToTray, bool slimMode)
    {
        if (!closeToTray) return CloseAction.ExitApp;
        return slimMode ? CloseAction.AllowCloseKeepRunning : CloseAction.CancelAndHide;
    }
}
