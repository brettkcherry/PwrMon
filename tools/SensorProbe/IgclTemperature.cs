using System.Runtime.InteropServices;

namespace SensorProbe;

/// <summary>
/// Reads Intel GPU temperature through IGCL (<c>ControlLib.dll</c>, installed to System32 by
/// the Intel graphics driver). This is the route HWiNFO uses: LibreHardwareMonitor's Intel GPU
/// support exposes no temperature sensor at all, and Level Zero Sysman can't initialise on a
/// stock driver install (no <c>HKLM\SOFTWARE\Khronos\OneAPI\LevelZero</c> registration) — see
/// <see cref="IntelGpuTemperature"/> for that dead end.
///
/// Only <c>ctl_init_args_t</c> needs hand-laid-out interop. Per Intel's header,
/// <c>ctl_version_info_t</c> is a packed uint32 (major &lt;&lt; 16 | minor), giving
/// Size@0, Version@4, AppVersion@8, flags@12, SupportedVersion@16, ApplicationUID@20 = 36 bytes.
/// </summary>
internal static class IgclTemperature
{
    private const string Lib = "ControlLib.dll";
    private const uint CtlResultSuccess = 0;
    private const uint CtlResultErrorUnsupportedVersion = 0x40000009;
    private const int InitArgsSize = 36;
    private const int TargetMajor = 1, TargetMinor = 2;

    [DllImport(Lib)] private static extern uint ctlInit(IntPtr pInitDesc, out IntPtr phAPIHandle);
    [DllImport(Lib)] private static extern uint ctlClose(IntPtr hAPIHandle);
    [DllImport(Lib)] private static extern uint ctlEnumerateDevices(IntPtr hAPIHandle, ref uint pCount, [Out] IntPtr[]? phDevices);
    [DllImport(Lib)] private static extern uint ctlEnumTemperatureSensors(IntPtr hDAhandle, ref uint pCount, [Out] IntPtr[]? phTemperature);
    [DllImport(Lib)] private static extern uint ctlTemperatureGetState(IntPtr hTemperature, out double pTemperature);

    public static void Dump()
    {
        Console.WriteLine("\n--- Intel GPU temperature via IGCL (ControlLib.dll) ---");
        try
        {
            DumpCore();
        }
        catch (DllNotFoundException)
        {
            Console.WriteLine($"{Lib} not present — no Intel graphics driver installed.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// One <c>ctlInit</c> attempt and nothing else, so a version sweep can use a fresh process
    /// per combination. Kept because it's how the two constraints below were found.
    /// </summary>
    public static void SingleInit(byte structVer, int major, int minor)
    {
        var buf = Marshal.AllocHGlobal(InitArgsSize);
        try
        {
            Zero(buf);
            Marshal.WriteInt32(buf, 0, InitArgsSize);
            Marshal.WriteByte(buf, 4, structVer);
            Marshal.WriteInt32(buf, 8, (major << 16) | minor);
            var rc = ctlInit(buf, out var api);
            Console.WriteLine($"ver={structVer} app={major}.{minor} -> 0x{rc:X8} " +
                              $"supported=0x{Marshal.ReadInt32(buf, 16):X8}" +
                              (rc == CtlResultSuccess ? "  <<< SUCCESS" : ""));
            if (rc == CtlResultSuccess) ctlClose(api);
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    private static void DumpCore()
    {
        var api = TryInit();
        if (api == IntPtr.Zero) return;

        try
        {
            uint count = 0;
            var rc = ctlEnumerateDevices(api, ref count, null);
            Console.WriteLine($"ctlEnumerateDevices -> 0x{rc:X8}, adapters={count}");
            if (rc != CtlResultSuccess || count == 0) return;

            var adapters = new IntPtr[count];
            rc = ctlEnumerateDevices(api, ref count, adapters);
            if (rc != CtlResultSuccess) { Console.WriteLine($"ctlEnumerateDevices(handles) -> 0x{rc:X8}"); return; }

            for (var a = 0; a < adapters.Length; a++)
            {
                uint sensorCount = 0;
                rc = ctlEnumTemperatureSensors(adapters[a], ref sensorCount, null);
                Console.WriteLine($"adapter #{a}: ctlEnumTemperatureSensors -> 0x{rc:X8}, sensors={sensorCount}");
                if (rc != CtlResultSuccess || sensorCount == 0) continue;

                var sensors = new IntPtr[sensorCount];
                rc = ctlEnumTemperatureSensors(adapters[a], ref sensorCount, sensors);
                if (rc != CtlResultSuccess) continue;

                for (var i = 0; i < sensors.Length; i++)
                {
                    var state = ctlTemperatureGetState(sensors[i], out var celsius);
                    Console.WriteLine(state == CtlResultSuccess
                        ? $"    sensor #{i}: {celsius:0.#} °C"
                        : $"    sensor #{i}: ctlTemperatureGetState -> 0x{state:X8}");
                }
            }
        }
        finally
        {
            ctlClose(api);
        }
    }

    /// <summary>
    /// Initialises IGCL, negotiating down if the installed driver is older than we target.
    /// Two constraints found by sweeping: the struct <c>Version</c> byte must be 0 (any other
    /// value fails *and* leaves the library unable to report SupportedVersion on subsequent
    /// calls in the same process), and AppVersion's major must match the driver's.
    /// </summary>
    private static IntPtr TryInit()
    {
        var api = Attempt(TargetMajor, TargetMinor, out var rc, out var supported);
        if (rc == CtlResultSuccess) return api;

        if (rc == CtlResultErrorUnsupportedVersion && supported != 0)
        {
            int major = (supported >> 16) & 0xFFFF, minor = supported & 0xFFFF;
            Console.WriteLine($"ctlInit: driver supports {major}.{minor}, retrying at that version");
            api = Attempt(major, minor, out rc, out _);
            if (rc == CtlResultSuccess) return api;
        }

        Console.WriteLine($"ctlInit -> 0x{rc:X8} (supported=0x{supported:X8})");
        return IntPtr.Zero;
    }

    private static IntPtr Attempt(int major, int minor, out uint rc, out int supported)
    {
        var buf = Marshal.AllocHGlobal(InitArgsSize);
        try
        {
            Zero(buf);
            Marshal.WriteInt32(buf, 0, InitArgsSize);           // Size
            Marshal.WriteByte(buf, 4, 0);                       // Version — must be 0
            Marshal.WriteInt32(buf, 8, (major << 16) | minor);  // AppVersion, packed
            Marshal.WriteInt32(buf, 12, 0);                     // flags
            rc = ctlInit(buf, out var api);
            supported = Marshal.ReadInt32(buf, 16);
            return rc == CtlResultSuccess ? api : IntPtr.Zero;
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    private static void Zero(IntPtr buf)
    {
        for (var i = 0; i < InitArgsSize; i++) Marshal.WriteByte(buf, i, 0);
    }
}
