using PwrMon.Models;

namespace PwrMon.Services;

/// <summary>
/// The heartbeat: polls battery + silicon sensors on a background loop, computes
/// smoothed rates / time estimates / session statistics, and detects AC + sleep events.
/// </summary>
public sealed class Sampler : IDisposable
{
    private readonly BatteryReader _battery;
    private readonly HardwareReader _hardware;
    private readonly DriveTemperatureReader _driveTemp = new();
    private readonly CancellationTokenSource _cts = new();
    private PeriodicTimer? _timer;
    private Task? _loop;

    // EMA state (τ = 30 s) for stable time-to-full/empty estimates
    private const double EmaTauSeconds = 30;
    private double _emaCharge, _emaDischarge;
    private bool _wasCharging, _wasDischarging;

    // fuel-gauge direction sanity: some firmware (seen on ASUS adapter-assist, i.e. the
    // battery covering what the AC source can't) reports the drain in ChargeRate with
    // Charging=true. RemainingCapacity never lies, so its trend arbitrates the direction.
    private readonly Queue<(DateTimeOffset Time, double Wh)> _capTrend = new();
    private (bool, bool, bool) _trendFlags;
    private bool _directionOverridden;
    private const double TrendWindowSeconds = 90;
    private const double TrendMinSpanSeconds = 60;
    private const double TrendContradictionW = 3;

    // CPU package EMA (τ ≈ 15 s) used only in the wall estimate: the gauge publishes
    // charge rate every ~15–30 s, so summing an instantaneous CPU spike with a stale
    // charge reading double-counts. This term matches their freshness without going stale.
    private const double WallTauSeconds = 15;
    private double _emaPkgW = double.NaN;

    private DateTimeOffset _lastTick = DateTimeOffset.MinValue;
    private bool? _lastAc;

    // learned "system minus CPU package" watts (screen/RAM/SSD/board), measured while on
    // battery where total draw is exact; lets us estimate system + wall draw on AC
    private const double BaselineTauSeconds = 180;
    private const double AdapterEfficiency = 0.90;
    private double _baselineW = AppSettings.Current.LearnedSystemBaselineW;
    private DateTime _baselinePersisted = DateTime.UtcNow;

    // session accumulators
    private DateTimeOffset _sessionStart = DateTimeOffset.Now;
    private double _energyOutWh, _energyInWh, _peakDischargeW, _peakCpuW;
    private DateTimeOffset? _peakDischargeTime;
    private TimeSpan _timeOnBattery;

    /// <summary>Volume the drive temperature is being read from, e.g. "C:". Null until read.</summary>
    public string? DriveVolume => _driveTemp.HottestVolume;

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
            var gap = PowerMath.IsGap(dt, interval);
            _lastTick = now;

            var b = SanitizeDirection(_battery.Read(), now, gap);
            var h = _hardware.Read();

            if (h.CpuPackageW is double pkgEma)
                _emaPkgW = double.IsNaN(_emaPkgW)
                    ? pkgEma
                    : _emaPkgW + (1 - Math.Exp(-(gap ? interval : dt) / WallTauSeconds)) * (pkgEma - _emaPkgW);

            // --- EMA smoothing; snap to the instantaneous value when the charge state flips
            var dtClamped = gap ? interval : dt;
            var alpha = PowerMath.EmaAlpha(dtClamped, EmaTauSeconds);
            if (b.Charging && !_wasCharging) _emaCharge = b.ChargeRateW;
            if (b.Discharging && !_wasDischarging) _emaDischarge = b.DischargeRateW;
            if (b.Charging) _emaCharge = PowerMath.EmaStep(_emaCharge, b.ChargeRateW, alpha);
            if (b.Discharging) _emaDischarge = PowerMath.EmaStep(_emaDischarge, b.DischargeRateW, alpha);
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

            // --- power-budget estimation
            // baseline learns only while discharging OFF AC: on AC the adapter contributes
            // an unknown share (adapter assist), so discharge − pkg is not "rest of system"
            if (b.Discharging && !b.AcOnline && !gap && h.CpuPackageW is double pkgW && b.DischargeRateW > 1)
            {
                var rest = b.DischargeRateW - pkgW;
                if (rest is > 0.5 and < 200)
                {
                    var ab = 1 - Math.Exp(-dtClamped / BaselineTauSeconds);
                    _baselineW = double.IsNaN(_baselineW) ? rest : _baselineW + ab * (rest - _baselineW);
                    PersistBaselineIfDue();
                }
            }

            // system draw: exact from the pack off AC; otherwise CPU pkg + learned baseline.
            // While draining ON AC the discharge is only a lower bound — take the larger.
            double? baselineSys = h.CpuPackageW is double pw && !double.IsNaN(_baselineW) ? pw + _baselineW : null;
            double? estSystem;
            bool isEstimate;
            if (b.Discharging && !b.AcOnline && b.DischargeRateW > 0.5)
            {
                estSystem = b.DischargeRateW;
                isEstimate = false;
            }
            else
            {
                estSystem = b.Discharging && b.DischargeRateW > baselineSys.GetValueOrDefault()
                    ? b.DischargeRateW
                    : baselineSys;
                isEstimate = true;
            }

            // wall input: smoothed system term + charge, over adapter efficiency.
            // Unknowable while the battery is assisting the adapter, so blank it then.
            double? estWall = null;
            if (b.AcOnline && !b.Discharging && estSystem is double es)
            {
                var sysForWall = !double.IsNaN(_emaPkgW) && !double.IsNaN(_baselineW) ? _emaPkgW + _baselineW : es;
                estWall = PowerMath.WallInputW(sysForWall, b.ChargeRateW, AdapterEfficiency);
            }

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
                CpuTempMaxC = h.CpuTempMaxC,
                CpuTjMaxDeltaC = h.CpuTjMaxDeltaC,
                DriveTempC = _driveTemp.Read(),
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
                TimeToFull = PowerMath.TimeToFull(b.RemainingWh, b.FullChargeWh, _emaCharge, b.Charging),
                TimeToEmpty = PowerMath.TimeToEmpty(b.RemainingWh, _emaDischarge, b.Discharging),
                EstSystemW = estSystem,
                EstWallW = estWall,
                IsSystemEstimate = isEstimate,
                LearnedBaselineW = _baselineW,
            };

            SampleReady?.Invoke(sample, stats, estimates);
        }
        catch (Exception ex)
        {
            Log.Error("sampler tick", ex);
        }
    }

    /// <summary>Flip charge/discharge direction when the capacity trend contradicts the
    /// firmware's flags. Magnitude comes from the reported rate (observed accurate even
    /// when the direction is wrong), with the measured trend as a floor.</summary>
    private BatteryReading SanitizeDirection(BatteryReading b, DateTimeOffset now, bool gap)
    {
        if (!b.HasBattery) { _capTrend.Clear(); return b; }

        // the trend is only meaningful while the reported state is stable — reset on any
        // flag change (a real plug/unplug makes capacity lag the flags briefly) or sleep gap
        var flags = (b.Charging, b.Discharging, b.AcOnline);
        if (gap || flags != _trendFlags) _capTrend.Clear();
        _trendFlags = flags;

        _capTrend.Enqueue((now, b.RemainingWh));
        while ((now - _capTrend.Peek().Time).TotalSeconds > TrendWindowSeconds)
            _capTrend.Dequeue();

        var (t0, wh0) = _capTrend.Peek();
        var spanSec = (now - t0).TotalSeconds;
        var slopeW = PowerMath.DirectionSlopeW(b.RemainingWh, wh0, spanSec, TrendMinSpanSeconds);

        var claimsChargingButDraining = PowerMath.ClaimsChargingButDraining(b.Charging, slopeW, TrendContradictionW);
        var claimsDischargingButFilling = PowerMath.ClaimsDischargingButFilling(b.Discharging, slopeW, TrendContradictionW);
        var flip = claimsChargingButDraining || claimsDischargingButFilling;
        if (flip != _directionOverridden)
        {
            Log.Info(flip
                ? $"fuel-gauge direction override: firmware says {(b.Charging ? "charging" : "discharging")} but capacity trend is {slopeW:F1} W"
                : "fuel-gauge direction override cleared");
            _directionOverridden = flip;
        }

        // reported magnitude is primary (observed accurate even with the direction wrong);
        // the trend slope is quantization-noisy, so it's only the fallback when rate ≈ 0
        if (claimsChargingButDraining)
            return b with { Charging = false, Discharging = true, ChargeRateW = 0, DischargeRateW = b.ChargeRateW > 1 ? b.ChargeRateW : -slopeW };
        if (claimsDischargingButFilling)
            return b with { Charging = true, Discharging = false, ChargeRateW = b.DischargeRateW > 1 ? b.DischargeRateW : slopeW, DischargeRateW = 0 };
        return b;
    }

    private void PersistBaselineIfDue()
    {
        if ((DateTime.UtcNow - _baselinePersisted).TotalSeconds < 120) return;
        if (Math.Abs((double.IsNaN(AppSettings.Current.LearnedSystemBaselineW) ? 0 : AppSettings.Current.LearnedSystemBaselineW) - _baselineW) < 0.5) return;
        AppSettings.Current.LearnedSystemBaselineW = _baselineW;
        AppSettings.Save();
        _baselinePersisted = DateTime.UtcNow;
    }

    public void Dispose()
    {
        if (!double.IsNaN(_baselineW))
        {
            AppSettings.Current.LearnedSystemBaselineW = _baselineW;
            AppSettings.Save();
        }
        _cts.Cancel();
        try { _loop?.Wait(1500); } catch { }
        _timer?.Dispose();
        _cts.Dispose();
    }
}
