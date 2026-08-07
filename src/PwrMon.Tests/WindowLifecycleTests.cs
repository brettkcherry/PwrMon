using PwrMon.Services;

namespace PwrMon.Tests;

/// <summary>
/// Pins App.OnStartup's second-instance handling and MainWindow_Closing's tray behavior.
/// Both are exercised through WindowLifecycle rather than by launching real windows/mutexes.
/// Two real bugs shipped from this exact surface before it had any coverage:
///   - 2026-08-04: an abandoned mutex during --replace takeover crashed the elevated relaunch
///     silently, because OnDispatcherException wasn't registered yet.
///   - 2026-08-07: SlimMode gated the *first* dashboard show as well as the close behavior, so
///     anyone with slim mode on launched the exe straight into the tray with no visible window.
///   - 2026-08-07: StartMinimized was read from AppSettings.Current before AppSettings.Load()
///     ran, so the checkbox was silently ignored.
/// Every branch below exists because one of those was a truth-table cell nobody had written down.
/// </summary>
public class WindowLifecycleTests
{
    // ── second-instance handling (App.OnStartup, isFirstInstance == false) ──

    [Fact]
    public void SecondInstance_NoReplaceArg_SignalsShowAndExits()
    {
        // The common case: user double-clicked the exe again, or the Start-menu shortcut,
        // while PwrMon was already running. Must not spawn a second instance or window.
        Assert.Equal(WindowLifecycle.SecondInstanceAction.SignalShowAndExit,
            WindowLifecycle.DecideSecondInstanceAction(replaceRequested: false));
    }

    [Fact]
    public void SecondInstance_ReplaceArg_TakesOverInsteadOfExiting()
    {
        // The elevated "restart as admin" relaunch passes --replace; the new (elevated)
        // process must take the mutex over, not just poke the old one and quit.
        Assert.Equal(WindowLifecycle.SecondInstanceAction.SignalExitAndTakeOver,
            WindowLifecycle.DecideSecondInstanceAction(replaceRequested: true));
    }

    // ── first-launch show/hide (App.OnStartup, isFirstInstance == true) ──

    [Theory]
    [InlineData(false, false, true)]   // nothing asks for minimized -> show
    [InlineData(true, false, false)]   // --minimized arg (installer's autostart entry) -> hide
    [InlineData(false, true, false)]   // StartMinimized setting alone -> hide
    [InlineData(true, true, false)]    // both -> still hide
    public void ShouldShowDashboardOnStartup_MinimizedArgOrSetting(
        bool minimizedArg, bool startMinimizedSetting, bool expectedShow)
    {
        var args = minimizedArg ? new[] { "--minimized" } : Array.Empty<string>();
        Assert.Equal(expectedShow, WindowLifecycle.ShouldShowDashboardOnStartup(args, startMinimizedSetting));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ShouldShowDashboardOnStartup_IsIndependentOfSlimMode(bool slimModeIsIrrelevantHere)
    {
        // Regression pin for the 2026-08-07 bug: SlimMode has no parameter here at all.
        // Slim mode governs what happens on CLOSE (see DecideCloseAction below), not whether
        // the dashboard opens on launch. If a future change threads SlimMode into this method
        // and makes it suppress the first show again, this test's signature won't even compile
        // without acknowledging the change — but the real guard is that the method plainly
        // takes no such parameter.
        _ = slimModeIsIrrelevantHere;
        Assert.True(WindowLifecycle.ShouldShowDashboardOnStartup(Array.Empty<string>(), startMinimizedSetting: false));
    }

    [Fact]
    public void ShouldShowDashboardOnStartup_OtherArgsDoNotSuppressShow()
    {
        // --replace (elevated relaunch) and unrecognized args must not be mistaken for
        // --minimized.
        Assert.True(WindowLifecycle.ShouldShowDashboardOnStartup(
            new[] { "--replace" }, startMinimizedSetting: false));
    }

    // ── closing behavior (MainWindow_Closing) — the full 2x2 over CloseToTray x SlimMode ──

    [Fact]
    public void Close_CloseToTrayOff_AlwaysExitsRegardlessOfSlimMode()
    {
        Assert.Equal(WindowLifecycle.CloseAction.ExitApp,
            WindowLifecycle.DecideCloseAction(closeToTray: false, slimMode: false));
        Assert.Equal(WindowLifecycle.CloseAction.ExitApp,
            WindowLifecycle.DecideCloseAction(closeToTray: false, slimMode: true));
    }

    [Fact]
    public void Close_CloseToTrayOn_SlimModeOff_CancelsAndHides()
    {
        // Default behavior most users see: the X button parks the app in the tray with the
        // chart/history still warm in memory for an instant reopen.
        Assert.Equal(WindowLifecycle.CloseAction.CancelAndHide,
            WindowLifecycle.DecideCloseAction(closeToTray: true, slimMode: false));
    }

    [Fact]
    public void Close_CloseToTrayOn_SlimModeOn_AllowsCloseAndKeepsRunning()
    {
        // This is the branch the whole feature exists for: the window is actually disposed
        // (freeing chart/history buffers) while the sampler and tray keep running headless.
        Assert.Equal(WindowLifecycle.CloseAction.AllowCloseKeepRunning,
            WindowLifecycle.DecideCloseAction(closeToTray: true, slimMode: true));
    }

    // ── full startup x close settings matrix, stated explicitly ──
    //
    // CloseToTray, SlimMode and StartMinimized are three independent booleans in AppSettings
    // with no mutual-exclusion logic anywhere, so all 8 combinations are reachable from
    // Settings. This theory doesn't test new behavior beyond the cases above — it exists so
    // the full matrix is visible in one place and any future coupling between the three has to
    // be deliberately introduced, not accidentally inherited from evaluation order.
    [Theory]
    [InlineData(false, false, false, true, WindowLifecycle.CloseAction.ExitApp)]
    [InlineData(false, false, true, false, WindowLifecycle.CloseAction.ExitApp)]
    [InlineData(false, true, false, true, WindowLifecycle.CloseAction.ExitApp)]
    [InlineData(false, true, true, false, WindowLifecycle.CloseAction.ExitApp)]
    [InlineData(true, false, false, true, WindowLifecycle.CloseAction.CancelAndHide)]
    [InlineData(true, false, true, false, WindowLifecycle.CloseAction.CancelAndHide)]
    [InlineData(true, true, false, true, WindowLifecycle.CloseAction.AllowCloseKeepRunning)]
    [InlineData(true, true, true, false, WindowLifecycle.CloseAction.AllowCloseKeepRunning)]
    public void FullMatrix_CloseToTray_SlimMode_StartMinimized(
        bool closeToTray, bool slimMode, bool startMinimized, bool expectedShowOnStartup,
        WindowLifecycle.CloseAction expectedCloseAction)
    {
        Assert.Equal(expectedShowOnStartup,
            WindowLifecycle.ShouldShowDashboardOnStartup(Array.Empty<string>(), startMinimized));
        Assert.Equal(expectedCloseAction, WindowLifecycle.DecideCloseAction(closeToTray, slimMode));
    }
}
