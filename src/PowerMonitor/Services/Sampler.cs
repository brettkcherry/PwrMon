using PowerMonitor.Models;

namespace PowerMonitor.Services;

/// <summary>
/// The heartbeat: polls battery + silicon sensors on a background loop, computes
/// smoothed rates / time estimates / session statistics, and detects AC + sleep events.
/// </summary>
public sealed class Sampler : IDisposable
{
    private readonly BatteryReader _battery;
    private readonly HardwareReader _hardware;
    private readonly CancellationTokenSource _cts = new();
    private PeriodicTimer? _timer;
    private Task? _loop;

    // EMA state (τ = 30 s) for stable time-to-full/empty estimates
    private const double EmaTauSeconds = 30;
    private double _emaCharge, _emaDischarge;
    private bool _wasCharging, _wasDischarging;

    private DateTimeOffset _lastTick = DateTimeOffset.MinValue;
    private bool? _lastAc;

    // session accumulators
    private DateTimeOffset _sessionStart = DateTimeOffset.Now;
    private double _energyOutWh, _energyInWh, _peakDischargeW, _peakCpuW;
    private DateTimeOffset? _peakDischargeTime;
    private TimeSpan _timeOnBattery;

    public event Action<PowerSample, SessionStats, Estimates>? SampleReady;
    public event Action<PowerEvent>? PowerEventRaised;

    public Sampler(BatteryReader battery, HardwareReader hardware)
    {
        _battery = battery;
        _hardware = hardware;
    }

    public void Start()
    {
        _timer = new PeriodicTimer(TimeSpan.FromSeconds(AppSettings.Current.SamplingIntervalSeconds));
        _loop = Task.Run(LoopAsync);
    }

    public void SetInterval(double seconds)
    {
        if (_timer is not null)
            _timer.Period = TimeSpan.FromSeconds(Math.Clamp(seconds, 0.5, 10));
    }

    /// <summary>Called from the UI thread when Windows reports resume-from-sleep.</summary>
    public void InjectEvent(PowerEventKind kind) =>
        PowerEventRaised?.Invoke(new PowerEvent(DateTimeOffset.Now, kind));

    private volatile bool _reinitRequested;

    /// <summary>LHM must stay on the sampler thread; the reinit happens at the next tick.</summary>
    public void RequestHardwareReinit() => _reinitRequested = true;

    public void ResetSession()
    {
        _sessionStart = DateTimeOffset.Now;
        _energyOutWh = _energyInWh = _peakDischargeW = _peakCpuW = 0;
        _peakDischargeTime = null;
        _timeOnBattery = TimeSpan.Zero;
    }

    private async Task LoopAsync()
    {
        _hardware.Init();
        Tick(); // immediate first sample so the UI isn't empty for a full interval
        try
        {
            while (await _timer!.WaitForNextTickAsync(_cts.Token))
                Tick();
        }
        catch (OperationCanceledException) { }
    }

    private void Tick()
    {
        try
        {
            if (_reinitRequested)
            {
                _reinitRequested = false;
                _hardware.Reinit();
            }

            var now = DateTimeOffset.Now;
            var interval = AppSettings.Current.SamplingIntervalSeconds;
            var dt = _lastTick == DateTimeOffset.MinValue ? interval : (now - _lastTick).TotalSeconds;
            var gap = dt > Math.Max(3 * interval, 10);
            _lastTick = now;

            var b = _battery.Read();
            var h = _hardware.Read();

            // --- EMA smoothing; snap to the instantaneous value when the charge state flips
            var dtClamped = gap ? interval : dt;
            var alpha = 1 - Math.Exp(-dtClamped / EmaTauSeconds);
            if (b.Charging && !_wasCharging) _emaCharge = b.ChargeRateW;
            if (b.Discharging && !_wasDischarging) _emaDischarge = b.DischargeRateW;
            if (b.Charging) _emaCharge += alpha * (b.ChargeRateW - _emaCharge);
            if (b.Discharging) _emaDischarge += alpha * (b.DischargeRateW - _emaDischarge);
            _wasCharging = b.Charging;
            _wasDischarging = b.Discharging;

            // --- session accumulation (dt clamped so sleep doesn't fabricate energy)
            var hours = dtClamped / 3600.0;
            _energyOutWh += b.DischargeRateW * hours;
            _energyInWh += b.ChargeRateW * hours;
            if (b.Discharging)
            {
                _timeOnBattery += TimeSpan.FromSeconds(dtClamped);
                if (b.DischargeRateW > _peakDischargeW)
                {
                    _peakDischargeW = b.DischargeRateW;
                    _peakDischargeTime = now;
                }
            }
            if (h.CpuPackageW is double cw && cw > _peakCpuW) _peakCpuW = cw;

            // --- AC transition detection
            if (_lastAc is bool prevAc && prevAc != b.AcOnline)
                PowerEventRaised?.Invoke(new PowerEvent(now, b.AcOnline ? PowerEventKind.AcConnected : PowerEventKind.AcDisconnected));
            _lastAc = b.AcOnline;

            var sample = new PowerSample
            {
                Time = now,
                HasBattery = b.HasBattery,
                AcOnline = b.AcOnline,
                Charging = b.Charging,
                Discharging = b.Discharging,
                ChargeRateW = b.ChargeRateW,
                DischargeRateW = b.DischargeRateW,
                BatteryPercent = b.Percent,
                RemainingWh = b.RemainingWh,
                FullChargeWh = b.FullChargeWh,
                VoltageV = b.VoltageV,
                CpuPackageW = h.CpuPackageW,
                CpuCoresW = h.CpuCoresW,
                CpuPlatformW = h.CpuPlatformW,
                IGpuW = h.IGpuW,
                CpuLoadPct = h.CpuLoadPct,
                CpuTempC = h.CpuTempC,
                GpuLoadPct = h.GpuLoadPct,
                GpuClockMhz = h.GpuClockMhz,
                GapBefore = gap,
            };

            var stats = new SessionStats
            {
                StartTime = _sessionStart,
                EnergyOutWh = _energyOutWh,
                EnergyInWh = _energyInWh,
                PeakDischargeW = _peakDischargeW,
                PeakDischargeTime = _peakDischargeTime,
                PeakCpuW = _peakCpuW,
                TimeOnBattery = _timeOnBattery,
            };

            var estimates = new Estimates
            {
                SmoothedChargeW = _emaCharge,
                SmoothedDischargeW = _emaDischarge,
                TimeToFull = b.Charging && _emaCharge > 0.5 && b.FullChargeWh > 0
                    ? TimeSpan.FromHours((b.FullChargeWh - b.RemainingWh) / _emaCharge)
                    : null,
                TimeToEmpty = b.Discharging && _emaDischarge > 0.5
                    ? TimeSpan.FromHours(b.RemainingWh / _emaDischarge)
                    : null,
            };

            SampleReady?.Invoke(sample, stats, estimates);
        }
        catch (Exception ex)
        {
            Log.Error("sampler tick", ex);
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _loop?.Wait(1500); } catch { }
        _timer?.Dispose();
        _cts.Dispose();
    }
}
