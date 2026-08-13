using PwrMon.Models;
using PwrMon.Services;

namespace PwrMon.Tests;

/// <summary>
/// FormatLine/ParseLine round-trip tests — purely in memory, no disk, no AppSettings.Dir.
/// Uses the internal members exposed via InternalsVisibleTo in PwrMon.csproj.
/// </summary>
public class HistoryStoreTests
{
    private static PowerSample MakeSample(double? wall = null) => new()
    {
        EstWallW = wall,
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
        CpuTempC = 64.5,
        DriveTempC = 45,
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
        Assert.Equal(s.CpuTempC!.Value, parsed.CpuTempC!.Value, 3);
        Assert.Equal(s.DriveTempC!.Value, parsed.DriveTempC!.Value, 3);
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
            CpuTempC = null,
            DriveTempC = null,
        };

        var line = HistoryStore.FormatLine(noOptionals);
        var fields = line.Split(',');
        // cpu_w, igpu_w, cpu_load, platform_w, cpu_temp_c, drive_temp_c are columns
        // 3,4,5,11,12,13 (0-indexed)
        Assert.Equal("", fields[3]);
        Assert.Equal("", fields[4]);
        Assert.Equal("", fields[5]);
        Assert.Equal("", fields[11]);
        Assert.Equal("", fields[12]);
        Assert.Equal("", fields[13]);

        var parsed = HistoryStore.ParseLine(line);
        Assert.NotNull(parsed);
        Assert.Null(parsed!.CpuPackageW);
        Assert.Null(parsed.IGpuW);
        Assert.Null(parsed.CpuLoadPct);
        Assert.Null(parsed.CpuPlatformW);
        Assert.Null(parsed.CpuTempC);
        Assert.Null(parsed.DriveTempC);
    }

    [Fact]
    public void Legacy_14_column_line_without_wall_w_still_parses()
    {
        // Back-compat: every history file written before 2026-08-13 stops at drive_temp_c.
        // Those rows must keep loading, with wall simply absent rather than zero — a chart
        // series that plots 0 W for all of history would be a lie, not a gap.
        var s = MakeSample();
        var legacy = string.Join(',', HistoryStore.FormatLine(s).Split(',').Take(14));

        var parsed = HistoryStore.ParseLine(legacy);

        Assert.NotNull(parsed);
        Assert.Equal(s.DriveTempC!.Value, parsed!.DriveTempC!.Value, 3);
        Assert.Null(parsed.EstWallW);
    }

    [Fact]
    public void Wall_w_round_trips_and_stays_null_when_unknowable()
    {
        var withWall = HistoryStore.ParseLine(HistoryStore.FormatLine(MakeSample(wall: 81.4)));
        Assert.Equal(81.4, withWall!.EstWallW!.Value, 3);

        // off AC / adapter-assist: the sampler leaves it null and the CSV field is empty
        var noWall = HistoryStore.ParseLine(HistoryStore.FormatLine(MakeSample(wall: null)));
        Assert.Null(noWall!.EstWallW);
    }

    [Fact]
    public void Legacy_12_column_line_without_temperatures_still_parses()
    {
        // Back-compat: history written before the temperature columns existed stops at
        // platform_w. Same append-only guarantee the platform_w column relies on.
        var s = MakeSample();
        var legacy = string.Join(',', HistoryStore.FormatLine(s).Split(',').Take(12));

        var parsed = HistoryStore.ParseLine(legacy);

        Assert.NotNull(parsed);
        Assert.Equal(s.CpuPlatformW!.Value, parsed!.CpuPlatformW!.Value, 3);
        Assert.Null(parsed.CpuTempC);
        Assert.Null(parsed.DriveTempC);
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

    // ShouldFlushBeforeAppending: regression coverage for the midnight-rollover bug, where
    // the day check ran *after* the sample was already buffered, so the first sample of a
    // new day landed in the previous day's file alongside it.

    [Fact]
    public void No_flush_needed_for_the_very_first_sample_ever_buffered()
    {
        Assert.False(HistoryStore.ShouldFlushBeforeAppending(bufferedDay: null, sampleDay: "2026-03-14"));
    }

    [Fact]
    public void No_flush_needed_when_the_sample_matches_the_buffered_day()
    {
        Assert.False(HistoryStore.ShouldFlushBeforeAppending(bufferedDay: "2026-03-14", sampleDay: "2026-03-14"));
    }

    [Fact]
    public void Flush_is_required_when_the_sample_is_a_different_day_than_whats_buffered()
    {
        // This is the midnight case: yesterday's rows are still sitting in the buffer when
        // the first sample past midnight arrives. They must be flushed under yesterday's
        // filename before today's sample is allowed to join the buffer.
        Assert.True(HistoryStore.ShouldFlushBeforeAppending(bufferedDay: "2026-03-14", sampleDay: "2026-03-15"));
    }
}
