using System.Media;
using PwrMon.Models;

namespace PwrMon.Services;

/// <summary>
/// The siren for the adapter-assist smoke detector.
///
/// <see cref="Sampler.SanitizeDirection"/> has been able to convict "plugged in but actually
/// draining" since the 2026-07-16 TB4 incident, but it only ever surfaced passively — a red
/// hero state in a window that may be closed, and a tray tooltip you have to hover. On
/// 2026-08-13 the machine drained 91% → flat over 70 minutes with the detector correct the
/// entire way and nothing reaching out. This class is the missing output path.
///
/// Strictly scoped to the AC-contradiction case: on-battery low-battery warnings are
/// Windows' job and it does them fine. The whole point here is the state Windows *can't*
/// warn about, because as far as it's concerned the machine is plugged in and happy.
/// </summary>
public sealed class DrainAlertService
{
    /// <summary>Battery levels that re-alert on the way down. The first alert can easily land
    /// while the user is away from the machine, so the warning has to repeat as it gets worse.</summary>
    private static readonly int[] Thresholds = { 50, 35, 20, 15, 10 };

    /// <summary>How long the drain-on-AC state must hold before the first alert. The gauge
    /// lags a plug/unplug by a poll or two, so an instant alert would cry wolf on every
    /// reconnect; the direction override upstream needs 60–90 s to convict anyway.</summary>
    private const double ConfirmSeconds = 20;

    private readonly Action<string, string> _notify;

    private DateTimeOffset? _drainingSince;
    private bool _announced;
    private readonly HashSet<int> _armed = new();

    /// <param name="notify">Shows the alert. Called on whichever thread feeds
    /// <see cref="OnSample"/> — the caller marshals to the UI thread.</param>
    public DrainAlertService(Action<string, string> notify) => _notify = notify;

    public void OnSample(PowerSample s, Estimates est)
    {
        // Post-sanitize flags, so this covers both the honest firmware case (AC online,
        // reported discharging) and the lying one the direction override rescues.
        if (!(s.HasBattery && s.AcOnline && s.Discharging))
        {
            if (_announced) Log.Info("drain-on-AC cleared");
            _drainingSince = null;
            _announced = false;
            _armed.Clear();
            return;
        }

        _drainingSince ??= s.Time;
        if ((s.Time - _drainingSince.Value).TotalSeconds < ConfirmSeconds) return;

        var rate = UnitFormatter.Power(s.DischargeRateW);
        var left = est.TimeToEmpty is not null ? $", {UnitFormatter.Duration(est.TimeToEmpty)} left" : "";

        if (!_announced)
        {
            _announced = true;
            // Only arm levels we're still above: starting a drain at 12% shouldn't dump the
            // 50/35/20/15 alerts all at once.
            foreach (var t in Thresholds)
                if (s.BatteryPercent > t) _armed.Add(t);

            Log.Info($"drain-on-AC alert: {s.DischargeRateW:F1} W at {s.BatteryPercent:F0}%");
            Alert("PwrMon — PLUGGED IN, DRAINING",
                  $"The charger is connected but the battery is losing {rate} at {s.BatteryPercent:F0}%{left}. Check the adapter and the port.",
                  critical: false);
            return;
        }

        // Fire only the lowest level crossed, and disarm everything at or above it — a jump
        // from 22% to 14% is one alert ("15%"), not two stacked balloons.
        var crossed = Thresholds.Where(t => _armed.Contains(t) && s.BatteryPercent <= t).ToList();
        if (crossed.Count == 0) return;

        var lowest = crossed.Min();
        foreach (var t in crossed) _armed.Remove(t);

        Log.Info($"drain-on-AC alert: crossed {lowest}% at {s.DischargeRateW:F1} W");
        Alert($"PwrMon — STILL DRAINING at {lowest}%",
              $"Plugged in and still losing {rate}{left}. The charger is not holding this machine up.",
              critical: lowest <= 20);
    }

    private void Alert(string title, string body, bool critical)
    {
        if (AppSettings.Current.DrainAlertSound)
        {
            // Hand is the system's "this is going wrong" sound; Exclamation is the softer
            // heads-up. Escalating at 20% matches the alert text getting blunter.
            try { (critical ? SystemSounds.Hand : SystemSounds.Exclamation).Play(); }
            catch (Exception ex) { Log.Error($"drain alert sound: {ex.Message}"); }
        }
        _notify(title, body);
    }
}
