// SensorProbe — dumps every power-related data source PwrMon will use,
// so the app can be built against what this machine actually reports.
//
// Pass --motherboard to additionally enable LHM's motherboard/LPC (super-I/O) probing.
// It's off by default deliberately: the app never enables it (see SECURITY.md), and on a
// PawnIO system it loads the LPC module. Use it only to find out whether an unfamiliar
// board exposes anything, and say so when attaching the dump.
using System.Diagnostics;
using System.Management;
using System.Security.Principal;
using LibreHardwareMonitor.Hardware;

// --igcl-init <structVersion> <major> <minor>: one IGCL init attempt, then exit. Used to
// sweep version combinations with a fresh process each time.
if (args.Length == 4 && args[0].Equals("--igcl-init", StringComparison.OrdinalIgnoreCase))
{
    SensorProbe.IgclTemperature.SingleInit(byte.Parse(args[1]), int.Parse(args[2]), int.Parse(args[3]));
    return;
}

var probeMotherboard = args.Contains("--motherboard", StringComparer.OrdinalIgnoreCase);
var elevated = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
Console.WriteLine($"=== SensorProbe ===  elevated={elevated}  motherboard={probeMotherboard}  time={DateTime.Now:O}");

// Every Temperature sensor found anywhere, collected as we go and reprinted at the end —
// the per-hardware dump is long and temps are easy to lose in it.
var temps = new List<(string Source, string Name, string Value)>();

Console.WriteLine("\n--- root\\wmi battery classes ---");
DumpWmi(@"root\wmi", "BatteryStaticData");
DumpWmi(@"root\wmi", "BatteryFullChargedCapacity");
DumpWmi(@"root\wmi", "BatteryStatus");
DumpWmi(@"root\wmi", "BatteryCycleCount");
DumpWmi(@"root\wmi", "BatteryRuntime");
DumpWmi(@"root\wmi", "BatteryTemperature");

Console.WriteLine("\n--- root\\cimv2 Win32_Battery ---");
DumpWmi(@"root\cimv2", "Win32_Battery");

DumpThermalZones();

Console.WriteLine("\n--- root\\cimv2 Win32_TemperatureProbe ---");
DumpWmi(@"root\cimv2", "Win32_TemperatureProbe");

SensorProbe.StorageTemperature.Dump();
SensorProbe.IntelGpuTemperature.Dump();
SensorProbe.IgclTemperature.Dump();
SensorProbe.D3dkmtTemperature.Dump();

Console.WriteLine("\n--- LibreHardwareMonitor sensors ---");
// Storage is deliberately NOT enabled here. Elevated, LHM's storage detection opens
// \\.\PhysicalDriveN and issues SMART commands, which hung indefinitely on the reference
// machine (unelevated it finds no drives, so it never got that far). It's probed separately
// below, behind a watchdog, so it can never take the rest of the dump down with it.
var computer = new Computer
{
    IsCpuEnabled = true,
    IsGpuEnabled = true,
    IsBatteryEnabled = true,
    IsMotherboardEnabled = probeMotherboard,
};
try
{
    computer.Open();
    // two update passes so rate-based sensors settle
    foreach (var hw in computer.Hardware) hw.Update();
    Thread.Sleep(1200);
    foreach (var hw in computer.Hardware)
    {
        // Per-hardware update cost matters: storage polling is far slower than RAPL reads
        // and the app updates everything on its sampler thread.
        var sw = Stopwatch.StartNew();
        hw.Update();
        sw.Stop();
        Console.WriteLine($"[{hw.HardwareType}] {hw.Name}   (update {sw.Elapsed.TotalMilliseconds:0.0} ms)");
        foreach (var s in hw.Sensors.OrderBy(s => s.SensorType.ToString()).ThenBy(s => s.Name))
        {
            Console.WriteLine($"    {s.SensorType,-12} {s.Name,-28} value={FormatVal(s.Value)}  min={FormatVal(s.Min)} max={FormatVal(s.Max)}");
            if (s.SensorType == SensorType.Temperature)
                temps.Add(($"{hw.HardwareType}/{hw.Name}", s.Name, $"{FormatVal(s.Value)} °C (max {FormatVal(s.Max)})"));
        }
        foreach (var sub in hw.SubHardware)
        {
            sub.Update();
            Console.WriteLine($"  [{sub.HardwareType}] {sub.Name}");
            foreach (var s in sub.Sensors.OrderBy(s => s.SensorType.ToString()).ThenBy(s => s.Name))
            {
                Console.WriteLine($"      {s.SensorType,-12} {s.Name,-28} value={FormatVal(s.Value)}");
                if (s.SensorType == SensorType.Temperature)
                    temps.Add(($"{sub.HardwareType}/{sub.Name}", s.Name, $"{FormatVal(s.Value)} °C"));
            }
        }
    }
}
catch (Exception ex)
{
    Console.WriteLine($"LHM failed: {ex.GetType().Name}: {ex.Message}");
}
finally
{
    computer.Close();
}

ProbeLhmStorage();

Console.WriteLine("\n--- temperature summary ---");
if (temps.Count == 0)
{
    Console.WriteLine("No temperature sensors reported a value.");
}
else
{
    foreach (var group in temps.GroupBy(t => t.Source))
    {
        Console.WriteLine(group.Key);
        foreach (var t in group)
            Console.WriteLine($"    {t.Name,-32} {t.Value}");
    }
}
Console.WriteLine(elevated
    ? "(null values here mean the sensor exists but the driver couldn't read it — check PawnIO)"
    : "(CPU temps read null unelevated by design; storage and ACPI zones should still have values)");

Console.WriteLine("\n=== done ===");
return;

static string FormatVal(float? v) => v.HasValue ? v.Value.ToString("0.###") : "null";

/// Tries LHM's own storage sensors on a background thread with a watchdog. Elevated, this
/// can block forever inside the SMART path; PwrMon reads drive temperature through the
/// storage IOCTL above instead, so a hang here is a finding, not a failure.
static void ProbeLhmStorage()
{
    Console.WriteLine("\n--- LHM storage sensors (watchdogged) ---");
    var output = new List<string>();
    var worker = new Thread(() =>
    {
        var storage = new Computer { IsStorageEnabled = true };
        try
        {
            storage.Open();
            foreach (var hw in storage.Hardware)
            {
                hw.Update();
                output.Add($"[{hw.HardwareType}] {hw.Name}");
                foreach (var s in hw.Sensors.OrderBy(s => s.SensorType.ToString()).ThenBy(s => s.Name))
                    output.Add($"    {s.SensorType,-12} {s.Name,-28} value={FormatVal(s.Value)}");
            }
            if (storage.Hardware.Count == 0) output.Add("no storage hardware detected");
        }
        catch (Exception ex) { output.Add($"failed: {ex.GetType().Name}: {ex.Message}"); }
        finally { try { storage.Close(); } catch { } }
    }) { IsBackground = true };

    worker.Start();
    if (worker.Join(TimeSpan.FromSeconds(20)))
        foreach (var line in output) Console.WriteLine(line);
    else
        Console.WriteLine("TIMED OUT after 20 s — LHM's storage path blocked. This is why PwrMon " +
                          "reads drive temperature via IOCTL_STORAGE_QUERY_PROPERTY instead.");
}

/// ACPI thermal zones — chassis/skin temps that need neither elevation nor a driver.
/// CurrentTemperature is in tenths of a Kelvin. Many laptops expose zones that never move;
/// the point of dumping them is to find out whether this machine's are real.
static void DumpThermalZones()
{
    Console.WriteLine("\n--- ACPI thermal zones (root\\wmi MSAcpi_ThermalZoneTemperature) ---");
    try
    {
        using var searcher = new ManagementObjectSearcher(@"root\wmi", "SELECT * FROM MSAcpi_ThermalZoneTemperature");
        var found = false;
        foreach (var obj in searcher.Get())
        {
            found = true;
            var name = obj["InstanceName"]?.ToString() ?? "?";
            Console.WriteLine($"{name}");
            foreach (var p in obj.Properties.Cast<PropertyData>().OrderBy(p => p.Name))
            {
                var decoded = p.Name.EndsWith("Temperature", StringComparison.Ordinal) && p.Value is uint dk and > 0
                    ? $"  ({dk / 10.0 - 273.15:0.0} °C)"
                    : "";
                Console.WriteLine($"    {p.Name} = {p.Value ?? "null"}{decoded}");
            }
        }
        if (!found) Console.WriteLine("MSAcpi_ThermalZoneTemperature: <no instances>");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"MSAcpi_ThermalZoneTemperature: ERROR {ex.GetType().Name}: {ex.Message}");
    }
}

static void DumpWmi(string ns, string cls)
{
    try
    {
        using var searcher = new ManagementObjectSearcher(ns, $"SELECT * FROM {cls}");
        var found = false;
        foreach (var obj in searcher.Get())
        {
            found = true;
            Console.WriteLine($"{cls}:");
            foreach (var p in obj.Properties.Cast<PropertyData>().OrderBy(p => p.Name))
                Console.WriteLine($"    {p.Name} = {p.Value ?? "null"}");
        }
        if (!found) Console.WriteLine($"{cls}: <no instances>");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"{cls}: ERROR {ex.GetType().Name}: {ex.Message}");
    }
}
