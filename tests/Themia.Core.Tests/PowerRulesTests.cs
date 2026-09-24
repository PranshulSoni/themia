using Themia.Core.Widgets;
using Xunit;

namespace Themia.Core.Tests;

/// <summary>
/// What <c>SYSTEM_POWER_STATUS</c> means. [S-230] [S-231] [S-232]
/// <para>
/// Every field in that struct uses 255 for "unknown" and every one of them is a plausible-looking
/// number if you take it at face value: 255% charge, a battery on a desktop, 136 years remaining.
/// These are the readings the widget must not repeat back.
/// </para>
/// </summary>
public class PowerRulesTests
{
    /// <summary>A desktop: mains, bit 7 of the flag set, no percentage.</summary>
    private static PowerSnapshot Desktop(string? plan = "Balanced")
        => PowerRules.Read(1, 128, 255, 0, uint.MaxValue, plan);

    /// <summary>A laptop discharging at 64% with two hours left.</summary>
    private static PowerSnapshot Laptop()
        => PowerRules.Read(0, 4, 64, 0, 7200, "Balanced");

    [Fact]
    public void S230_A_desktop_has_no_battery_rather_than_a_battery_at_255_percent()
    {
        var snapshot = Desktop();

        Assert.True(snapshot.Readable);
        Assert.False(snapshot.HasBattery);
        Assert.Null(snapshot.Percent);
        Assert.Equal("--", PowerRules.BatteryReadout(snapshot));
        Assert.Equal("No battery", PowerRules.BatteryCaption(snapshot, showRemaining: true));
    }

    [Fact]
    public void S230_A_machine_windows_will_not_answer_about_keeps_its_battery_row()
    {
        // 255 in the flag is "I do not know", which is a different claim from "there is none".
        // Calling it a desktop would be the widget inventing a fact about the hardware.
        var snapshot = PowerRules.Read(255, 255, 255, 0, uint.MaxValue, null);

        Assert.True(snapshot.HasBattery);
        Assert.Null(snapshot.Percent);
        Assert.Equal(PowerSource.Unknown, snapshot.Source);
        Assert.Equal("Unknown", PowerRules.SourceLabel(snapshot));
    }

    [Fact]
    public void S230_A_real_percentage_outranks_a_flag_that_says_there_is_no_battery()
    {
        // Contradictory, and seen in the wild on machines with a detachable battery. Something
        // reporting 80% charge has a battery, whatever else it set.
        var snapshot = PowerRules.Read(0, 128, 80, 0, uint.MaxValue, null);

        Assert.True(snapshot.HasBattery);
        Assert.Equal(80, snapshot.Percent);
    }

    [Fact]
    public void S231_The_time_remaining_sentinel_is_not_shown_as_a_time()
    {
        // 0xFFFFFFFF seconds is 136 years.
        var snapshot = PowerRules.Read(0, 4, 50, 0, uint.MaxValue, null);

        Assert.Null(snapshot.Remaining);
        Assert.Equal("On battery", PowerRules.BatteryCaption(snapshot, showRemaining: true));
    }

    [Fact]
    public void S231_Time_remaining_is_only_offered_while_discharging()
    {
        // Plugged in, Windows leaves whatever it last computed in the field, so "2h left" under a
        // charging battery is simply wrong.
        var charging = PowerRules.Read(1, 4, 50, 0, 7200, null);

        Assert.Null(charging.Remaining);
        Assert.Equal("Charging", PowerRules.BatteryCaption(charging, showRemaining: true));
    }

    [Fact]
    public void S231_A_discharging_battery_says_how_long_is_left()
    {
        Assert.Equal("2h 0m left", PowerRules.BatteryCaption(Laptop(), showRemaining: true));
        Assert.Equal("On battery", PowerRules.BatteryCaption(Laptop(), showRemaining: false));
    }

    [Fact]
    public void S231_A_full_battery_on_mains_says_so_rather_than_claiming_to_be_charging()
    {
        var full = PowerRules.Read(1, 1, 100, 0, uint.MaxValue, null);

        Assert.Equal("Fully charged", PowerRules.BatteryCaption(full, showRemaining: true));
    }

    [Fact]
    public void S232_Battery_saver_comes_from_the_status_flag()
    {
        Assert.True(PowerRules.Read(0, 4, 20, 1, 1800, null).BatterySaver);
        Assert.False(PowerRules.Read(0, 4, 20, 0, 1800, null).BatterySaver);
        Assert.Equal("On", PowerRules.SaverLabel(PowerRules.Read(0, 4, 20, 1, 1800, null)));
    }

    [Fact]
    public void S233_A_plan_name_out_of_the_registry_is_collapsed_to_one_line()
    {
        // It comes from the power registry, where a vendor tool can have put anything. A row is
        // one line tall whatever arrives in it.
        var snapshot = PowerRules.Read(1, 128, 255, 0, uint.MaxValue, "  High \r\n performance  ");

        Assert.Equal("High performance", snapshot.PlanName);
    }

    [Fact]
    public void S233_A_plan_name_that_is_only_whitespace_is_an_absent_name()
    {
        Assert.Null(PowerRules.PlanDisplayName("   "));
        Assert.Null(PowerRules.PlanDisplayName(null));
        Assert.Equal("Unavailable", PowerRules.PlanLabel(Desktop(plan: null)));
    }

    [Fact]
    public void S233_A_very_long_plan_name_cannot_push_the_row_open()
    {
        var name = PowerRules.PlanDisplayName(new string('x', 400));

        Assert.NotNull(name);
        Assert.Equal(64, name.Length);
    }

    [Fact]
    public void S234_An_unreadable_status_is_a_state_of_its_own()
    {
        Assert.False(PowerSnapshot.Unreadable.Readable);
        Assert.False(PowerSnapshot.Unreadable.HasBattery);
        Assert.Equal(PowerSource.Unknown, PowerSnapshot.Unreadable.Source);
    }

    [Fact]
    public void S230_The_source_is_said_in_words()
    {
        Assert.Equal("Plugged in", PowerRules.SourceLabel(Desktop()));
        Assert.Equal("On battery", PowerRules.SourceLabel(Laptop()));
    }
}
