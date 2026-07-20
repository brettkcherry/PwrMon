// SensorProbe — dumps every power-related data source PwrMon will use,
// so the app can be built against what this machine actually reports.
using System.Management;
using System.Security.Principal;
using LibreHardwareMonitor.Hardware;

var elevated = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
Console.WriteLine($"=== SensorProbe ===  elevated={elevated}  time={DateTime.Now:O}");

Console.WriteLine("\n--- root\\wmi battery classes ---");
DumpWmi(@"root\wmi", "BatteryStaticData");
DumpWmi(@"root\wmi", "BatteryFullChargedCapacity");
DumpWmi(@"root\wmi", "BatteryStatus");
DumpWmi(@"root\wmi", "BatteryCycleCount");
DumpWmi(@"root\wmi", "BatteryRuntime");
DumpWmi(@"root\wmi", "BatteryTemperature");

Console.WriteLine("\n--- root\\cimv2 Win32_Battery ---");
DumpWmi(@"root\cimv2", "Win32_Battery");

Console.WriteLine("\n--- LibreHardwareMonitor sensors ---");
var computer = new Computer
{
    IsCpuEnabled = true,
    IsGpuEnabled = true,
    IsBatteryEnabled = true,
};
try
{
    computer.Open();
    // two update passes so rate-based sensors settle
    foreach (var hw in computer.Hardware) hw.Update();
    Thread.Sleep(1200);
    foreach (var hw in computer.Hardware)
    {
        hw.Update();
        Console.WriteLine($"[{hw.HardwareType}] {hw.Name}");
        foreach (var s in hw.Sensors.OrderBy(s => s.SensorType.ToString()).ThenBy(s => s.Name))
            Console.WriteLine($"    {s.SensorType,-12} {s.Name,-28} value={FormatVal(s.Value)}  min={FormatVal(s.Min)} max={FormatVal(s.Max)}");
        foreach (var sub in hw.SubHardware)
        {
            sub.Update();
            Console.WriteLine($"  [{sub.HardwareType}] {sub.Name}");
            foreach (var s in sub.Sensors.OrderBy(s => s.SensorType.ToString()).ThenBy(s => s.Name))
                Console.WriteLine($"      {s.SensorType,-12} {s.Name,-28} value={FormatVal(s.Value)}");
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

Console.WriteLine("\n=== done ===");
return;

static string FormatVal(float? v) => v.HasValue ? v.Value.ToString("0.###") : "null";

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
