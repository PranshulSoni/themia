using Themia.Core.Model;
using Themia.Core.Widgets;
using Xunit;

namespace Themia.Core.Tests;

public sealed class GpuRulesTests
{
    // ---- [S-433] which adapters a config means ---------------------------------------------

    [Fact]
    public void S433_uses_the_selection_in_its_own_order()
    {
        Assert.Equal([2, 0, 1], GpuRules.ResolveAdapters([2, 0, 1]));
    }

    [Fact]
    public void S433_drops_negative_and_repeated_entries()
    {
        Assert.Equal([0, 2], GpuRules.ResolveAdapters([0, -1, 2, 2, 0]));
    }

    [Fact]
    public void S433_an_absent_or_emptied_selection_draws_the_primary_adapter()
    {
        Assert.Equal([0], GpuRules.ResolveAdapters(null));
        Assert.Equal([0], GpuRules.ResolveAdapters([]));
        Assert.Equal([0], GpuRules.ResolveAdapters([-3]));
    }

    [Fact]
    public void S433_the_picker_never_re_ticks_the_primary_adapter()
    {
        // Unticking the last adapter writes an empty list; the picker must show it empty.
        Assert.Empty(GpuRules.SelectedAdapters([]));
        Assert.Empty(GpuRules.SelectedAdapters(null));
        Assert.Equal([2, 0], GpuRules.SelectedAdapters([2, -1, 2, 0]));
    }

    [Fact]
    public void S433_two_or_more_adapters_stack_one_fills()
    {
        Assert.False(GpuRules.Stacked(0));
        Assert.False(GpuRules.Stacked(1));
        Assert.True(GpuRules.Stacked(2));
        Assert.True(GpuRules.Stacked(5));
    }

    [Fact]
    public void S433_clone_does_not_share_the_adapter_list()
    {
        var config = new GpuConfig { Adapters = [1] };
        var copy = (GpuConfig)config.Clone();
        copy.Adapters.Add(2);
        Assert.Equal([1], config.Adapters);
    }

    // ---- [S-434] counter instances ----------------------------------------------------------

    [Fact]
    public void S434_engine_instances_match_their_adapter_although_they_start_with_a_pid()
    {
        // The original matched "starts with the LUID", which a pid_-prefixed name never does, so
        // load collapsed to zero as soon as an adapter was chosen.
        const string instance = "pid_1234_luid_0x00000000_0x0000ABCD_phys_0_eng_0_engtype_3D";
        Assert.Equal("luid_0x00000000_0x0000abcd", GpuRules.LuidOf(instance));
        Assert.Equal(GpuRules.LuidKey(0, 0xABCD), GpuRules.LuidOf(instance));
    }

    [Fact]
    public void S434_memory_instances_and_engine_instances_agree()
    {
        Assert.Equal(
            GpuRules.LuidOf("luid_0x00000000_0x0000abcd_phys_0"),
            GpuRules.LuidOf("pid_9_luid_0x00000000_0x0000abcd_phys_0_eng_1_engtype_VideoDecode"));
    }

    [Fact]
    public void S434_a_name_without_a_luid_matches_nothing()
    {
        Assert.Null(GpuRules.LuidOf("engtype_3d"));
        Assert.Null(GpuRules.LuidOf(string.Empty));
        Assert.Equal("luid_0x0_0x1", GpuRules.LuidOf("pid_1_luid_0x0_0x1"));
    }

    [Fact]
    public void S434_load_is_the_busiest_engine_summed_across_processes()
    {
        var luid = GpuRules.LuidKey(0, 0xABCD);
        var engines = new List<(string, double)>
        {
            ("pid_1_luid_0x00000000_0x0000abcd_phys_0_eng_0_engtype_3D", 20),
            ("pid_2_luid_0x00000000_0x0000abcd_phys_0_eng_0_engtype_3D", 15),
            ("pid_3_luid_0x00000000_0x0000abcd_phys_0_eng_4_engtype_VideoDecode", 60),

            // Another adapter, and junk: neither counts.
            ("pid_4_luid_0x00000000_0x0000ffff_phys_0_eng_0_engtype_3D", 99),
            ("pid_5_luid_0x00000000_0x0000abcd_phys_0_eng_0_engtype_3D", double.NaN),
            ("pid_6_luid_0x00000000_0x0000abcd_phys_0_eng_0_engtype_3D", -5),
        };

        // The decoder at 60% is busier than the 3D engine's 35%: Task Manager's figure, not the
        // original's 3D-only sum.
        Assert.Equal(60, GpuRules.Utilization(engines, luid));
    }

    [Fact]
    public void S434_load_is_clamped_and_zero_without_counters()
    {
        var luid = GpuRules.LuidKey(0, 1);
        Assert.Equal(0, GpuRules.Utilization([], luid));
        Assert.Equal(100, GpuRules.Utilization(
            [("pid_1_luid_0x00000000_0x00000001_phys_0_eng_0_engtype_3D", 80), ("pid_2_luid_0x00000000_0x00000001_phys_0_eng_0_engtype_3D", 70)],
            luid));
    }

    [Fact]
    public void S434_memory_adds_up_per_adapter_and_ignores_negatives()
    {
        var luid = GpuRules.LuidKey(0, 0xABCD);
        var items = new List<(string, long)>
        {
            ("luid_0x00000000_0x0000abcd_phys_0", 1024),
            ("luid_0x00000000_0x0000abcd_phys_1", 2048),
            ("luid_0x00000000_0x0000ffff_phys_0", 4096),
            ("luid_0x00000000_0x0000abcd_phys_0", -10),
        };

        Assert.Equal(3072UL, GpuRules.Bytes(items, luid));
    }

    // ---- [S-435] whose load figure wins ------------------------------------------------------

    [Fact]
    public void S435_the_drivers_figure_wins_over_the_counters()
    {
        // A borderless full-screen game: the 3D counter collapsed to 4% while the card was pegged.
        Assert.Equal(87, GpuRules.ChooseUtilization(87, 4));
    }

    [Fact]
    public void S435_the_counters_are_used_without_a_driver_figure()
    {
        Assert.Equal(23.5, GpuRules.ChooseUtilization(null, 23.5));
        Assert.Equal(23.5, GpuRules.ChooseUtilization(double.NaN, 23.5));
    }

    [Fact]
    public void S435_the_chosen_figure_is_clamped()
    {
        Assert.Equal(100, GpuRules.ChooseUtilization(140, 0));
        Assert.Equal(0, GpuRules.ChooseUtilization(-5, 0));
        Assert.Equal(100, GpuRules.ChooseUtilization(null, 250));
    }

    // ---- [S-432] / [S-437] pressure and memory rows -----------------------------------------

    [Theory]
    [InlineData(0, GpuPressure.Normal)]
    [InlineData(69.9, GpuPressure.Normal)]
    [InlineData(70, GpuPressure.High)]
    [InlineData(89.9, GpuPressure.High)]
    [InlineData(90, GpuPressure.Critical)]
    public void S432_load_pressure_thresholds(double percent, GpuPressure expected)
        => Assert.Equal(expected, GpuRules.LoadPressure(percent));

    [Theory]
    [InlineData(0.79, GpuPressure.Normal)]
    [InlineData(0.8, GpuPressure.High)]
    [InlineData(0.9, GpuPressure.Critical)]
    public void S437_memory_pressure_thresholds(double fraction, GpuPressure expected)
        => Assert.Equal(expected, GpuRules.MemoryPressure(fraction));

    [Fact]
    public void S437_dedicated_row_is_dropped_when_use_exceeds_the_reported_total()
    {
        // An integrated adapter: a 128 MB carve-out "using" 3 GB.
        Assert.False(GpuRules.ShowsDedicated(3UL << 30, 128UL << 20));
        Assert.True(GpuRules.ShowsDedicated(1UL << 30, 8UL << 30));
        Assert.True(GpuRules.ShowsDedicated(1UL << 30, 0));
        Assert.False(GpuRules.ShowsDedicated(0, 8UL << 30));
    }

    [Fact]
    public void S437_shared_row_needs_use_and_a_budget()
    {
        Assert.True(GpuRules.ShowsShared(1, 2));
        Assert.False(GpuRules.ShowsShared(0, 2));
        Assert.False(GpuRules.ShowsShared(1, 0));
    }

    [Theory]
    [InlineData("NVIDIA GeForce RTX 4060 Ti", "RTX 4060 Ti")]
    [InlineData("Intel(R) UHD Graphics 770", "UHD Graphics 770")]
    [InlineData("AMD Radeon(TM) RX 7900 XTX", "Radeon RX 7900 XTX")]
    [InlineData("NVIDIA", "NVIDIA")]
    [InlineData("  ", "GPU")]
    public void S430_card_titles_drop_vendor_and_trademark_noise(string name, string expected)
        => Assert.Equal(expected, GpuRules.ShortName(name));

    [Fact]
    public void S437_fraction_is_clamped_and_zero_without_a_total()
    {
        Assert.Equal(0.5, GpuRules.Fraction(1, 2));
        Assert.Equal(1, GpuRules.Fraction(5, 2));
        Assert.Equal(0, GpuRules.Fraction(5, 0));
    }
}
