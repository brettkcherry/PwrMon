using System.Security.Principal;
using LibreHardwareMonitor.Hardware;
using PowerMonitor.Models;

namespace PowerMonitor.Services;

public sealed record HardwareReading(
    double? CpuPackageW,
    double? CpuCoresW,
    double? CpuPlatformW,
    double? CpuLoadPct,
    double? CpuTempC,
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
    private ISensor? _cpuPackage, _cpuCores, _cpuPlatform, _cpuLoad, _cpuTemp;
    private ISensor? _gpuPower, _gpuLoad, _gpuClock;
    private int _updates;
    private float _maxPowerSeen;

    public bool IsElevated { get; } =
        new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    public bool LhmInitialized { get; private set; }
    public string CpuName { get; private set; } = "";
    public string GpuName { get; private set; } = "";

    public SensorTier Tier
    {
        get
        {
            if (!LhmInitialized) return SensorTier.LhmFailed;
            if (_maxPowerSeen > 0.5f) return SensorTier.Full;
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
    }

    /// <summary>Tear down and re-open LHM — used by the "re-detect" action after installing PawnIO.</summary>
    public void Reinit()
    {
        try { _computer?.Close(); } catch { }
        _computer = null;
        _cpuPackage = _cpuCores = _cpuPlatform = _cpuLoad = _cpuTemp = _gpuPower = _gpuLoad = _gpuClock = null;
        _updates = 0;
        _maxPowerSeen = 0;
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
                            if (s.Name == "CPU Package") _cpuTemp = s;
                            else if (s.Name == "Core Average") _cpuTemp ??= s;
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
        if (_computer is null || !LhmInitialized)
            return new HardwareReading(null, null, null, null, null, null, null, null);

        try
        {
            foreach (var hw in _computer.Hardware)
                hw.Update();
        }
        catch (Exception ex)
        {
            if (_updates < 3) Log.Error("LHM update", ex);
        }

        _updates++;

        var pkg = Val(_cpuPackage);
        var gpu = Val(_gpuPower);
        if (pkg is > 0.5) _maxPowerSeen = Math.Max(_maxPowerSeen, (float)pkg.Value);
        if (gpu is > 0.5) _maxPowerSeen = Math.Max(_maxPowerSeen, (float)gpu.Value);

        // A flat 0 from a RAPL sensor means "driver can't read MSRs", not "0 watts".
        var live = _maxPowerSeen > 0.5f;

        return new HardwareReading(
            CpuPackageW: live ? pkg : null,
            CpuCoresW: live ? Val(_cpuCores) : null,
            CpuPlatformW: live ? Val(_cpuPlatform) : null,
            CpuLoadPct: Val(_cpuLoad),
            CpuTempC: Val(_cpuTemp),
            IGpuW: live ? gpu : null,
            GpuLoadPct: Val(_gpuLoad),
            GpuClockMhz: Val(_gpuClock));
    }

    private static double? Val(ISensor? s) => s?.Value is float f && !float.IsNaN(f) ? f : null;

    public void Dispose()
    {
        try { _computer?.Close(); } catch { }
        _computer = null;
    }
}
