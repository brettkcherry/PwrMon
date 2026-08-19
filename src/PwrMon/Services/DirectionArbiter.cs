namespace PwrMon.Services;

/// <summary>
/// Decides the battery's true charge/discharge direction when the firmware's flags and the
/// measured capacity trend disagree.
///
/// <para>Some firmware — ASUS adapter-assist, where the battery covers what the AC source
/// can't — reports the drain in ChargeRate with Charging=true. RemainingCapacity never lies,
/// so its trend arbitrates.</para>
///
/// <para>Extracted from <see cref="Sampler"/> so it can be replayed against recorded incident
/// data without WMI or a live battery. This surface has produced two real failures and both
/// are pinned by tests: 2026-08-13, a genuine 70-minute drain-on-AC that nothing warned about;
/// 2026-08-19, a false alert 60 s after an ordinary plug-in. Holding the two in tension is the
/// whole job — it has to convict a sustained real drain and acquit a transient artifact.</para>
///
/// <para>Deliberately does no logging: it is called from the sampler's hot loop and from
/// tests, and a static file logger reached from a test run writes into the user's real log
/// directory. <see cref="IsOverriding"/> is exposed so the caller can log transitions.</para>
/// </summary>
public sealed class DirectionArbiter
{
    private const double TrendWindowSeconds = 90;
    private const double TrendMinSpanSeconds = 60;
    private const double TrendContradictionW = 3;

    /// <summary>
    /// How long to ignore the gauge after any flag change before anchoring a new trend window.
    ///
    /// Clearing the window on a flag change is necessary but not sufficient: it re-anchors the
    /// trend at the instant of the transition, which is exactly when RemainingWh is least
    /// trustworthy. Observed 2026-08-19: AC connected at 09:59:19 with the gauge still
    /// reporting capacity *falling* for ~35 s afterwards (35.830 → 35.509 Wh). At T+60 s the
    /// window first became eligible, measured endpoint-to-endpoint across that dip at -6.4 W,
    /// and convicted "charging but draining" while the battery was in fact filling.
    /// </summary>
    private const double TrendSettleSeconds = 45;

    /// <summary>
    /// How long a contradiction must persist before it is acted on. A genuine adapter-assist
    /// drain is sustained — the 2026-08-13 incident ran 70 minutes — so anything disagreeing
    /// for only seconds is an artifact. Independent of the settle delay: that keeps known-bad
    /// data out of the window, this refuses to act on a transient from any other cause.
    /// </summary>
    private const double ContradictionHoldSeconds = 30;

    private readonly Queue<(DateTimeOffset Time, double Wh)> _capTrend = new();
    private (bool Charging, bool Discharging, bool AcOnline) _trendFlags;
    private DateTimeOffset? _settleUntil;
    private DateTimeOffset? _contradictionSince;

    /// <summary>True while the reported direction is being overridden. Caller logs transitions.</summary>
    public bool IsOverriding { get; private set; }

    /// <summary>The arbiter's ruling for one sample. <see cref="SlopeW"/> is the measured
    /// capacity trend, negative when draining; 0 while the window is too short to trust.</summary>
    public readonly record struct Verdict(
        bool ClaimsChargingButDraining,
        bool ClaimsDischargingButFilling,
        double SlopeW);

    public void Reset()
    {
        _capTrend.Clear();
        _settleUntil = null;
        _contradictionSince = null;
        IsOverriding = false;
    }

    public Verdict Evaluate(
        bool hasBattery, bool charging, bool discharging, bool acOnline,
        double remainingWh, DateTimeOffset now, bool gap)
    {
        if (!hasBattery) { Reset(); return default; }

        // The trend is only meaningful while the reported state is stable, so reset on any
        // flag change or sleep gap. Any conviction is dropped along with the evidence it
        // rested on, rather than carried across a transition this is about to be blind through.
        var flags = (charging, discharging, acOnline);
        if (gap || flags != _trendFlags)
        {
            _capTrend.Clear();
            _contradictionSince = null;
            IsOverriding = false;
            _settleUntil = now.AddSeconds(TrendSettleSeconds);
        }
        _trendFlags = flags;

        // Hold the window empty through settling so it anchors on a reading the gauge has
        // actually converged on, rather than on the transition itself.
        if (_settleUntil is { } until && now < until) return default;
        _settleUntil = null;

        _capTrend.Enqueue((now, remainingWh));
        while ((now - _capTrend.Peek().Time).TotalSeconds > TrendWindowSeconds)
            _capTrend.Dequeue();

        var (t0, wh0) = _capTrend.Peek();
        var spanSec = (now - t0).TotalSeconds;
        var slopeW = PowerMath.DirectionSlopeW(remainingWh, wh0, spanSec, TrendMinSpanSeconds);

        var chargingButDraining = PowerMath.ClaimsChargingButDraining(charging, slopeW, TrendContradictionW);
        var dischargingButFilling = PowerMath.ClaimsDischargingButFilling(discharging, slopeW, TrendContradictionW);
        var contradicts = chargingButDraining || dischargingButFilling;

        if (!contradicts) _contradictionSince = null;
        else _contradictionSince ??= now;

        // Hysteresis on the way in only. Releasing immediately is the safe direction — it fails
        // toward "no alert" — and the slope is measured across a 90 s window, so a momentarily
        // flat gauge doesn't move it much. DrainAlertService serves its own 20 s confirm before
        // re-announcing, which dampens any flapping this could otherwise cause.
        var flip = _contradictionSince is { } since
                   && (now - since).TotalSeconds >= ContradictionHoldSeconds;
        IsOverriding = flip;

        return flip
            ? new Verdict(chargingButDraining, dischargingButFilling, slopeW)
            : new Verdict(false, false, slopeW);
    }
}
