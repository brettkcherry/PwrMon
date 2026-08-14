using PwrMon.Services;

namespace PwrMon.Tests;

/// <summary>
/// The learned heavy-draw threshold: histogram maths and the fallback ladder that decides what
/// "heavy" means before this machine has said anything about itself.
/// </summary>
public class DrawProfileTests
{
    // ---- percentiles over a time-weighted histogram ----

    [Fact]
    public void Percentile_of_empty_profile_is_null()
    {
        Assert.Null(new DrawProfile().Percentile(0.9));
        Assert.Null(DrawProfile.PercentileOf(new double[10], 0, 0.9));
    }

    [Fact]
    public void Percentile_of_a_single_draw_is_that_draw()
    {
        var p = new DrawProfile();
        p.Add(watts: 12, seconds: 600);

        // bin 12 spans 12..13 W and we report its upper edge
        Assert.Equal(13, p.Percentile(0.5));
        Assert.Equal(13, p.Percentile(0.9));
    }

    [Fact]
    public void Percentile_splits_a_two_level_profile_where_the_time_says_it_should()
    {
        var p = new DrawProfile();
        p.Add(watts: 10, seconds: 900);   // 90% of the time idling
        p.Add(watts: 50, seconds: 100);   // 10% of the time working

        Assert.Equal(11, p.Percentile(0.5));    // median is still idle
        Assert.Equal(51, p.Percentile(0.95));   // the top tail is the working draw
    }

    [Fact]
    public void Percentile_is_time_weighted_not_sample_counted()
    {
        // Same two draws, but recorded at different sampling intervals. The one that occupied
        // more *time* has to dominate regardless of how many calls described it.
        var fine = new DrawProfile();
        for (var i = 0; i < 100; i++) fine.Add(10, 1);   // 100 calls, 100 s
        fine.Add(80, 900);                                // 1 call, 900 s

        Assert.Equal(81, fine.Percentile(0.5));           // 900 s of 80 W is the majority
    }

    [Fact]
    public void Draws_above_the_top_bin_saturate_rather_than_being_lost()
    {
        var p = new DrawProfile();
        p.Add(watts: 5000, seconds: 100);

        Assert.Equal(DrawProfile.BinCount, p.Percentile(0.5));
        Assert.Equal(DrawProfile.BinCount - 1, DrawProfile.BinOf(5000));
    }

    [Theory]
    [InlineData(-5)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Nonsense_draws_are_ignored(double watts)
    {
        var p = new DrawProfile();
        p.Add(watts, 100);
        Assert.Equal(0, p.TotalSeconds);
    }

    [Fact]
    public void Zero_or_negative_durations_are_ignored()
    {
        var p = new DrawProfile();
        p.Add(20, 0);
        p.Add(20, -5);
        Assert.Equal(0, p.TotalSeconds);
    }

    // ---- the learning gate ----

    [Fact]
    public void Profile_is_not_trusted_until_it_has_seen_enough_battery_time()
    {
        var p = new DrawProfile();
        p.Add(20, DrawProfile.MinSecondsToTrust - 1);
        Assert.False(p.IsLearned);

        p.Add(20, 1);
        Assert.True(p.IsLearned);
    }

    [Fact]
    public void Decay_halves_the_profile_but_keeps_its_shape()
    {
        var p = new DrawProfile();
        // two draws in a 3:1 time ratio, pushed past the decay point
        p.Add(10, DrawProfile.DecayAtSeconds * 0.75);
        var before = p.Percentile(0.5);
        p.Add(50, DrawProfile.DecayAtSeconds * 0.25);

        Assert.Equal(DrawProfile.DecayAtSeconds * 0.5, p.TotalSeconds, 1);
        Assert.Equal(before, p.Percentile(0.5));   // still idle-dominated at the median
        Assert.Equal(51, p.Percentile(0.95));      // and the tail survived the halving
    }

    // ---- the fallback ladder ----

    [Fact]
    public void Capacity_derived_threshold_is_the_draw_that_empties_the_pack_in_the_floor_time()
    {
        // 56 Wh (a ~20%-worn 70 Wh pack) over 1.2 h
        Assert.Equal(56 / 1.2, PowerMath.CapacityDerivedHeavyDrawW(56), 3);
    }

    [Fact]
    public void Capacity_derived_threshold_scales_with_the_pack()
    {
        var tablet = PowerMath.CapacityDerivedHeavyDrawW(26);
        var laptop = PowerMath.CapacityDerivedHeavyDrawW(56);
        var gaming = PowerMath.CapacityDerivedHeavyDrawW(90);

        Assert.True(tablet < laptop && laptop < gaming);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    public void Unreadable_capacity_falls_back_to_the_shipped_constant(double wh)
    {
        Assert.Equal(PowerMath.FallbackHeavyDrawW, PowerMath.CapacityDerivedHeavyDrawW(wh));
    }

    [Fact]
    public void Capacity_derived_threshold_is_clamped_against_absurd_packs()
    {
        Assert.Equal(15, PowerMath.CapacityDerivedHeavyDrawW(2));        // UPS/firmware nonsense
        Assert.Equal(150, PowerMath.CapacityDerivedHeavyDrawW(10_000));
    }

    // ---- hysteresis ----

    [Fact]
    public void Heavy_draw_trips_above_the_trip_point()
    {
        Assert.True(PowerMath.IsHeavyDraw(wasHeavy: false, drawW: 51, tripW: 50, releaseW: 40));
        Assert.False(PowerMath.IsHeavyDraw(wasHeavy: false, drawW: 49, tripW: 50, releaseW: 40));
    }

    [Fact]
    public void Once_heavy_it_stays_heavy_until_the_draw_falls_to_the_release_point()
    {
        // between release and trip, the previous state is what decides — this is the whole
        // point of having two thresholds
        Assert.True(PowerMath.IsHeavyDraw(wasHeavy: true, drawW: 45, tripW: 50, releaseW: 40));
        Assert.False(PowerMath.IsHeavyDraw(wasHeavy: false, drawW: 45, tripW: 50, releaseW: 40));

        Assert.False(PowerMath.IsHeavyDraw(wasHeavy: true, drawW: 39, tripW: 50, releaseW: 40));
    }

    [Fact]
    public void A_load_sitting_exactly_on_the_trip_point_does_not_oscillate()
    {
        // walk a load up through the trip point and back down; the state may change at most
        // once in each direction, never tick-to-tick
        var heavy = false;
        var flips = 0;
        foreach (var w in new double[] { 44, 46, 48, 50, 52, 50, 48, 46, 44, 46, 48 })
        {
            var next = PowerMath.IsHeavyDraw(heavy, w, tripW: 50, releaseW: 40);
            if (next != heavy) flips++;
            heavy = next;
        }

        Assert.Equal(1, flips);   // trips once at 52 and never clears, because it never hit 40
        Assert.True(heavy);
    }
}
