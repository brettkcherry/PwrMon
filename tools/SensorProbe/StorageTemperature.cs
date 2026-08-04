using System.Runtime.InteropServices;

namespace SensorProbe;

/// <summary>
/// Reads drive temperature through <c>IOCTL_STORAGE_QUERY_PROPERTY</c> on a *volume* handle.
///
/// This is the interesting path: LibreHardwareMonitor reads storage sensors by opening
/// <c>\\.\PhysicalDriveN</c>, which requires administrator. A volume handle opened with
/// zero access rights does not — Windows answers the temperature query on a query-only
/// handle. If this works, drive temps are available in PwrMon's default (unelevated,
/// no-driver) tier; if it doesn't, temps stay a full-tier feature like CPU package power.
/// </summary>
internal static class StorageTemperature
{
    private const uint IoctlStorageQueryProperty = 0x2D1400;
    private const int StorageAdapterTemperatureProperty = 51;
    private const int StorageDeviceTemperatureProperty = 52;
    private const uint OpenExisting = 3;
    private const uint FileShareReadWrite = 0x00000003;

    [StructLayout(LayoutKind.Sequential)]
    private struct StoragePropertyQuery
    {
        public int PropertyId;
        public int QueryType; // PropertyStandardQuery = 0
        public byte AdditionalParameters;
    }

    // STORAGE_TEMPERATURE_INFO — Temperature is plain degrees Celsius.
    [StructLayout(LayoutKind.Sequential)]
    private struct StorageTemperatureInfo
    {
        public ushort Index;
        public short Temperature;
        public short OverThreshold;
        public short UnderThreshold;
        [MarshalAs(UnmanagedType.U1)] public bool OverThresholdChanged;
        [MarshalAs(UnmanagedType.U1)] public bool UnderThresholdChanged;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandleWrapper CreateFileW(
        string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes,
        uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(
        SafeFileHandleWrapper hDevice, uint dwIoControlCode,
        ref StoragePropertyQuery lpInBuffer, int nInBufferSize,
        IntPtr lpOutBuffer, int nOutBufferSize, out int lpBytesReturned, IntPtr lpOverlapped);

    private sealed class SafeFileHandleWrapper : Microsoft.Win32.SafeHandles.SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafeFileHandleWrapper() : base(true) { }
        protected override bool ReleaseHandle() => CloseHandle(handle);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr h);
    }

    public static void Dump()
    {
        Console.WriteLine("\n--- drive temperature via IOCTL_STORAGE_QUERY_PROPERTY (volume handles) ---");

        foreach (var drive in DriveInfo.GetDrives())
        {
            if (drive.DriveType != DriveType.Fixed) continue;
            var letter = drive.Name.TrimEnd('\\');
            Report($@"\\.\{letter}", "device", StorageDeviceTemperatureProperty);
            Report($@"\\.\{letter}", "adapter", StorageAdapterTemperatureProperty);
        }

        // For comparison: the physical-drive path LHM uses, which is expected to need admin.
        for (var i = 0; i < 4; i++)
            Report($@"\\.\PhysicalDrive{i}", "device", StorageDeviceTemperatureProperty, quietIfMissing: true);
    }

    private static void Report(string path, string label, int propertyId, bool quietIfMissing = false)
    {
        // Zero desired access = query-only handle; this is what avoids needing administrator.
        using var h = CreateFileW(path, 0, FileShareReadWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
        if (h.IsInvalid)
        {
            var err = Marshal.GetLastWin32Error();
            if (quietIfMissing && err == 2) return; // no such drive
            Console.WriteLine($"{path} [{label}]: open failed, Win32 error {err}" +
                              (err == 5 ? " (access denied — needs elevation)" : ""));
            return;
        }

        var query = new StoragePropertyQuery { PropertyId = propertyId, QueryType = 0 };
        const int bufSize = 512;
        var buf = Marshal.AllocHGlobal(bufSize);
        try
        {
            if (!DeviceIoControl(h, IoctlStorageQueryProperty, ref query, Marshal.SizeOf<StoragePropertyQuery>(),
                                 buf, bufSize, out var returned, IntPtr.Zero))
            {
                Console.WriteLine($"{path} [{label}]: ioctl failed, Win32 error {Marshal.GetLastWin32Error()}");
                return;
            }

            // STORAGE_TEMPERATURE_DATA_DESCRIPTOR header is 24 bytes before the info array.
            var critical = Marshal.ReadInt16(buf, 8);
            var warning = Marshal.ReadInt16(buf, 10);
            var infoCount = (ushort)Marshal.ReadInt16(buf, 12);
            Console.WriteLine($"{path} [{label}]: {returned} bytes, critical={critical} °C warning={warning} °C infoCount={infoCount}");

            var infoSize = Marshal.SizeOf<StorageTemperatureInfo>();
            for (var i = 0; i < infoCount && 24 + (i + 1) * infoSize <= returned; i++)
            {
                var info = Marshal.PtrToStructure<StorageTemperatureInfo>(buf + 24 + i * infoSize);
                Console.WriteLine($"    sensor #{info.Index}: {info.Temperature} °C " +
                                  $"(over {info.OverThreshold}, under {info.UnderThreshold})");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
    }
}
