namespace PwrMon.Models;

/// <summary>One point-in-time reading of everything the machine reports about power.</summary>
public sealed class PowerSample
{
    public DateTimeOffset Time { get; init; }

    public bool HasBattery { get; init; }
    public bool AcOnline { get; init; }
    public bool Charging { get; init; }
    public bool Discharging { get; init; }

    /// <summary>Power flowing into the battery, watts (0 when not charging).</summary>
    public double ChargeRateW { get; init; }
    /// <summary>Power flowing out of the battery, watts (0 when not discharging).</summary>
    public double DischargeRateW { get; init; }
    /// <summary>Signed battery flow: positive = charging, negative = discharging.</summary>
    public double NetW => ChargeRateW - DischargeRateW;

    /// <summary>How long the raw fuel-gauge rate has held its current value. The gauge
    /// publishes on its own quantized ~15–30 s cadence regardless of how often we poll it, so
    /// this can run well past the sampling interval — see
    /// <see cref="PwrMon.Services.UnitFormatter.IsStale"/>. Live display only, not persisted.</summary>
    public TimeSpan RateAge { get; init; }

    public double BatteryPercent { get; init; }
    public double RemainingWh { get; init; }
    public double FullChargeWh { get; init; }
    public double VoltageV { get; init; }
    /// <summary>Battery current in amps derived from rate/voltage; signed like <see cref="NetW"/>.</summary>
    public double CurrentA => VoltageV > 1 ? NetW / VoltageV : 0;

    // CPU/GPU silicon telemetry (null when the sensor tier doesn't provide it).
    public double? CpuPackageW { get; init; }
    public double? CpuCoresW { get; init; }
    public double? CpuPlatformW { get; init; }
    public double? IGpuW { get; init; }
    public double? CpuLoadPct { get; init; }
    public double? CpuTempC { get; init; }
    public double? GpuLoadPct { get; init; }
    public double? GpuClockMhz { get; init; }

    /// <summary>Hottest single core in °C — full tier only. Live display only, not persisted.</summary>
    public double? CpuTempMaxC { get; init; }
    /// <summary>Headroom in °C between the hottest core and its throttle point. Small = about
    /// to throttle. Full tier only; live display only, not persisted.</summary>
    public double? CpuTjMaxDeltaC { get; init; }

    /// <summary>Hottest fixed drive in °C. Unlike <see cref="CpuTempC"/> this needs no
    /// elevation or driver, so it's the one temperature available in the default tier.</summary>
    public double? DriveTempC { get; init; }

    /// <summary>Estimated wall/adapter input in watts — the one derived value that lives on the
    /// sample rather than on <see cref="Estimates"/>, because it has to be persisted to be
    /// chartable after a restart and the CSV row is built from a sample alone. Null whenever it
    /// is unknowable: off AC, and during adapter assist (the battery is covering an unknown
    /// share of the load, so "system + charge" no longer describes what the wall is supplying).</summary>
    public double? EstWallW { get; init; }

    /// <summary>True when the wall-clock gap since the previous sample is large (sleep/hibernate) —
    /// charts should break the line before this sample.</summary>
    public bool GapBefore { get; init; }
}

/// <summary>Static battery pack facts; refreshed rarely.</summary>
public sealed class BatteryStaticInfo
{
    public bool HasBattery { get; init; }
    public double DesignWh { get; init; }
    public double FullChargeWh { get; init; }
    public int? CycleCount { get; init; }
    public string Chemistry { get; init; } = "";
    public string Manufacturer { get; init; } = "";
    public string DeviceName { get; init; } = "";
    public double DesignVoltageV { get; init; }
    public double WearPct => DesignWh > 0 ? Math.Max(0, (DesignWh - FullChargeWh) / DesignWh * 100.0) : 0;
}

/// <summary>Rolling statistics since app start (or manual reset).</summary>
public sealed class SessionStats
{
    public DateTimeOffset StartTime { get; init; }
    public double EnergyOutWh { get; init; }
    public double EnergyInWh { get; init; }
    public double PeakDischargeW { get; init; }
    public DateTimeOffset? PeakDischargeTime { get; init; }
    public double PeakCpuW { get; init; }
    public TimeSpan TimeOnBattery { get; init; }
    public double AvgDischargeW => TimeOnBattery.TotalHours > 0.003 ? EnergyOutWh / TimeOnBattery.TotalHours : 0;
}

/// <summary>Smoothed time predictions and derived power-budget figures.</summary>
public sealed class Estimates
{
    public TimeSpan? TimeToEmpty { get; init; }
    public TimeSpan? TimeToFull { get; init; }
    public double SmoothedDischargeW { get; init; }
    public double SmoothedChargeW { get; init; }

    /// <summary>Total system draw. Exact (from the battery) while discharging; on AC it is
    /// CPU package + the baseline learned during battery sessions.</summary>
    public double? EstSystemW { get; init; }
    /// <summary>Estimated wall/adapter input: system + battery charging, over ~90% adapter efficiency.</summary>
    public double? EstWallW { get; init; }
    /// <summary>True when <see cref="EstSystemW"/> is the learned estimate rather than a measurement.</summary>
    public bool IsSystemEstimate { get; init; }
    /// <summary>The learned "everything except CPU package" draw (screen, RAM, SSD, board). NaN until learned.</summary>
    public double LearnedBaselineW { get; init; } = double.NaN;

    /// <summary>Discharge above which this machine's draw counts as heavy — the p90 of its own
    /// observed distribution once <see cref="HeavyDrawLearned"/>, and a capacity-derived stand-in
    /// before that. See <see cref="PwrMon.Services.DrawProfile"/>.</summary>
    public double HeavyDrawTripW { get; init; } = PwrMon.Services.PowerMath.FallbackHeavyDrawW;
    /// <summary>Where a tripped heavy-draw state clears again. Below the trip point on purpose;
    /// see <see cref="PwrMon.Services.PowerMath.IsHeavyDraw"/>.</summary>
    public double HeavyDrawReleaseW { get; init; } = PwrMon.Services.PowerMath.FallbackHeavyDrawW;
    /// <summary>True when the thresholds above come from this machine's observed history rather
    /// than from its battery capacity.</summary>
    public bool HeavyDrawLearned { get; init; }
}

public enum PowerEventKind { AcConnected, AcDisconnected, Resumed, AppStarted }

/// <summary>Notable moment worth marking on the history chart.</summary>
public sealed record PowerEvent(DateTimeOffset Time, PowerEventKind Kind)
{
    public string Label => Kind switch
    {
        PowerEventKind.AcConnected => "AC in",
        PowerEventKind.AcDisconnected => "AC out",
        PowerEventKind.Resumed => "resume",
        _ => "start",
    };
}

/// <summary>What level of silicon telemetry the current process can reach.</summary>
public enum SensorTier
{
    /// <summary>LHM failed to initialize at all.</summary>
    LhmFailed,
    /// <summary>No power source at all and not elevated.</summary>
    NeedsAdmin,
    /// <summary>Elevated but the kernel sensor driver could not load (e.g. HVCI blocklist, PawnIO missing).</summary>
    DriverBlocked,
    /// <summary>CPU/iGPU watts flowing via Windows' Energy Meter (EMI RAPL) counters — no admin
    /// needed. Temps/platform power still require the full driver tier.</summary>
    EmiOnly,
    /// <summary>Full RAPL telemetry flowing through the kernel driver.</summary>
    Full,
    /// <summary>Still warming up / undetermined.</summary>
    Probing,
}
