using PwrMon.Models;
using PwrMon.Services;

namespace PwrMon.Tests;

/// <summary>
/// FormatLine/ParseLine round-trip tests — purely in memory, no disk, no AppSettings.Dir.
/// Uses the internal members exposed via InternalsVisibleTo in PwrMon.csproj.
/// </summary>
public class HistoryStoreTests
{
    private static PowerSample MakeSample() => new()
    {
        Time = new DateTimeOffset(2026, 3, 14, 9, 26, 53, 500, TimeSpan.FromHours(-5)),
        HasBattery = true,
        AcOnline = true,
        Charging = true,
        Discharging = false,
        ChargeRateW = 12.345,
        DischargeRateW = 0,
        BatteryPercent = 87.25,
        RemainingWh = 41.678,
        FullChargeWh = 50,
        VoltageV = 12.9,
        CpuPackageW = 8.111,
        IGpuW = 1.222,
        CpuLoadPct = 33.5,
        CpuPlatformW = 15.999,
        GapBefore = true,
    };

    [Fact]
    public void FormatLine_then_ParseLine_round_trips_core_fields()
    {
        var s = MakeSample();
        var line = HistoryStore.FormatLine(s);
        var parsed = HistoryStore.ParseLine(line);

        Assert.NotNull(parsed);
        Assert.Equal(s.Time.ToUniversalTime(), parsed!.Time.ToUniversalTime());
        Assert.Equal(s.ChargeRateW, parsed.ChargeRateW, 3);
        Assert.Equal(s.DischargeRateW, parsed.DischargeRateW, 3);
        Assert.Equal(s.BatteryPercent, parsed.BatteryPercent, 3);
        Assert.Equal(s.RemainingWh, parsed.RemainingWh, 3);
        Assert.Equal(s.VoltageV, parsed.VoltageV, 3);
        Assert.Equal(s.AcOnline, parsed.AcOnline);
        Assert.Equal(s.GapBefore, parsed.GapBefore);
    }

    [Fact]
    public void FormatLine_then_ParseLine_round_trips_optional_fields()
    {
        var s = MakeSample();
        var parsed = HistoryStore.ParseLine(HistoryStore.FormatLine(s));

        Assert.NotNull(parsed);
        Assert.Equal(s.CpuPackageW!.Value, parsed!.CpuPackageW!.Value, 3);
        Assert.Equal(s.IGpuW!.Value, parsed.IGpuW!.Value, 3);
        Assert.Equal(s.CpuLoadPct!.Value, parsed.CpuLoadPct!.Value, 3);
        Assert.Equal(s.CpuPlatformW!.Value, parsed.CpuPlatformW!.Value, 3);
    }

    [Fact]
    public void Null_optionals_serialize_to_empty_and_parse_back_to_null()
    {
        var s = MakeSample();
        var noOptionals = new PowerSample
        {
            Time = s.Time,
            AcOnline = s.AcOnline,
            ChargeRateW = s.ChargeRateW,
            DischargeRateW = s.DischargeRateW,
            BatteryPercent = s.BatteryPercent,
            RemainingWh = s.RemainingWh,
            VoltageV = s.VoltageV,
            GapBefore = s.GapBefore,
            CpuPackageW = null,
            IGpuW = null,
            CpuLoadPct = null,
            CpuPlatformW = null,
        };

        var line = HistoryStore.FormatLine(noOptionals);
        var fields = line.Split(',');
        // cpu_w, igpu_w, cpu_load, platform_w are columns 3,4,5,11 (0-indexed)
        Assert.Equal("", fields[3]);
        Assert.Equal("", fields[4]);
        Assert.Equal("", fields[5]);
        Assert.Equal("", fields[11]);

        var parsed = HistoryStore.ParseLine(line);
        Assert.NotNull(parsed);
        Assert.Null(parsed!.CpuPackageW);
        Assert.Null(parsed.IGpuW);
        Assert.Null(parsed.CpuLoadPct);
        Assert.Null(parsed.CpuPlatformW);
    }

    [Fact]
    public void Legacy_11_column_line_without_platform_w_still_parses()
    {
        // Back-compat: history files written before CpuPlatformW existed have no 12th column.
        var s = MakeSample();
        var fullLine = HistoryStore.FormatLine(s);
        var legacyLine = string.Join(",", fullLine.Split(',').Take(11));

        var parsed = HistoryStore.ParseLine(legacyLine);

        Assert.NotNull(parsed);
        Assert.Null(parsed!.CpuPlatformW);
        Assert.Equal(s.ChargeRateW, parsed.ChargeRateW, 3);
        Assert.Equal(s.CpuPackageW!.Value, parsed.CpuPackageW!.Value, 3);
    }

    [Theory]
    [InlineData("2026-03-14T09:26:53.500-05:00,12.345,0.000")] // fewer than 11 columns
    [InlineData("not,even,close,to,a,valid,csv,history,line,at,all")] // 11 columns of garbage
    [InlineData("")]
    public void Malformed_line_returns_null(string line)
    {
        Assert.Null(HistoryStore.ParseLine(line));
    }

    [Fact]
    public void Header_line_is_rejected()
    {
        const string header = "time,charge_w,discharge_w,cpu_w,igpu_w,cpu_load,percent,remaining_wh,voltage_v,ac,gap,platform_w";
        Assert.Null(HistoryStore.ParseLine(header));
    }
}
