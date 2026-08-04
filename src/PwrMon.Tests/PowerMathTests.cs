using PwrMon.Services;

namespace PwrMon.Tests;

/// <summary>Direct tests of the pure formulas extracted from Sampler's hot loop.</summary>
public class PowerMathTests
{
    [Fact]
    public void EmaAlpha_at_dt_equal_tau_is_about_0_632()
    {
        // 1 - e^-1
        var alpha = PowerMath.EmaAlpha(30, 30);
        Assert.Equal(0.6321, alpha, 4);
    }

    [Fact]
    public void EmaAlpha_at_zero_dt_is_zero()
    {
        Assert.Equal(0, PowerMath.EmaAlpha(0, 30), 10);
    }

    [Fact]
    public void EmaAlpha_grows_toward_one_as_dt_grows()
    {
        var small = PowerMath.EmaAlpha(1, 30);
        var large = PowerMath.EmaAlpha(300, 30);
        Assert.True(small < large);
        Assert.True(large > 0.9999);
    }

    [Fact]
    public void EmaStep_nudges_prev_toward_sample_by_alpha()
    {
        // prev + alpha*(sample-prev): 10 + 0.5*(20-10) = 15
        Assert.Equal(15, PowerMath.EmaStep(10, 20, 0.5), 10);
    }

    [Fact]
    public void EmaStep_at_alpha_zero_returns_prev_unchanged()
    {
        Assert.Equal(10, PowerMath.EmaStep(10, 999, 0), 10);
    }

    [Fact]
    public void EmaStep_at_alpha_one_snaps_to_sample()
    {
        Assert.Equal(999, PowerMath.EmaStep(10, 999, 1), 10);
    }

    [Fact]
    public void IsGap_false_just_at_the_3x_interval_boundary()
    {
        // dt > max(3*interval, 10); use interval=5 so 3*interval=15 dominates the 10s floor.
        // At exactly the boundary it's not a gap (strict >).
        Assert.False(PowerMath.IsGap(15, 5));
    }

    [Fact]
    public void IsGap_true_just_past_the_3x_interval_boundary()
    {
        Assert.True(PowerMath.IsGap(15.001, 5));
    }

    [Fact]
    public void IsGap_false_at_the_10_second_floor_for_tiny_intervals()
    {
        // interval=1 => 3*1=3, floored to 10; dt==10 is not > 10
        Assert.False(PowerMath.IsGap(10, 1));
        Assert.True(PowerMath.IsGap(10.001, 1));
    }

    [Fact]
    public void TimeToFull_null_when_not_charging()
    {
        Assert.Null(PowerMath.TimeToFull(remainingWh: 20, fullChargeWh: 50, emaChargeW: 10, charging: false));
    }

    [Fact]
    public void TimeToFull_null_when_charge_rate_at_or_below_half_watt()
    {
        Assert.Null(PowerMath.TimeToFull(20, 50, 0.5, charging: true));
    }

    [Fact]
    public void TimeToFull_null_when_full_charge_capacity_is_zero()
    {
        Assert.Null(PowerMath.TimeToFull(20, 0, 10, charging: true));
    }

    [Fact]
    public void TimeToFull_computes_hours_remaining_to_fill()
    {
        // (50-20)/10 = 3 hours
        var t = PowerMath.TimeToFull(20, 50, 10, charging: true);
        Assert.NotNull(t);
        Assert.Equal(3.0, t!.Value.TotalHours, 6);
    }

    [Fact]
    public void TimeToEmpty_null_when_not_discharging()
    {
        Assert.Null(PowerMath.TimeToEmpty(remainingWh: 20, emaDischargeW: 10, discharging: false));
    }

    [Fact]
    public void TimeToEmpty_null_when_discharge_rate_at_or_below_half_watt()
    {
        Assert.Null(PowerMath.TimeToEmpty(20, 0.5, discharging: true));
    }

    [Fact]
    public void TimeToEmpty_computes_hours_remaining_to_drain()
    {
        // 20/10 = 2 hours
        var t = PowerMath.TimeToEmpty(20, 10, discharging: true);
        Assert.NotNull(t);
        Assert.Equal(2.0, t!.Value.TotalHours, 6);
    }

    [Fact]
    public void WallInputW_sums_system_and_charge_over_efficiency()
    {
        // (15+5)/0.9
        Assert.Equal(20 / 0.9, PowerMath.WallInputW(15, 5, 0.9), 10);
    }

    [Fact]
    public void DirectionSlopeW_zero_below_min_span()
    {
        Assert.Equal(0, PowerMath.DirectionSlopeW(whNow: 10, wh0: 20, spanSeconds: 30, minSpanSeconds: 60));
    }

    [Fact]
    public void DirectionSlopeW_computed_exactly_at_min_span_boundary()
    {
        // span == minSpan is included (>=); (10-20)/(60/3600) = -600 W
        var slope = PowerMath.DirectionSlopeW(10, 20, 60, 60);
        Assert.Equal(-600, slope, 6);
    }

    [Fact]
    public void DirectionSlopeW_computes_watts_from_wh_delta_over_span()
    {
        // (25-20)/(3600/3600) = 5 W over one hour span
        var slope = PowerMath.DirectionSlopeW(25, 20, 3600, 60);
        Assert.Equal(5, slope, 6);
    }

    [Theory]
    [InlineData(true, -3.0001, 3, true)]   // just past the threshold -> contradiction
    [InlineData(true, -3, 3, false)]       // exactly at threshold -> not (strict <)
    [InlineData(false, -100, 3, false)]    // not claiming charging -> never a contradiction
    public void ClaimsChargingButDraining_matches_threshold(bool charging, double slopeW, double contradictionW, bool expected)
    {
        Assert.Equal(expected, PowerMath.ClaimsChargingButDraining(charging, slopeW, contradictionW));
    }

    [Theory]
    [InlineData(true, 3.0001, 3, true)]    // just past the threshold -> contradiction
    [InlineData(true, 3, 3, false)]        // exactly at threshold -> not (strict >)
    [InlineData(false, 100, 3, false)]     // not claiming discharging -> never a contradiction
    public void ClaimsDischargingButFilling_matches_threshold(bool discharging, double slopeW, double contradictionW, bool expected)
    {
        Assert.Equal(expected, PowerMath.ClaimsDischargingButFilling(discharging, slopeW, contradictionW));
    }

    [Fact]
    public void RateChanged_false_for_the_exact_same_value()
    {
        Assert.False(PowerMath.RateChanged(45.231, 45.231));
    }

    [Fact]
    public void RateChanged_false_for_float_round_trip_noise()
    {
        // WMI values arrive as mW then get divided by 1000.0 — sub-mW float noise from that
        // round trip must never register as a "new" reading.
        Assert.False(PowerMath.RateChanged(45.0, 45.0 + 0.0001));
    }

    [Fact]
    public void RateChanged_true_once_the_reading_actually_moves()
    {
        Assert.True(PowerMath.RateChanged(45.0, 45.6));
    }

    [Fact]
    public void RateChanged_true_across_a_charge_discharge_transition()
    {
        // e.g. discharging at 12W to charging at 8W — Sampler tracks whichever direction's
        // magnitude is active, so a state flip is just as much a "changed" value as any other
        Assert.True(PowerMath.RateChanged(12.0, 8.0));
    }
}
