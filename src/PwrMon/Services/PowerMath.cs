namespace PwrMon.Services;

/// <summary>
/// Pure power/energy math extracted from <see cref="Sampler"/>'s hot loop so it can be unit
/// tested without hardware. Every function mirrors an inline formula in Sampler.Tick() /
/// Sampler.SanitizeDirection() exactly (same operands, same order) — no behavior change.
/// </summary>
public static class PowerMath
{
    /// <summary>EMA smoothing factor for a step of <paramref name="dtSeconds"/> against time
    /// constant <paramref name="tauSeconds"/>.</summary>
    public static double EmaAlpha(double dtSeconds, double tauSeconds) => 1 - Math.Exp(-dtSeconds / tauSeconds);

    /// <summary>One EMA update step: prev nudged toward sample by alpha.</summary>
    public static double EmaStep(double prev, double sample, double alpha) => prev + alpha * (sample - prev);

    /// <summary>True when the wall-clock gap since the previous sample is large enough to be a
    /// sleep/hibernate gap rather than normal jitter.</summary>
    public static bool IsGap(double dtSeconds, double intervalSeconds) => dtSeconds > Math.Max(3 * intervalSeconds, 10);

    /// <summary>Smoothed time-to-full estimate, or null when not charging (or too little
    /// current/capacity to estimate meaningfully).</summary>
    public static TimeSpan? TimeToFull(double remainingWh, double fullChargeWh, double emaChargeW, bool charging) =>
        charging && emaChargeW > 0.5 && fullChargeWh > 0
            ? TimeSpan.FromHours((fullChargeWh - remainingWh) / emaChargeW)
            : null;

    /// <summary>Smoothed time-to-empty estimate, or null when not discharging (or too little
    /// current to estimate meaningfully).</summary>
    public static TimeSpan? TimeToEmpty(double remainingWh, double emaDischargeW, bool discharging) =>
        discharging && emaDischargeW > 0.5
            ? TimeSpan.FromHours(remainingWh / emaDischargeW)
            : null;

    /// <summary>Estimated wall/adapter input power: system + battery charging, over adapter
    /// efficiency.</summary>
    public static double WallInputW(double systemW, double chargeW, double adapterEfficiency) =>
        (systemW + chargeW) / adapterEfficiency;

    /// <summary>Battery capacity trend slope in watts over the tracked window, or 0 when the
    /// window is still too short to trust.</summary>
    public static double DirectionSlopeW(double whNow, double wh0, double spanSeconds, double minSpanSeconds) =>
        spanSeconds >= minSpanSeconds ? (whNow - wh0) / (spanSeconds / 3600.0) : 0;

    /// <summary>True when firmware claims charging but the measured capacity trend is actually
    /// draining beyond the contradiction threshold.</summary>
    public static bool ClaimsChargingButDraining(bool charging, double slopeW, double contradictionW) =>
        charging && slopeW < -contradictionW;

    /// <summary>True when firmware claims discharging but the measured capacity trend is
    /// actually filling beyond the contradiction threshold.</summary>
    public static bool ClaimsDischargingButFilling(bool discharging, double slopeW, double contradictionW) =>
        discharging && slopeW > contradictionW;

    /// <summary>True when a new raw battery rate reading differs enough from the previous one
    /// to count as a genuinely fresh value from the fuel gauge, rather than the same
    /// still-quantized reading repeating across polls. Below this, the gauge simply hasn't
    /// republished yet — see <see cref="UnitFormatter.IsStale"/> for what the UI does with
    /// that.</summary>
    public static bool RateChanged(double previousW, double currentW) => Math.Abs(currentW - previousW) > 0.0005;

    // ---- heavy-draw threshold ----
    //
    // "Heavy" has to mean heavy *for this machine*. See DrawProfile for why no constant works
    // and why the primary answer comes from observation.

    /// <summary>Last-resort trip point when there is neither observation nor a readable pack —
    /// the constant PwrMon shipped with, kept only so behaviour degrades to the old behaviour
    /// rather than to nothing.</summary>
    public const double FallbackHeavyDrawW = 60;

    /// <summary>Trip point before the profile is trusted, derived from the pack: the draw that
    /// would empty it in <paramref name="hoursFloor"/>. Not the same signal as the learned one
    /// — it's runtime urgency standing in for "unusual for you" until there's history — but it
    /// is at least this machine's number rather than another laptop's.</summary>
    public static double CapacityDerivedHeavyDrawW(double fullChargeWh, double hoursFloor = 1.2) =>
        fullChargeWh > 1 && hoursFloor > 0
            ? Math.Clamp(fullChargeWh / hoursFloor, 15, 150)
            : FallbackHeavyDrawW;

    /// <summary>Whether the draw counts as heavy right now, with hysteresis: once tripped it
    /// takes a fall all the way to <paramref name="releaseW"/> to clear. A single trip point
    /// would flicker the tray icon between two colours whenever the load sat on it.</summary>
    public static bool IsHeavyDraw(bool wasHeavy, double drawW, double tripW, double releaseW) =>
        wasHeavy ? drawW > releaseW : drawW > tripW;
}
