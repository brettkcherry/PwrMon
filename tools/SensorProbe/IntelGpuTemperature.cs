using System.Runtime.InteropServices;

namespace SensorProbe;

/// <summary>
/// Reads Intel iGPU temperature through Level Zero Sysman (<c>ze_loader.dll</c>, shipped by
/// the Intel graphics driver).
///
/// LibreHardwareMonitor's Intel GPU support exposes Clock/Load/Power/Voltage but no
/// temperature sensor at all, while HWiNFO reports an iGPU core temperature on the same
/// machine — so the sensor exists and the gap is LHM's. Intel offers two user-mode routes:
/// IGCL (<c>ControlLib.dll</c>, <c>ctlPowerTelemetryGet</c> — also carries throttle reasons)
/// and Level Zero Sysman. Sysman is tried here because reading a temperature needs only a
/// handful of calls and no version-tagged telemetry struct.
/// </summary>
internal static class IntelGpuTemperature
{
    private const string Lib = "ze_loader.dll";

    // Newer loaders expose the zes* entry points directly; older ones require zeInit plus
    // ZES_ENABLE_SYSMAN=1 and reuse the core driver/device handles.
    [DllImport(Lib)] private static extern uint zesInit(uint flags);
    [DllImport(Lib)] private static extern uint zesDriverGet(ref uint pCount, [Out] IntPtr[]? phDrivers);
    [DllImport(Lib)] private static extern uint zesDeviceGet(IntPtr hDriver, ref uint pCount, [Out] IntPtr[]? phDevices);
    [DllImport(Lib)] private static extern uint zeInit(uint flags);
    [DllImport(Lib)] private static extern uint zeDriverGet(ref uint pCount, [Out] IntPtr[]? phDrivers);
    [DllImport(Lib)] private static extern uint zeDeviceGet(IntPtr hDriver, ref uint pCount, [Out] IntPtr[]? phDevices);

    [DllImport(Lib)] private static extern uint zesDeviceEnumTemperatureSensors(IntPtr hDevice, ref uint pCount, [Out] IntPtr[]? phTemperature);
    [DllImport(Lib)] private static extern uint zesTemperatureGetState(IntPtr hTemperature, out double pTemperature);

    public static void Dump()
    {
        Console.WriteLine("\n--- Intel iGPU temperature via Level Zero Sysman ---");
        try
        {
            DumpCore();
        }
        catch (DllNotFoundException)
        {
            Console.WriteLine($"{Lib} not present — no Intel graphics driver, or a build without Level Zero.");
        }
        catch (EntryPointNotFoundException ex)
        {
            Console.WriteLine($"entry point missing: {ex.Message}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void DumpCore()
    {
        // Legacy sysman activation; harmless on loaders that don't need it.
        Environment.SetEnvironmentVariable("ZES_ENABLE_SYSMAN", "1");

        var useZes = true;
        uint rc;
        try
        {
            rc = zesInit(0);
            Console.WriteLine($"zesInit -> 0x{rc:X}");
            if (rc != 0) { useZes = false; }
        }
        catch (EntryPointNotFoundException)
        {
            useZes = false;
            Console.WriteLine("zesInit absent — falling back to zeInit + ZES_ENABLE_SYSMAN");
        }

        if (!useZes)
        {
            rc = zeInit(0);
            Console.WriteLine($"zeInit -> 0x{rc:X}");
            if (rc != 0) { Console.WriteLine("init failed; giving up"); return; }
        }

        uint driverCount = 0;
        rc = useZes ? zesDriverGet(ref driverCount, null) : zeDriverGet(ref driverCount, null);
        if (rc != 0 || driverCount == 0) { Console.WriteLine($"driverGet -> 0x{rc:X}, count={driverCount}"); return; }

        var drivers = new IntPtr[driverCount];
        rc = useZes ? zesDriverGet(ref driverCount, drivers) : zeDriverGet(ref driverCount, drivers);
        if (rc != 0) { Console.WriteLine($"driverGet(handles) -> 0x{rc:X}"); return; }
        Console.WriteLine($"drivers: {driverCount}");

        foreach (var driver in drivers)
        {
            uint deviceCount = 0;
            rc = useZes ? zesDeviceGet(driver, ref deviceCount, null) : zeDeviceGet(driver, ref deviceCount, null);
            if (rc != 0 || deviceCount == 0) continue;

            var devices = new IntPtr[deviceCount];
            rc = useZes ? zesDeviceGet(driver, ref deviceCount, devices) : zeDeviceGet(driver, ref deviceCount, devices);
            if (rc != 0) continue;

            for (var d = 0; d < devices.Length; d++)
            {
                uint sensorCount = 0;
                rc = zesDeviceEnumTemperatureSensors(devices[d], ref sensorCount, null);
                Console.WriteLine($"device #{d}: enumTemperatureSensors -> 0x{rc:X}, count={sensorCount}");
                if (rc != 0 || sensorCount == 0) continue;

                var sensors = new IntPtr[sensorCount];
                rc = zesDeviceEnumTemperatureSensors(devices[d], ref sensorCount, sensors);
                if (rc != 0) continue;

                for (var i = 0; i < sensors.Length; i++)
                {
                    var state = zesTemperatureGetState(sensors[i], out var celsius);
                    Console.WriteLine(state == 0
                        ? $"    sensor #{i}: {celsius:0.#} °C"
                        : $"    sensor #{i}: getState -> 0x{state:X}");
                }
            }
        }
    }
}
