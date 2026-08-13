using PwrMon.Models;
using PwrMon.Services;

namespace PwrMon.Tests;

/// <summary>The siren logic for "plugged in but draining" — the 2026-08-13 failure mode.
/// Alert text is not asserted, only when and how often alerts fire.</summary>
public class DrainAlertServiceTests
{
    private static readonly DateTimeOffset T0 = new(2026, 8, 13, 10, 47, 0, TimeSpan.Zero);
    private static readonly Estimates NoEstimates = new();

    private sealed class Harness
    {
        public readonly List<string> Alerts = new();
        public readonly DrainAlertService Service;
        private DateTimeOffset _now = T0;

        public Harness()
        {
            AppSettings.Current.DrainAlertSound = false; // no audio from a test run
            Service = new DrainAlertService((title, _) => Alerts.Add(title));
        }

        /// <summary>Feeds one sample <paramref name="afterSeconds"/> later.</summary>
        public void Feed(double percent, bool ac, bool discharging, double afterSeconds = 1)
        {
            _now = _now.AddSeconds(afterSeconds);
            Service.OnSample(new PowerSample
            {
                Time = _now,
                HasBattery = true,
                AcOnline = ac,
                Discharging = discharging,
                Charging = !discharging && ac,
                DischargeRateW = discharging ? 31 : 0,
                BatteryPercent = percent,
            }, NoEstimates);
        }
    }

    [Fact]
    public void Does_not_alert_before_the_confirm_window_elapses()
    {
        var h = new Harness();
        h.Feed(90, ac: true, discharging: true);
        h.Feed(90, ac: true, discharging: true, afterSeconds: 10);
        Assert.Empty(h.Alerts);
    }

    [Fact]
    public void Alerts_once_when_drain_on_ac_is_sustained()
    {
        var h = new Harness();
        h.Feed(90, ac: true, discharging: true);
        h.Feed(89, ac: true, discharging: true, afterSeconds: 30);
        h.Feed(88, ac: true, discharging: true, afterSeconds: 30);
        Assert.Single(h.Alerts);
    }

    [Fact]
    public void Never_alerts_on_battery_that_is_honestly_discharging()
    {
        var h = new Harness();
        for (var pct = 90; pct >= 5; pct -= 5)
            h.Feed(pct, ac: false, discharging: true, afterSeconds: 60);
        Assert.Empty(h.Alerts);
    }

    [Fact]
    public void Never_alerts_while_charging_normally()
    {
        var h = new Harness();
        for (var pct = 50; pct <= 90; pct += 5)
            h.Feed(pct, ac: true, discharging: false, afterSeconds: 60);
        Assert.Empty(h.Alerts);
    }

    [Fact]
    public void Re_alerts_at_each_threshold_on_the_way_down()
    {
        var h = new Harness();
        h.Feed(91, ac: true, discharging: true);
        h.Feed(91, ac: true, discharging: true, afterSeconds: 30); // entry alert
        foreach (var pct in new[] { 60, 50, 40, 35, 25, 20, 15, 12, 10, 5 })
            h.Feed(pct, ac: true, discharging: true, afterSeconds: 60);

        // entry + 50 + 35 + 20 + 15 + 10
        Assert.Equal(6, h.Alerts.Count);
    }

    [Fact]
    public void A_steep_drop_fires_one_alert_for_the_lowest_level_crossed()
    {
        var h = new Harness();
        h.Feed(22, ac: true, discharging: true);
        h.Feed(22, ac: true, discharging: true, afterSeconds: 30); // entry alert
        h.Feed(14, ac: true, discharging: true, afterSeconds: 60); // crosses 20 and 15 at once

        Assert.Equal(2, h.Alerts.Count);
        Assert.Contains("15%", h.Alerts[1]);
    }

    [Fact]
    public void Thresholds_already_below_the_starting_level_never_fire()
    {
        var h = new Harness();
        h.Feed(12, ac: true, discharging: true);
        h.Feed(12, ac: true, discharging: true, afterSeconds: 30); // entry alert only
        h.Feed(11, ac: true, discharging: true, afterSeconds: 60);

        Assert.Single(h.Alerts); // no burst of 50/35/20/15 on the way in
    }

    [Fact]
    public void Recovering_and_relapsing_re_arms_the_alerts()
    {
        var h = new Harness();
        h.Feed(80, ac: true, discharging: true);
        h.Feed(80, ac: true, discharging: true, afterSeconds: 30); // entry alert
        h.Feed(80, ac: true, discharging: false, afterSeconds: 60); // charger recovers
        h.Feed(78, ac: true, discharging: true, afterSeconds: 60);
        h.Feed(78, ac: true, discharging: true, afterSeconds: 30); // second entry alert

        Assert.Equal(2, h.Alerts.Count);
    }

    [Fact]
    public void Unplugging_clears_the_state_without_alerting()
    {
        var h = new Harness();
        h.Feed(80, ac: true, discharging: true);
        h.Feed(80, ac: true, discharging: true, afterSeconds: 30); // entry alert
        foreach (var pct in new[] { 60, 40, 20, 10 })
            h.Feed(pct, ac: false, discharging: true, afterSeconds: 60); // now genuinely on battery

        Assert.Single(h.Alerts);
    }
}
