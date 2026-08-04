using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PwrMon.Services;

/// <summary>
/// Reads drive temperature with <c>IOCTL_STORAGE_QUERY_PROPERTY</c> against a *volume* handle
/// opened with zero access rights.
///
/// This is the one temperature PwrMon can show in its default tier. LibreHardwareMonitor's
/// storage sensors open <c>\\.\PhysicalDriveN</c>, which needs administrator — and elevated,
/// its SMART path hung indefinitely on the reference machine. A query-only volume handle needs
/// no elevation, returns the same value as the physical-drive path, and is a single IOCTL.
/// </summary>
public sealed class DriveTemperatureReader
{
    private const uint IoctlStorageQueryProperty = 0x2D1400;
    private const int StorageDeviceTemperatureProperty = 52;
    private const uint OpenExisting = 3;
    private const uint FileShareReadWrite = 0x00000003;

    // The drive is far slower to change than watts and each read is a device round-trip,
    // so it runs on its own cadence rather than every sampler tick.
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(10);

    private readonly List<string> _volumes = new();
    private bool _enumerated;
    private DateTime _lastRead = DateTime.MinValue;
    private double? _cached;

    /// <summary>Volume the cached reading came from, e.g. "C:". Null until a read succeeds.</summary>
    public string? HottestVolume { get; private set; }

    [StructLayout(LayoutKind.Sequential)]
    private struct StoragePropertyQuery
    {
        public int PropertyId;
        public int QueryType;
        public byte AdditionalParameters;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFileW(
        string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes,
        uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(
        SafeFileHandle hDevice, uint dwIoControlCode,
        ref StoragePropertyQuery lpInBuffer, int nInBufferSize,
        IntPtr lpOutBuffer, int nOutBufferSize, out int lpBytesReturned, IntPtr lpOverlapped);

    /// <summary>
    /// Hottest fixed drive in °C, or null when no drive reports one. Self-throttling: returns
    /// the cached value between refreshes, so it's safe to call every tick.
    /// </summary>
    public double? Read()
    {
        if (DateTime.UtcNow - _lastRead < RefreshInterval) return _cached;
        _lastRead = DateTime.UtcNow;

        if (!_enumerated) Enumerate();

        double? hottest = null;
        string? hottestVolume = null;
        foreach (var volume in _volumes)
        {
            if (QueryTemperature(volume) is not double c) continue;
            if (hottest is null || c > hottest)
            {
                hottest = c;
                hottestVolume = volume;
            }
        }

        _cached = hottest;
        HottestVolume = hottestVolume;
        return _cached;
    }

    private void Enumerate()
    {
        _enumerated = true;
        try
        {
            foreach (var d in DriveInfo.GetDrives())
            {
                if (d.DriveType != DriveType.Fixed || !d.IsReady) continue;
                var letter = d.Name.TrimEnd('\\');
                if (QueryTemperature(letter) is not null) _volumes.Add(letter);
            }
            Log.Info($"drive temperature: {_volumes.Count} volume(s) reporting " +
                     $"({string.Join(", ", _volumes)})");
        }
        catch (Exception ex) { Log.Error("drive temp enumerate", ex); }
    }

    private static double? QueryTemperature(string volume)
    {
        // Zero desired access = a query-only handle, which is what avoids needing admin.
        using var h = CreateFileW($@"\\.\{volume}", 0, FileShareReadWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
        if (h.IsInvalid) return null;

        var query = new StoragePropertyQuery { PropertyId = StorageDeviceTemperatureProperty, QueryType = 0 };
        const int bufSize = 512;
        var buf = Marshal.AllocHGlobal(bufSize);
        try
        {
            if (!DeviceIoControl(h, IoctlStorageQueryProperty, ref query, Marshal.SizeOf<StoragePropertyQuery>(),
                                 buf, bufSize, out var returned, IntPtr.Zero))
                return null;

            // STORAGE_TEMPERATURE_DATA_DESCRIPTOR: 24-byte header, then STORAGE_TEMPERATURE_INFO[].
            // The first info entry is the device's main sensor; Temperature is plain Celsius.
            const int headerSize = 24;
            if (returned < headerSize + 4) return null;
            var infoCount = (ushort)Marshal.ReadInt16(buf, 12);
            if (infoCount == 0) return null;

            var celsius = Marshal.ReadInt16(buf, headerSize + 2);
            // Drives that don't populate a sensor report SHRT_MIN rather than omitting it.
            return celsius is > -50 and < 200 ? celsius : null;
        }
        catch { return null; }
        finally { Marshal.FreeHGlobal(buf); }
    }
}
