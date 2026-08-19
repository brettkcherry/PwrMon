using PwrMon.Services;

namespace PwrMon.Tests;

/// <summary>
/// The fuel-gauge direction arbiter, pinned against both real incidents it has to tell apart:
/// a sustained adapter-assist drain that must be convicted, and a post-plug-in gauge artifact
/// that must be acquitted. Capacity figures in the replay tests are the recorded ones.
/// </summary>
public class DirectionArbiterTests
{
    private static readonly DateTimeOffset T0 = new(2026, 8, 19, 9, 0, 0, TimeSpan.Zero);

    private sealed class Harness
    {
        public readonly DirectionArbiter Arbiter = new();
        private DateTimeOffset _now = T0;

        public DirectionArbiter.Verdict Feed(
            double remainingWh, bool charging, bool discharging, bool ac,
            double afterSeconds = 1, bool gap = false)
        {
            _now = _now.AddSeconds(afterSeconds);
            return Arbiter.Evaluate(
                hasBattery: true, charging, discharging, acOnline: ac,
                remainingWh, _now, gap);
        }

        /// <summary>Feeds a steady capacity ramp, one sample a second.</summary>
        public DirectionArbiter.Verdict Ramp(double startWh, double wattsPerHour, int seconds,
                                             bool charging, bool discharging, bool ac)
        {
            var v = default(DirectionArbiter.Verdict);
            for (var i = 1; i <= seconds; i++)
                v = Feed(startWh + wattsPerHour * (i / 3600.0), charging, discharging, ac);
            return v;
        }
    }

    // ── the 2026-08-19 false positive ───────────────────────────────────────────────

    /// <summary>
    /// The recorded incident: AC connects at 09:59:19 while the gauge keeps reporting capacity
    /// falling for ~35 s (35.830 → 35.509 Wh) before recovering. The old code cleared the trend
    /// window on the flag change but re-anchored it at the transition, so at T+60 s it measured
    /// -6.4 W across the settling dip and convicted a drain while the battery was filling.
    /// </summary>
    [Fact]
    public void Does_not_convict_on_the_post_plug_in_settling_dip()
    {
        var h = new Harness();

        // on battery, capacity genuinely falling
        h.Ramp(36.222, wattsPerHour: -33, seconds: 30, charging: false, discharging: true, ac: false);

        // AC connects: flags flip to charging, but the gauge still reports a falling capacity
        // for ~35 s before it turns around.
        h.Feed(35.830, charging: true, discharging: false, ac: true);
        for (var i = 0; i < 34; i++)
            h.Feed(35.509, charging: true, discharging: false, ac: true);

        // then it recovers and climbs, as it did on the day
        var verdict = h.Ramp(35.509, wattsPerHour: 14, seconds: 120,
                             charging: true, discharging: false, ac: true);

        Assert.False(verdict.ClaimsChargingButDraining);
        Assert.False(h.Arbiter.IsOverriding);
    }

    [Fact]
    public void Does_not_convict_on_a_contradiction_shorter_than_the_hold()
    {
        var h = new Harness();
        // settle out first so the window is anchored on converged data
        h.Ramp(40.0, wattsPerHour: 10, seconds: 60, charging: true, discharging: false, ac: true);
        h.Ramp(40.0, wattsPerHour: 10, seconds: 90, charging: true, discharging: false, ac: true);

        // a brief dip that contradicts, but for well under ContradictionHoldSeconds
        for (var i = 0; i < 10; i++)
            h.Feed(39.0, charging: true, discharging: false, ac: true);

        Assert.False(h.Arbiter.IsOverriding);
    }

    // ── the 2026-08-13 real drain ───────────────────────────────────────────────────

    /// <summary>The failure this whole mechanism exists for: firmware insists it is charging
    /// while the pack steadily empties. Must still be convicted, settle delay and hold and all.</summary>
    [Fact]
    public void Convicts_a_sustained_drain_while_firmware_claims_charging()
    {
        var h = new Harness();
        // plug in, then drain steadily at ~30 W with Charging=true throughout
        var verdict = h.Ramp(50.0, wattsPerHour: -30, seconds: 300,
                             charging: true, discharging: false, ac: true);

        Assert.True(verdict.ClaimsChargingButDraining);
        Assert.True(h.Arbiter.IsOverriding);
        Assert.True(verdict.SlopeW < -3);
    }

    [Fact]
    public void Convicts_the_mirror_case_filling_while_firmware_claims_discharging()
    {
        var h = new Harness();
        var verdict = h.Ramp(20.0, wattsPerHour: 25, seconds: 300,
                             charging: false, discharging: true, ac: true);

        Assert.True(verdict.ClaimsDischargingButFilling);
        Assert.True(h.Arbiter.IsOverriding);
    }

    /// <summary>A conviction holds for as long as the drain does — the hold is served once on
    /// the way in, not re-served on every tick.</summary>
    [Fact]
    public void Keeps_a_conviction_while_the_drain_continues()
    {
        var h = new Harness();
        h.Ramp(50.0, wattsPerHour: -30, seconds: 300, charging: true, discharging: false, ac: true);
        Assert.True(h.Arbiter.IsOverriding);

        for (var i = 0; i < 5; i++)
        {
            h.Ramp(47.5 - i, wattsPerHour: -30, seconds: 60, charging: true, discharging: false, ac: true);
            Assert.True(h.Arbiter.IsOverriding);
        }
    }

    /// <summary>Release is immediate and deliberate: it fails toward "no alert". Pinned so the
    /// asymmetry with the entry hold is a decision on record rather than an accident.</summary>
    [Fact]
    public void Releases_a_conviction_as_soon_as_the_trend_reverses()
    {
        var h = new Harness();
        h.Ramp(50.0, wattsPerHour: -30, seconds: 300, charging: true, discharging: false, ac: true);
        Assert.True(h.Arbiter.IsOverriding);

        // capacity climbing again, with the flags unchanged so nothing else resets the window
        h.Ramp(47.5, wattsPerHour: 40, seconds: 95, charging: true, discharging: false, ac: true);
        Assert.False(h.Arbiter.IsOverriding);
    }

    // ── resets ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void Drops_a_conviction_when_the_flags_change()
    {
        var h = new Harness();
        h.Ramp(50.0, wattsPerHour: -30, seconds: 300, charging: true, discharging: false, ac: true);
        Assert.True(h.Arbiter.IsOverriding);

        // unplugging is a new situation; the old verdict's evidence no longer applies
        h.Feed(45.0, charging: false, discharging: true, ac: false);
        Assert.False(h.Arbiter.IsOverriding);
    }

    [Fact]
    public void Drops_a_conviction_across_a_sleep_gap()
    {
        var h = new Harness();
        h.Ramp(50.0, wattsPerHour: -30, seconds: 300, charging: true, discharging: false, ac: true);
        Assert.True(h.Arbiter.IsOverriding);

        h.Feed(30.0, charging: true, discharging: false, ac: true, afterSeconds: 3600, gap: true);
        Assert.False(h.Arbiter.IsOverriding);
    }

    [Fact]
    public void Reports_nothing_without_a_battery()
    {
        var h = new Harness();
        h.Ramp(50.0, wattsPerHour: -30, seconds: 300, charging: true, discharging: false, ac: true);

        var verdict = h.Arbiter.Evaluate(
            hasBattery: false, charging: false, discharging: false, acOnline: true,
            remainingWh: 0, T0.AddSeconds(400), gap: false);

        Assert.False(verdict.ClaimsChargingButDraining);
        Assert.False(verdict.ClaimsDischargingButFilling);
        Assert.False(h.Arbiter.IsOverriding);
    }
}
