using System.Diagnostics;
using System.Security.Principal;
using LibreHardwareMonitor.Hardware;
using PwrMon.Models;

namespace PwrMon.Services;

public sealed record HardwareReading(
    double? CpuPackageW,
    double? CpuCoresW,
    double? CpuPlatformW,
    double? CpuLoadPct,
    double? CpuTempC,
    double? CpuTempMaxC,
    double? CpuTjMaxDeltaC,
    double? IGpuW,
    double? GpuLoadPct,
    double? GpuClockMhz);

/// <summary>
/// Wraps LibreHardwareMonitor for CPU/iGPU silicon power. All access must stay on the
/// sampler thread. RAPL power sensors report a flat 0 when the kernel driver isn't
/// available (non-admin, or HVCI blocking WinRing0 without PawnIO installed) — tier
/// detection below turns that into actionable UI state.
/// </summary>
public sealed class HardwareReader : IDisposable
{
    private Computer? _computer;
    private ISensor? _cpuPackage, _cpuCores, _cpuPlatform, _cpuLoad, _cpuTemp, _cpuTempMax;
    private ISensor? _gpuPower, _gpuLoad, _gpuClock;
    // Per-core "Distance to TjMax"; LHM offers no aggregate, so the hottest core is the min.
    private readonly List<ISensor> _tjMaxDistances = new();
    private int _updates;
    private float _maxPowerSeen;

    // Windows Energy Meter (EMI) RAPL counters — CPU/iGPU watts without any driver or elevation.
    // Values arrive in milliwatts.
    private PerformanceCounter? _emiPkg, _emiCores, _emiGpu;
    private float _maxEmiSeen;

    public bool IsElevated { get; } =
        new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    public bool LhmInitialized { get; private set; }
    public string CpuName { get; private set; } = "";
    public string GpuName { get; private set; } = "";

    public SensorTier Tier
    {
        get
        {
            if (_maxPowerSeen > 0.5f) return SensorTier.Full;
            if (_maxEmiSeen > 0.5f && _updates >= 8) return SensorTier.EmiOnly;
            if (!LhmInitialized) return SensorTier.LhmFailed;
            if (_updates < 8) return SensorTier.Probing;
            return IsElevated ? SensorTier.DriverBlocked : SensorTier.NeedsAdmin;
        }
    }

    public void Init()
    {
        try
        {
            _computer = new Computer { IsCpuEnabled = true, IsGpuEnabled = true };
            _computer.Open();
            FindSensors();
            LhmInitialized = true;
            Log.Info($"LHM initialized (elevated={IsElevated}) cpu='{CpuName}' gpu='{GpuName}' " +
                     $"pkg={_cpuPackage != null} cores={_cpuCores != null} platform={_cpuPlatform != null} gpuPower={_gpuPower != null}");
        }
        catch (Exception ex)
        {
            LhmInitialized = false;
            Log.Error("LHM init failed", ex);
        }
        InitEmi();
    }

    private void InitEmi()
    {
        try
        {
            if (!PerformanceCounterCategory.Exists("Energy Meter")) return;
            foreach (var inst in new PerformanceCounterCategory("Energy Meter").GetInstanceNames())
            {
                if (inst.EndsWith("_pkg", StringComparison.OrdinalIgnoreCase))
                    _emiPkg = new PerformanceCounter("Energy Meter", "Power", inst, readOnly: true);
                else if (inst.EndsWith("_pp0", StringComparison.OrdinalIgnoreCase))
                    _emiCores = new PerformanceCounter("Energy Meter", "Power", inst, readOnly: true);
                else if (inst.EndsWith("_pp1", StringComparison.OrdinalIgnoreCase))
                    _emiGpu = new PerformanceCounter("Energy Meter", "Power", inst, readOnly: true);
            }
            _emiPkg?.NextValue(); // rate counters need a priming read
            _emiCores?.NextValue();
            _emiGpu?.NextValue();
            Log.Info($"EMI energy meter: pkg={_emiPkg != null} pp0={_emiCores != null} pp1={_emiGpu != null}");
        }
        catch (Exception ex)
        {
            Log.Error("EMI init", ex);
            _emiPkg = _emiCores = _emiGpu = null;
        }
    }

    /// <summary>Tear down and re-open LHM — used by the "re-detect" action after installing PawnIO.</summary>
    public void Reinit()
    {
        try { _computer?.Close(); } catch { }
        _computer = null;
        _cpuPackage = _cpuCores = _cpuPlatform = _cpuLoad = _cpuTemp = _cpuTempMax = _gpuPower = _gpuLoad = _gpuClock = null;
        _tjMaxDistances.Clear();
        _emiPkg?.Dispose();
        _emiCores?.Dispose();
        _emiGpu?.Dispose();
        _emiPkg = _emiCores = _emiGpu = null;
        _updates = 0;
        _maxPowerSeen = 0;
        _maxEmiSeen = 0;
        Init();
    }

    private void FindSensors()
    {
        if (_computer is null) return;
        foreach (var hw in _computer.Hardware)
        {
            hw.Update();
            if (hw.HardwareType == HardwareType.Cpu)
            {
                CpuName = hw.Name;
                foreach (var s in hw.Sensors)
                {
                    switch (s.SensorType)
                    {
                        case SensorType.Power:
                            // Intel: "CPU Package"/"CPU Cores"/"CPU Platform"; AMD: "Package"/"Cores"
                            if (s.Name is "CPU Package" or "Package") _cpuPackage = s;
                            else if (s.Name is "CPU Cores" or "Cores") _cpuCores = s;
                            else if (s.Name is "CPU Platform") _cpuPlatform = s;
                            break;
                        case SensorType.Load when s.Name == "CPU Total":
                            _cpuLoad = s;
                            break;
                        case SensorType.Temperature:
                            if (s.Name.EndsWith("Distance to TjMax", StringComparison.Ordinal)) _tjMaxDistances.Add(s);
                            else if (s.Name == "CPU Package") _cpuTemp = s;
                            else if (s.Name == "Core Average") _cpuTemp ??= s;
                            else if (s.Name == "Core Max") _cpuTempMax = s;
                            break;
                    }
                }
            }
            else if (hw.HardwareType is HardwareType.GpuIntel or HardwareType.GpuNvidia or HardwareType.GpuAmd)
            {
                // Prefer a discrete GPU if one ever appears; otherwise take the iGPU.
                if (_gpuPower is null || hw.HardwareType != HardwareType.GpuIntel)
                {
                    GpuName = hw.Name;
                    ISensor? power = null, load = null, clock = null;
                    foreach (var s in hw.Sensors)
                    {
                        switch (s.SensorType)
                        {
                            case SensorType.Power when power is null || s.Name.Contains("Package") || s.Name.Contains("Power"):
                                power = s;
                                break;
                            case SensorType.Load:
                                if (s.Name == "GPU Core") load = s;
                                else if (s.Name == "D3D 3D") load ??= s;
                                break;
                            case SensorType.Clock when s.Name.Contains("Core"):
                                clock = s;
                                break;
                        }
                    }
                    _gpuPower = power;
                    _gpuLoad = load;
                    _gpuClock = clock;
                }
            }
        }
    }

    public HardwareReading Read()
    {
        double? lhmPkg = null, lhmCores = null, lhmPlatform = null, lhmGpu = null;
        double? load = null, temp = null, tempMax = null, tjMaxDelta = null, gpuLoad = null, gpuClock = null;

        if (_computer is not null && LhmInitialized)
        {
            try
            {
                foreach (var hw in _computer.Hardware)
                    hw.Update();
            }
            catch (Exception ex)
            {
                if (_updates < 3) Log.Error("LHM update", ex);
            }

            lhmPkg = Val(_cpuPackage);
            lhmCores = Val(_cpuCores);
            lhmPlatform = Val(_cpuPlatform);
            lhmGpu = Val(_gpuPower);
            load = Val(_cpuLoad);
            temp = Val(_cpuTemp);
            tempMax = Val(_cpuTempMax);
            // Smallest headroom across cores = the hottest core, which is what throttles first.
            foreach (var s in _tjMaxDistances)
                if (Val(s) is double d && (tjMaxDelta is null || d < tjMaxDelta)) tjMaxDelta = d;
            gpuLoad = Val(_gpuLoad);
            gpuClock = Val(_gpuClock);

            if (lhmPkg is > 0.5) _maxPowerSeen = Math.Max(_maxPowerSeen, (float)lhmPkg.Value);
            if (lhmGpu is > 0.5) _maxPowerSeen = Math.Max(_maxPowerSeen, (float)lhmGpu.Value);
        }

        _updates++;

        // A flat 0 from an LHM RAPL sensor means "driver can't read MSRs", not "0 watts".
        var lhmLive = _maxPowerSeen > 0.5f;

        // EMI fallback: Windows' own RAPL counters (mW), no driver or elevation required
        var emiPkg = EmiVal(_emiPkg);
        var emiCores = EmiVal(_emiCores);
        var emiGpu = EmiVal(_emiGpu);
        if (emiPkg is > 0.5) _maxEmiSeen = Math.Max(_maxEmiSeen, (float)emiPkg.Value);

        return new HardwareReading(
            CpuPackageW: lhmLive ? lhmPkg : emiPkg,
            CpuCoresW: lhmLive ? lhmCores : emiCores,
            CpuPlatformW: lhmLive ? lhmPlatform : null,
            CpuLoadPct: load,
            CpuTempC: temp,
            CpuTempMaxC: tempMax,
            CpuTjMaxDeltaC: tjMaxDelta,
            IGpuW: lhmLive && lhmGpu is > 0.05 ? lhmGpu : emiGpu ?? (lhmLive ? lhmGpu : null),
            GpuLoadPct: gpuLoad,
            GpuClockMhz: gpuClock);
    }

    private static double? EmiVal(PerformanceCounter? c)
    {
        if (c is null) return null;
        try
        {
            var mw = c.NextValue();
            return mw is > 0 and < 500_000 ? mw / 1000.0 : null;
        }
        catch { return null; }
    }

    private static double? Val(ISensor? s) => s?.Value is float f && !float.IsNaN(f) ? f : null;

    public void Dispose()
    {
        try { _computer?.Close(); } catch { }
        _computer = null;
        _emiPkg?.Dispose();
        _emiCores?.Dispose();
        _emiGpu?.Dispose();
        _emiPkg = _emiCores = _emiGpu = null;
    }
}
