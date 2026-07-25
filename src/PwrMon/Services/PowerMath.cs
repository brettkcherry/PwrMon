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
}
