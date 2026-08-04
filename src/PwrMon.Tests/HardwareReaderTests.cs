using PwrMon.Services;

namespace PwrMon.Tests;

/// <summary>
/// The miss-streak state machine behind <see cref="HardwareReader.Tier"/>: a source (LHM or
/// EMI) is "live" only while it has produced a real reading within the last
/// <see cref="HardwareReader.StaleAfterMisses"/> ticks, not merely at some point in the past.
/// Uses the internal members exposed via InternalsVisibleTo in PwrMon.csproj — no real
/// <see cref="LibreHardwareMonitor.Hardware.Computer"/> or Energy Meter counters involved.
/// </summary>
public class HardwareReaderTests
{
    [Fact]
    public void A_reading_resets_the_streak_to_zero()
    {
        Assert.Equal(0, HardwareReader.NextMissStreak(current: 5, gotReading: true));
    }

    [Fact]
    public void A_miss_increments_the_streak()
    {
        Assert.Equal(1, HardwareReader.NextMissStreak(current: 0, gotReading: false));
        Assert.Equal(4, HardwareReader.NextMissStreak(current: 3, gotReading: false));
    }

    [Fact]
    public void The_streak_caps_at_StaleAfterMisses_and_does_not_grow_unbounded()
    {
        var streak = 0;
        for (var i = 0; i < 100; i++)
            streak = HardwareReader.NextMissStreak(streak, gotReading: false);

        Assert.Equal(HardwareReader.StaleAfterMisses, streak);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(HardwareReader.StaleAfterMisses - 1, true)]
    [InlineData(HardwareReader.StaleAfterMisses, false)]
    [InlineData(HardwareReader.StaleAfterMisses + 1, false)]
    public void IsLive_is_true_only_below_the_stale_threshold(int missStreak, bool expectedLive)
    {
        Assert.Equal(expectedLive, HardwareReader.IsLive(missStreak));
    }

    [Fact]
    public void A_source_that_stops_reporting_falls_out_of_live_after_enough_consecutive_misses()
    {
        // Regression test: Tier used to gate on an ever-set high-water mark, so one good
        // reading pinned it at "live" (and the app at SensorTier.Full) for the rest of the
        // session even if the driver/counter category later disappeared entirely.
        var streak = 0; // one earlier good reading already reset it to 0
        Assert.True(HardwareReader.IsLive(streak));

        for (var i = 0; i < HardwareReader.StaleAfterMisses - 1; i++)
        {
            streak = HardwareReader.NextMissStreak(streak, gotReading: false);
            Assert.True(HardwareReader.IsLive(streak)); // still within tolerance
        }

        streak = HardwareReader.NextMissStreak(streak, gotReading: false);
        Assert.False(HardwareReader.IsLive(streak)); // enough consecutive misses: no longer live
    }

    [Fact]
    public void A_single_transient_miss_does_not_flip_liveness()
    {
        // One blip shouldn't cause a tier flap — this is the anti-flapping behavior the
        // streak-based design has to preserve relative to the old sticky-max approach.
        var streak = HardwareReader.NextMissStreak(0, gotReading: false);
        Assert.True(HardwareReader.IsLive(streak));

        streak = HardwareReader.NextMissStreak(streak, gotReading: true);
        Assert.Equal(0, streak);
        Assert.True(HardwareReader.IsLive(streak));
    }
}
