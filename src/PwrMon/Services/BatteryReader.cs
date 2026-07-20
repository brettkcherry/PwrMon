using System.Management;
using PwrMon.Models;

namespace PwrMon.Services;

public sealed record BatteryReading(
    bool HasBattery,
    bool AcOnline,
    bool Charging,
    bool Discharging,
    double ChargeRateW,
    double DischargeRateW,
    double RemainingWh,
    double FullChargeWh,
    double VoltageV,
    double Percent);

/// <summary>
/// Reads live battery telemetry from the ACPI battery classes in root\wmi.
/// Works without elevation. Values verified against this project's SensorProbe:
/// rates/capacities arrive in mW / mWh, voltage in mV.
/// </summary>
public sealed class BatteryReader : IDisposable
{
    // Firmware occasionally reports nonsense (int wraparound); anything above this is discarded.
    private const double MaxSaneRateMw = 500_000;

    private readonly ManagementObjectSearcher _status = new(
        @"root\wmi", "SELECT ChargeRate,DischargeRate,Charging,Discharging,PowerOnline,RemainingCapacity,Voltage FROM BatteryStatus WHERE Active=TRUE");
    private readonly ManagementObjectSearcher _fullCap = new(
        @"root\wmi", "SELECT FullChargedCapacity FROM BatteryFullChargedCapacity WHERE Active=TRUE");

    private double _fullChargeWh;
    private DateTime _fullChargeRefreshed = DateTime.MinValue;

    public BatteryReading Read()
    {
        bool found = false, ac = false, charging = false, discharging = false;
        double chargeMw = 0, dischargeMw = 0, remainingMwh = 0, voltageMv = 0;

        foreach (ManagementBaseObject obj in _status.Get())
        {
            found = true;
            ac |= AsBool(obj["PowerOnline"]);
            charging |= AsBool(obj["Charging"]);
            discharging |= AsBool(obj["Discharging"]);
            chargeMw += SaneRate(AsDouble(obj["ChargeRate"]));
            dischargeMw += SaneRate(AsDouble(obj["DischargeRate"]));
            remainingMwh += AsDouble(obj["RemainingCapacity"]);
            voltageMv = Math.Max(voltageMv, AsDouble(obj["Voltage"]));
            obj.Dispose();
        }

        if (!found)
            return new BatteryReading(false, true, false, false, 0, 0, 0, 0, 0, 0);

        RefreshFullChargeIfStale();

        var remainingWh = remainingMwh / 1000.0;
        var percent = _fullChargeWh > 0 ? Math.Clamp(remainingWh / _fullChargeWh * 100.0, 0, 100) : 0;

        return new BatteryReading(
            HasBattery: true,
            AcOnline: ac,
            Charging: charging,
            Discharging: discharging,
            ChargeRateW: chargeMw / 1000.0,
            DischargeRateW: dischargeMw / 1000.0,
            RemainingWh: remainingWh,
            FullChargeWh: _fullChargeWh,
            VoltageV: voltageMv / 1000.0,
            Percent: percent);
    }

    /// <summary>Full-charge capacity drifts only as the battery wears; re-read once a minute.</summary>
    private void RefreshFullChargeIfStale()
    {
        if ((DateTime.UtcNow - _fullChargeRefreshed).TotalSeconds < 60 && _fullChargeWh > 0)
            return;
        try
        {
            double mwh = 0;
            foreach (ManagementBaseObject obj in _fullCap.Get())
            {
                mwh += AsDouble(obj["FullChargedCapacity"]);
                obj.Dispose();
            }
            if (mwh > 0) _fullChargeWh = mwh / 1000.0;
            _fullChargeRefreshed = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            Log.Error("full-charge capacity query", ex);
        }
    }

    /// <summary>Static pack info; call rarely (startup + settings refresh).</summary>
    public BatteryStaticInfo ReadStatic()
    {
        double designMwh = 0, designVoltageMv = 0;
        int? cycles = null;
        string chemistry = "", manufacturer = "", deviceName = "";

        try
        {
            using var s = new ManagementObjectSearcher(@"root\wmi",
                "SELECT DesignedCapacity,Chemistry,ManufactureName,DeviceName FROM BatteryStaticData WHERE Active=TRUE");
            foreach (ManagementBaseObject obj in s.Get())
            {
                designMwh += AsDouble(obj["DesignedCapacity"]);
                if (chemistry.Length == 0) chemistry = DecodeChemistry(AsDouble(obj["Chemistry"]));
                if (manufacturer.Length == 0) manufacturer = obj["ManufactureName"]?.ToString()?.Trim() ?? "";
                if (deviceName.Length == 0) deviceName = obj["DeviceName"]?.ToString()?.Trim() ?? "";
                obj.Dispose();
            }
        }
        catch (Exception ex) { Log.Error("BatteryStaticData", ex); }

        try
        {
            using var s = new ManagementObjectSearcher(@"root\wmi",
                "SELECT CycleCount FROM BatteryCycleCount WHERE Active=TRUE");
            foreach (ManagementBaseObject obj in s.Get())
            {
                var c = (int)AsDouble(obj["CycleCount"]);
                if (c > 0) cycles = Math.Max(cycles ?? 0, c);
                obj.Dispose();
            }
        }
        catch { /* class often absent; fine */ }

        try
        {
            using var s = new ManagementObjectSearcher(@"root\cimv2", "SELECT DesignVoltage FROM Win32_Battery");
            foreach (ManagementBaseObject obj in s.Get())
            {
                designVoltageMv = Math.Max(designVoltageMv, AsDouble(obj["DesignVoltage"]));
                obj.Dispose();
            }
        }
        catch { /* optional */ }

        RefreshFullChargeIfStale();

        return new BatteryStaticInfo
        {
            HasBattery = designMwh > 0 || _fullChargeWh > 0,
            DesignWh = designMwh / 1000.0,
            FullChargeWh = _fullChargeWh,
            CycleCount = cycles,
            Chemistry = chemistry,
            Manufacturer = manufacturer,
            DeviceName = deviceName,
            DesignVoltageV = designVoltageMv / 1000.0,
        };
    }

    /// <summary>Chemistry arrives as a 4-char ASCII code packed little-endian into a uint (e.g. "LiP").</summary>
    private static string DecodeChemistry(double raw)
    {
        var v = (uint)raw;
        if (v == 0) return "";
        var chars = new[] { (char)(v & 0xFF), (char)((v >> 8) & 0xFF), (char)((v >> 16) & 0xFF), (char)((v >> 24) & 0xFF) };
        var code = new string(chars).TrimEnd('\0', ' ');
        return code.ToUpperInvariant() switch
        {
            "LION" or "LI-I" or "LI" => "Li-ion",
            "LIP" => "Li-polymer",
            "PBAC" => "Lead-acid",
            "NICD" => "NiCd",
            "NIMH" => "NiMH",
            _ => code,
        };
    }

    private static double SaneRate(double mw) => mw is > 0 and < MaxSaneRateMw ? mw : 0;

    private static bool AsBool(object? v) => v is bool b && b;

    private static double AsDouble(object? v)
    {
        if (v is null) return 0;
        try { return Convert.ToDouble(v); } catch { return 0; }
    }

    public void Dispose()
    {
        _status.Dispose();
        _fullCap.Dispose();
    }
}
