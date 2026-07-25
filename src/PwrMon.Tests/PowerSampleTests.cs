using PwrMon.Models;

namespace PwrMon.Tests;

/// <summary>Pure computed properties on the model types — no hardware, no settings.</summary>
public class PowerSampleTests
{
    [Fact]
    public void NetW_is_charge_minus_discharge()
    {
        var s = new PowerSample { ChargeRateW = 12.5, DischargeRateW = 3.25 };
        Assert.Equal(9.25, s.NetW, 10);
    }

    [Fact]
    public void NetW_is_negative_when_discharging_dominates()
    {
        var s = new PowerSample { ChargeRateW = 0, DischargeRateW = 8.4 };
        Assert.Equal(-8.4, s.NetW, 10);
    }

    [Fact]
    public void CurrentA_divides_net_by_voltage_when_voltage_above_one()
    {
        var s = new PowerSample { ChargeRateW = 10, DischargeRateW = 0, VoltageV = 12.5 };
        Assert.Equal(0.8, s.CurrentA, 10);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(0.5)]
    public void CurrentA_is_zero_when_voltage_at_or_below_one(double voltage)
    {
        var s = new PowerSample { ChargeRateW = 10, DischargeRateW = 0, VoltageV = voltage };
        Assert.Equal(0, s.CurrentA);
    }

    [Fact]
    public void WearPct_is_zero_when_design_capacity_is_zero()
    {
        var info = new BatteryStaticInfo { DesignWh = 0, FullChargeWh = 40 };
        Assert.Equal(0, info.WearPct);
    }

    [Fact]
    public void WearPct_computes_percentage_lost_from_design()
    {
        var info = new BatteryStaticInfo { DesignWh = 50, FullChargeWh = 45 };
        Assert.Equal(10, info.WearPct, 10);
    }

    [Fact]
    public void WearPct_clamps_to_zero_when_full_charge_exceeds_design()
    {
        // e.g. a freshly-calibrated pack reporting slightly above its nominal design capacity
        var info = new BatteryStaticInfo { DesignWh = 50, FullChargeWh = 52 };
        Assert.Equal(0, info.WearPct);
    }

    [Fact]
    public void AvgDischargeW_is_zero_below_the_tiny_time_floor()
    {
        // TotalHours <= 0.003 (~10.8 s) is treated as "not enough signal yet"
        var stats = new SessionStats { EnergyOutWh = 5, TimeOnBattery = TimeSpan.FromHours(0.003) };
        Assert.Equal(0, stats.AvgDischargeW);
    }

    [Fact]
    public void AvgDischargeW_divides_energy_by_hours_above_the_floor()
    {
        var stats = new SessionStats { EnergyOutWh = 20, TimeOnBattery = TimeSpan.FromHours(2) };
        Assert.Equal(10, stats.AvgDischargeW, 10);
    }

    [Theory]
    [InlineData(PowerEventKind.AcConnected, "AC in")]
    [InlineData(PowerEventKind.AcDisconnected, "AC out")]
    [InlineData(PowerEventKind.Resumed, "resume")]
    [InlineData(PowerEventKind.AppStarted, "start")]
    public void PowerEvent_label_matches_kind(PowerEventKind kind, string expected)
    {
        var e = new PowerEvent(DateTimeOffset.Now, kind);
        Assert.Equal(expected, e.Label);
    }
}
