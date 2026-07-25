using PwrMon.Models;
using PwrMon.Services;

namespace PwrMon.Tests;

/// <summary>
/// Tests UnitFormatter against AppSettings.Current, which is a shared process-wide static.
/// Every test sets the unit(s) it depends on explicitly at the start. Never calls
/// AppSettings.Load()/Save() and never touches AppSettings.Dir (see project safety rule).
/// Assembly-wide parallelization is disabled (see AssemblyInfo.cs) so these tests can't race.
/// </summary>
public class UnitFormatterTests
{
    [Fact]
    public void Power_uses_two_decimals_under_ten_watts()
    {
        AppSettings.Current.PowerUnit = PowerUnit.Watts;
        Assert.Equal("9.99 W", UnitFormatter.Power(9.99));
    }

    [Fact]
    public void Power_uses_one_decimal_at_or_above_ten_watts()
    {
        AppSettings.Current.PowerUnit = PowerUnit.Watts;
        Assert.Equal("10.0 W", UnitFormatter.Power(10));
    }

    [Fact]
    public void Power_signed_adds_plus_above_the_0_05_floor()
    {
        AppSettings.Current.PowerUnit = PowerUnit.Watts;
        Assert.Equal("+5.00 W", UnitFormatter.Power(5, signed: true));
    }

    [Fact]
    public void Power_signed_omits_plus_at_or_below_0_05()
    {
        AppSettings.Current.PowerUnit = PowerUnit.Watts;
        Assert.Equal("0.05 W", UnitFormatter.Power(0.05, signed: true));
    }

    [Fact]
    public void Power_signed_never_adds_plus_when_unsigned_requested()
    {
        AppSettings.Current.PowerUnit = PowerUnit.Watts;
        Assert.Equal("5.00 W", UnitFormatter.Power(5, signed: false));
    }

    [Fact]
    public void Power_milliwatts_mode_converts_and_rounds_to_whole_number()
    {
        AppSettings.Current.PowerUnit = PowerUnit.Milliwatts;
        Assert.Equal("1,500 mW", UnitFormatter.Power(1.5));
    }

    [Fact]
    public void Energy_watt_hours_mode_formats_with_one_decimal()
    {
        AppSettings.Current.EnergyUnit = EnergyUnit.WattHours;
        Assert.Equal("41.7 Wh", UnitFormatter.Energy(41.678, 12.0));
    }

    [Fact]
    public void Energy_mAh_mode_uses_supplied_voltage()
    {
        AppSettings.Current.EnergyUnit = EnergyUnit.MilliampHours;
        // 50 Wh / 10 V * 1000 = 5000 mAh
        Assert.Equal("5,000 mAh", UnitFormatter.Energy(50, 10));
    }

    [Fact]
    public void Energy_mAh_mode_falls_back_to_11_1V_when_voltage_at_or_below_one()
    {
        AppSettings.Current.EnergyUnit = EnergyUnit.MilliampHours;
        var expected = $"{50.0 / 11.1 * 1000:N0} mAh";
        Assert.Equal(expected, UnitFormatter.Energy(50, 1));
        Assert.Equal(expected, UnitFormatter.Energy(50, 0));
    }

    [Fact]
    public void Duration_null_is_em_dash()
    {
        Assert.Equal("—", UnitFormatter.Duration(null));
    }

    [Fact]
    public void Duration_at_or_above_24_hours_is_more_than_a_day()
    {
        Assert.Equal("> 1 day", UnitFormatter.Duration(TimeSpan.FromHours(24)));
        Assert.Equal("> 1 day", UnitFormatter.Duration(TimeSpan.FromHours(30)));
    }

    [Fact]
    public void Duration_under_one_minute_is_less_than_a_minute()
    {
        Assert.Equal("< 1 min", UnitFormatter.Duration(TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public void Duration_formats_hours_and_minutes()
    {
        Assert.Equal("3:07", UnitFormatter.Duration(TimeSpan.FromMinutes(187)));
    }

    [Fact]
    public void Percent_formats_with_one_decimal()
    {
        Assert.Equal("87.3%", UnitFormatter.Percent(87.27));
    }
}
