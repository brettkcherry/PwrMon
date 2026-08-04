using System.Runtime.InteropServices;

namespace SensorProbe;

/// <summary>
/// Reads GPU temperature from the display kernel via <c>D3DKMTQueryAdapterInfo</c> —
/// the same interface Task Manager uses. Unelevated, no vendor library, no Level Zero.
///
/// Third route tried for Intel iGPU temperature. LibreHardwareMonitor exposes no Intel GPU
/// temperature sensor; Level Zero Sysman can't initialise (no Khronos driver registration);
/// and IGCL's <c>ctlEnumTemperatureSensors</c> returns CTL_RESULT_ERROR_ZE_LOADER because it
/// is itself implemented over Level Zero.
/// </summary>
internal static class D3dkmtTemperature
{
    private const uint KmtqaitypeAdapterPerfdata = 62;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OpenAdapterFromGdiDisplayName
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        public uint hAdapter;
        public uint AdapterLuidLow;
        public int AdapterLuidHigh;
        public uint VidPnSourceId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct QueryAdapterInfo
    {
        public uint hAdapter;
        public uint Type;
        public IntPtr pPrivateDriverData;
        public uint PrivateDriverDataSize;
    }

    // Temperature is in deci-Celsius (1 = 0.1 °C); Power is in tenths of a percent.
    [StructLayout(LayoutKind.Sequential)]
    private struct AdapterPerfData
    {
        public uint PhysicalAdapterIndex;
        public ulong MemoryFrequency;
        public ulong MaxMemoryFrequency;
        public ulong MaxMemoryFrequencyOc;
        public ulong MemoryBandwidth;
        public ulong PcieBandwidth;
        public uint FanRpm;
        public uint Power;
        public uint Temperature;
        public byte PowerStateOverride;
    }

    [DllImport("gdi32.dll")] private static extern int D3DKMTOpenAdapterFromGdiDisplayName(ref OpenAdapterFromGdiDisplayName p);
    [DllImport("gdi32.dll")] private static extern int D3DKMTQueryAdapterInfo(ref QueryAdapterInfo p);
    [DllImport("gdi32.dll")] private static extern int D3DKMTCloseAdapter(ref uint hAdapter);

    public static void Dump()
    {
        Console.WriteLine("\n--- GPU temperature via D3DKMT adapter perf data (Task Manager's route) ---");
        for (var display = 1; display <= 2; display++)
        {
            var name = $@"\\.\DISPLAY{display}";
            var open = new OpenAdapterFromGdiDisplayName { DeviceName = name };
            var status = D3DKMTOpenAdapterFromGdiDisplayName(ref open);
            if (status != 0)
            {
                if (display == 1) Console.WriteLine($"{name}: open failed, NTSTATUS 0x{status:X8}");
                continue;
            }

            try
            {
                var size = Marshal.SizeOf<AdapterPerfData>();
                var buf = Marshal.AllocHGlobal(size);
                try
                {
                    Marshal.StructureToPtr(new AdapterPerfData { PhysicalAdapterIndex = 0 }, buf, false);
                    var q = new QueryAdapterInfo
                    {
                        hAdapter = open.hAdapter,
                        Type = KmtqaitypeAdapterPerfdata,
                        pPrivateDriverData = buf,
                        PrivateDriverDataSize = (uint)size,
                    };
                    status = D3DKMTQueryAdapterInfo(ref q);
                    if (status != 0)
                    {
                        Console.WriteLine($"{name}: QueryAdapterInfo(ADAPTERPERFDATA, {size} bytes) -> NTSTATUS 0x{status:X8}");
                        continue;
                    }

                    var d = Marshal.PtrToStructure<AdapterPerfData>(buf);
                    Console.WriteLine($"{name}: temperature={d.Temperature / 10.0:0.0} °C  fan={d.FanRpm} RPM  " +
                                      $"power={d.Power / 10.0:0.0} %  memClock={d.MemoryFrequency / 1_000_000.0:0} MHz");
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
            finally
            {
                var h = open.hAdapter;
                D3DKMTCloseAdapter(ref h);
            }
        }
    }
}
