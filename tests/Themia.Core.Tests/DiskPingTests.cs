using Themia.Core.Model;
using Themia.Core.Widgets;
using Xunit;

namespace Themia.Core.Tests;

/// <summary>The disk and ping meters' rules (S-1160..S-1174).</summary>
public sealed class DiskPingTests
{
    private const ulong GiB = 1024UL * 1024 * 1024;

    // ---- S-1160: which drives ------------------------------------------------------------------

    [Theory]
    [InlineData("C:\\", "C:\\")]
    [InlineData("c:", "C:\\")]
    [InlineData(" d ", "D:\\")]
    [InlineData("e:/", "E:\\")]
    [InlineData("", null)]
    [InlineData("_Total", null)]
    [InlineData("HarddiskVolume3", null)]
    [InlineData("C:\\Mount", null)]
    [InlineData("\\\\server\\share", null)]
    [InlineData("1:", null)]
    public void S1160_only_a_drive_letter_is_a_root(string drive, string? expected)
        => Assert.Equal(expected, DiskRules.Root(drive));

    [Fact]
    public void S1160_a_clean_config_repairs_to_nothing_and_clones_every_option()
    {
        var config = new DiskConfig { Drive = "D:\\", IoScope = DiskIoScope.Drive, IoDrive = "C:\\" };

        Assert.Empty(config.Repair());

        var copy = (DiskConfig)config.Clone();
        Assert.Equal("D:\\", copy.Drive);
        Assert.Equal((DiskIoScope.Drive, "C:\\"), (copy.IoScope, copy.IoDrive));
    }

    [Theory]
    [InlineData("d", "D:\\")]
    [InlineData("D:", "D:\\")]
    [InlineData("d:\\", "D:\\")]
    [InlineData("", "")]
    public void S1160_repair_normalises_the_drive_to_its_root(string drive, string expected)
    {
        var config = new DiskConfig { Drive = drive };

        Assert.Empty(config.Repair());
        Assert.Equal(expected, config.Drive);
    }

    [Theory]
    [InlineData("not a drive")]
    [InlineData("\\\\server\\share")]
    [InlineData("1:")]
    public void S1160_a_drive_that_is_not_a_drive_letter_is_dropped_loudly_for_the_system_drive(string drive)
    {
        var config = new DiskConfig { Drive = drive };

        Assert.Single(config.Repair());
        Assert.Equal(string.Empty, config.Drive);
    }

    [Fact]
    public void S1163_the_history_is_on_by_default_because_it_charts_reads_and_writes()
        => Assert.True(new DiskConfig().ShowGraph);

    // ---- S-1162: per-drive rates -----------------------------------------------------------------

    [Fact]
    public void S1162_rates_pair_by_drive_and_leave_out_the_total_and_folder_mounts()
    {
        var rates = DiskRules.Pair(
            [("C:", 1000), ("_Total", 5000), ("HarddiskVolume4", 7), ("d:", 10)],
            [("C:", 200), ("E:", 30)]);

        Assert.Equal(new DiskRates(1000, 200), rates["C:"]);
        Assert.Equal(new DiskRates(10, 0), rates["D:"]);
        Assert.Equal(new DiskRates(0, 30), rates["E:"]);
        Assert.Equal(3, rates.Count);
    }

    [Fact]
    public void S1162_a_drive_the_counters_do_not_list_has_no_rate_never_the_total()
    {
        var rates = DiskRules.Pair([("_Total", 5000), ("C:", 1)], []);

        Assert.Null(DiskRules.RatesFor("D:\\", rates));
        Assert.Equal(new DiskRates(1, 0), DiskRules.RatesFor("c:\\", rates));
    }

    [Fact]
    public void S1162_negative_or_non_finite_counter_values_read_as_zero()
    {
        var rates = DiskRules.Pair([("C:", -5), ("D:", double.NaN)], [("C:", double.PositiveInfinity)]);

        Assert.Equal(new DiskRates(0, 0), rates["C:"]);
        Assert.Equal(new DiskRates(0, 0), rates["D:"]);
    }

    // ---- S-1163: the chart is I/O, of the drive the settings name ----------------------------------

    [Fact]
    public void S1163_the_chart_follows_the_scope()
    {
        var rates = DiskRules.Pair([("C:", 100), ("D:", 10)], [("C:", 1), ("D:", 2)]);

        Assert.Equal(new DiskRates(100, 1), DiskRules.Charted(DiskIoScope.Reading, "C:\\", "", rates));
        Assert.Equal(new DiskRates(110, 3), DiskRules.Charted(DiskIoScope.All, "C:\\", "", rates));
        Assert.Equal(new DiskRates(10, 2), DiskRules.Charted(DiskIoScope.Drive, "C:\\", "d:", rates));
        Assert.Null(DiskRules.Charted(DiskIoScope.Drive, "C:\\", "E:\\", rates));
        Assert.Null(DiskRules.Charted(DiskIoScope.All, "C:\\", "", DiskRules.Pair([], [])));
    }

    [Fact]
    public void S1163_all_drives_sums_the_letters_and_never_adds_the_total_instance()
        => Assert.Equal(new DiskRates(30, 0), DiskRules.Total(DiskRules.Pair([("C:", 10), ("D:", 20), ("_Total", 30)], [])));

    [Fact]
    public void S1163_the_chart_names_what_it_plots()
    {
        Assert.Equal("C:", DiskRules.ChartedName(DiskIoScope.Reading, "C:\\", "D:\\"));
        Assert.Equal("all drives", DiskRules.ChartedName(DiskIoScope.All, "C:\\", ""));
        Assert.Equal("D:", DiskRules.ChartedName(DiskIoScope.Drive, "C:\\", "d"));
    }

    [Fact]
    public void S1163_repair_falls_back_to_the_reading_for_a_bad_scope_or_drive()
    {
        var bad = new DiskConfig { IoScope = DiskIoScope.Drive, IoDrive = @"\\nas\share" };
        Assert.Single(bad.Repair());
        Assert.Equal((DiskIoScope.Reading, ""), (bad.IoScope, bad.IoDrive));

        var unknown = new DiskConfig { IoScope = (DiskIoScope)9 };
        Assert.Single(unknown.Repair());
        Assert.Equal(DiskIoScope.Reading, unknown.IoScope);

        var loose = new DiskConfig { IoScope = DiskIoScope.Drive, IoDrive = "e" };
        Assert.Empty(loose.Repair());
        Assert.Equal("E:\\", loose.IoDrive);
    }

    // ---- S-1161 / S-1163..S-1166: wording ----------------------------------------------------------

    [Fact]
    public void S1161_a_drive_is_named_as_explorer_names_it()
    {
        Assert.Equal("System (C:)", DiskRules.Title(new DiskVolume("C:\\", " System ", "NTFS", false, 0, 0)));
        Assert.Equal("Local Disk (D:)", DiskRules.Title(new DiskVolume("D:\\", "", "NTFS", false, 0, 0)));
        Assert.Equal("USB Drive (E:)", DiskRules.Title(new DiskVolume("E:\\", "", "exFAT", true, 0, 0)));
    }

    [Fact]
    public void S1161_used_and_free_follow_the_volume()
    {
        var volume = new DiskVolume("C:\\", "", "", false, 100 * GiB, 25 * GiB);

        Assert.Equal(75 * GiB, volume.Used);
        Assert.Equal(0.75, volume.UsedFraction);
        Assert.Null(new DiskVolume("C:\\", "", "", false, 0, 0).UsedFraction);
        Assert.Equal(0UL, new DiskVolume("C:\\", "", "", false, 10, 20).Used);
    }

    // ---- S-1166: the drives list -------------------------------------------------------------------

    // ---- S-1171: ping statistics -------------------------------------------------------------------

    [Fact]
    public void S1171_the_history_holds_the_span_and_forgets_the_oldest()
    {
        var history = new PingHistory(3);
        history.Add(1);
        history.Add(null);
        history.Add(3);
        history.Add(4);

        Assert.Equal(3, history.Count);
        Assert.Equal(4.0, history.Last);

        history.Capacity = 1;
        Assert.Equal(1, history.Count);
        Assert.Equal(4.0, history.Last);

        history.Clear();
        Assert.False(history.Any);
        Assert.Null(history.Last);
    }

    // ---- S-1170 / S-1172: status -------------------------------------------------------------------

    [Theory]
    [InlineData(false, false, PingStatus.Checking)]
    [InlineData(true, true, PingStatus.Online)]
    [InlineData(true, false, PingStatus.Offline)]
    public void S1170_status_follows_the_last_probe(bool any, bool answered, PingStatus expected)
        => Assert.Equal(expected, PingStats.Status(any, answered));

    [Fact]
    public void S1172_online_is_success_offline_is_critical()
    {
        Assert.Equal(MeterPressure.Healthy, PingStats.Pressure(PingStatus.Online));
        Assert.Equal(MeterPressure.Critical, PingStats.Pressure(PingStatus.Offline));
        Assert.Equal(MeterPressure.Normal, PingStats.Pressure(PingStatus.Checking));
    }

    // ---- S-1173: the host's name --------------------------------------------------------------------

    [Fact]
    public void S1173_a_blank_label_is_none_and_an_overlong_one_is_cut()
    {
        Assert.Null(new PingConfig { HostLabel = "  " }.EffectiveLabel);
        Assert.Equal("Router", new PingConfig { HostLabel = " Router " }.EffectiveLabel);

        var config = new PingConfig { HostLabel = new string('x', PingConfig.MaxLabelLength + 5) };
        Assert.Single(config.Repair());
        Assert.Equal(PingConfig.MaxLabelLength, config.HostLabel.Length);
    }

    [Fact]
    public void S1173_clone_keeps_the_host_and_its_label()
    {
        var copy = (PingConfig)new PingConfig { Host = "8.8.8.8", HostLabel = "DNS" }.Clone();

        Assert.Equal(("8.8.8.8", "DNS"), (copy.Host, copy.HostLabel));
    }
}
