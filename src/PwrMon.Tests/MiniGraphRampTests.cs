using PwrMon.Views;

namespace PwrMon.Tests;

/// <summary>
/// The mini graph's opening window ramp. It has no backfill, so plotting the user's chosen
/// window immediately leaves the panel nearly empty — at "last 24 hours", for most of a day.
/// These pin that it opens narrow, tracks the data it actually has, and settles on the user's
/// choice without overshooting it.
/// </summary>
public class MiniGraphRampTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(59.4)]
    public void Opens_at_the_floor_before_a_minute_of_data(double span)
    {
        Assert.Equal(60, MiniGraphWindow.RampedWindowSeconds(span, chosenSeconds: 900));
    }

    [Fact]
    public void Tracks_the_data_span_once_past_the_floor()
    {
        // the whole point: window == data, so the line spans the full width rather than
        // trailing off into empty space
        Assert.Equal(120, MiniGraphWindow.RampedWindowSeconds(120, chosenSeconds: 900));
        Assert.Equal(500, MiniGraphWindow.RampedWindowSeconds(500, chosenSeconds: 900));
    }

    [Fact]
    public void Stops_at_the_users_choice_and_stays_there()
    {
        Assert.Equal(900, MiniGraphWindow.RampedWindowSeconds(900, chosenSeconds: 900));
        Assert.Equal(900, MiniGraphWindow.RampedWindowSeconds(901, chosenSeconds: 900));
        Assert.Equal(900, MiniGraphWindow.RampedWindowSeconds(86_400, chosenSeconds: 900));
    }

    /// <summary>The 24-hour case is what motivated this: an hour in, the old code plotted an
    /// hour of data across a 24-hour window — a smear against the left edge.</summary>
    [Fact]
    public void Fills_the_width_on_the_way_to_a_24_hour_window()
    {
        Assert.Equal(3_600, MiniGraphWindow.RampedWindowSeconds(3_600, chosenSeconds: 86_400));
        Assert.Equal(43_200, MiniGraphWindow.RampedWindowSeconds(43_200, chosenSeconds: 86_400));
        Assert.Equal(86_400, MiniGraphWindow.RampedWindowSeconds(90_000, chosenSeconds: 86_400));
    }

    [Fact]
    public void A_choice_at_or_below_the_floor_is_used_as_is()
    {
        // also guards Math.Clamp, whose bounds would invert if the floor were applied blindly
        Assert.Equal(60, MiniGraphWindow.RampedWindowSeconds(5, chosenSeconds: 60));
        Assert.Equal(30, MiniGraphWindow.RampedWindowSeconds(5, chosenSeconds: 30));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    public void Nonsense_spans_fall_back_to_the_floor(double span)
    {
        Assert.Equal(60, MiniGraphWindow.RampedWindowSeconds(span, chosenSeconds: 900));
    }

    /// <summary>Shrinking the chosen window mid-session takes effect immediately — the ramp is a
    /// ceiling on the way up, never a floor holding a wider view open.</summary>
    [Fact]
    public void Narrowing_the_choice_applies_at_once()
    {
        Assert.Equal(120, MiniGraphWindow.RampedWindowSeconds(dataSpanSeconds: 5_000, chosenSeconds: 120));
    }
}
