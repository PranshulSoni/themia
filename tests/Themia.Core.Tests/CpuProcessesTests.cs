using Themia.Core.Model;
using Themia.Core.Widgets;
using Xunit;

namespace Themia.Core.Tests;

/// <summary>The processor meter's extras and the process list's ladder (S-1140..S-1159).</summary>
public sealed class CpuProcessesTests
{
    // ---- S-1142 per-core load ----------------------------------------------------------------

    [Fact]
    public void S1142_load_is_busy_over_elapsed_with_idle_inside_kernel()
    {
        // 100 ticks elapsed (kernel 60 incl. idle 25, user 40): busy 75.
        var load = CpuRules.Load(new CoreTimes(0, 0, 0), new CoreTimes(25, 60, 40));
        Assert.Equal(0.75, load, 6);
    }

    [Theory]
    [InlineData(10, 10, 10, 10, 10, 10)] // nothing elapsed
    [InlineData(50, 100, 100, 40, 200, 200)] // idle went backwards
    [InlineData(0, 100, 100, 0, 90, 90)] // a counter went backwards
    public void S1142_a_degenerate_delta_reads_zero(long i0, long k0, long u0, long i1, long k1, long u1)
        => Assert.Equal(0, CpuRules.Load(new CoreTimes(i0, k0, u0), new CoreTimes(i1, k1, u1)));

    [Fact]
    public void S1142_idle_exceeding_elapsed_is_clamped_not_negative()
        => Assert.Equal(0, CpuRules.Load(new CoreTimes(0, 0, 0), new CoreTimes(500, 100, 0)));

    [Fact]
    public void S1142_a_changed_processor_count_gives_no_answer_for_the_tick()
    {
        Assert.Null(CpuRules.Loads([new CoreTimes(0, 0, 0)], [new CoreTimes(0, 10, 0), new CoreTimes(0, 10, 0)]));
        Assert.Null(CpuRules.Loads([], []));
        Assert.Equal([1.0, 0.5], CpuRules.Loads(
            [new CoreTimes(0, 0, 0), new CoreTimes(0, 0, 0)],
            [new CoreTimes(0, 10, 0), new CoreTimes(5, 10, 0)])!);
    }

    // ---- S-1141 clock -------------------------------------------------------------------------

    [Theory]
    [InlineData(120.0, 3600, 3600, 4320.0)] // turbo: % Processor Performance over 100
    [InlineData(null, 3600, 2900, 2900.0)] // no counter: the reported clock
    [InlineData(0.0, 3600, 2900, 2900.0)] // a first rate sample of 0 is "no data"
    [InlineData(double.NaN, 3600, 2900, 2900.0)]
    [InlineData(110.0, 0, 2900, 2900.0)] // no base clock to scale
    public void S1141_effective_clock(double? performance, double baseMhz, double reported, double expected)
        => Assert.Equal(expected, CpuRules.EffectiveMhz(performance, baseMhz, reported)!.Value, 6);

    [Fact]
    public void S1141_no_source_means_no_clock()
        => Assert.Null(CpuRules.EffectiveMhz(null, 3600, 0));

    [Theory]
    [InlineData(4720, "4.72 GHz")]
    [InlineData(800, "0.80 GHz")]
    [InlineData(3999.6, "4.00 GHz")]
    public void S1141_clock_is_gigahertz_to_two_decimals(double mhz, string expected)
        => Assert.Equal(expected, CpuRules.Gigahertz(mhz));

    // ---- S-1143 uptime ------------------------------------------------------------------------

    [Theory]
    [InlineData(0, 0, 0, "0m")]
    [InlineData(0, 0, 59, "59m")]
    [InlineData(0, 1, 0, "1h 0m")]
    [InlineData(0, 23, 59, "23h 59m")]
    [InlineData(1, 0, 0, "1d 0h")]
    [InlineData(3, 4, 59, "3d 4h")]
    [InlineData(400, 2, 0, "400d 2h")]
    public void S1143_uptime_in_its_two_largest_units(int days, int hours, int minutes, string expected)
        => Assert.Equal(expected, CpuRules.Uptime(new TimeSpan(days, hours, minutes, 30)));

    [Fact]
    public void S1143_a_negative_span_reads_zero()
        => Assert.Equal("0m", CpuRules.Uptime(TimeSpan.FromMinutes(-5)));

    // ---- S-1144 totals ------------------------------------------------------------------------

    [Fact]
    public void S1144_totals_group_thousands()
        => Assert.Equal("312 processes · 4,210 threads", CpuRules.Totals(312, 4210));

    // ---- S-1146 / S-1147 sensors --------------------------------------------------------------

    [Theory]
    [InlineData(273.15, 0.0)]
    [InlineData(323.15, 50.0)]
    [InlineData(423.15, 150.0)]
    public void S1146_kelvin_to_celsius(double kelvin, double celsius)
        => Assert.Equal(celsius, CpuRules.Celsius(kelvin)!.Value, 6);

    [Theory]
    [InlineData(0)] // an unset zone
    [InlineData(250)] // below freezing: a broken probe
    [InlineData(500)] // 227 °C
    [InlineData(double.NaN)]
    public void S1146_implausible_zones_are_rejected(double kelvin)
        => Assert.Null(CpuRules.Celsius(kelvin));

    [Fact]
    public void S1146_the_hottest_plausible_zone_wins()
    {
        Assert.Equal(60, CpuRules.HottestZone([300, 333.15, 0, 900])!.Value, 6);
        Assert.Null(CpuRules.HottestZone([]));
        Assert.Null(CpuRules.HottestZone([0, 1000]));
    }

    [Fact]
    public void S1146_no_na_flash_before_the_first_poll()
    {
        Assert.Null(CpuRules.TemperatureText(polled: false, null));
        Assert.Null(CpuRules.TemperatureText(polled: false, 50));
        Assert.Equal("N/A", CpuRules.TemperatureText(polled: true, null));
        Assert.Equal("N/A", CpuRules.TemperatureText(polled: true, 400));
        Assert.Equal("53 °C", CpuRules.TemperatureText(polled: true, 52.5));
    }

    [Fact]
    public void S1147_fan_reads_rpm_or_na_and_never_flashes()
    {
        Assert.Null(CpuRules.FanText(polled: false, 1200));
        Assert.Equal("N/A", CpuRules.FanText(polled: true, null));
        Assert.Equal("N/A", CpuRules.FanText(polled: true, 0));
        Assert.Equal("N/A", CpuRules.FanText(polled: true, 70000));
        Assert.Equal("1,180 RPM", CpuRules.FanText(polled: true, 1180));
    }

    private const string MonitorTree = """
        {"id":0,"Text":"Sensor","Value":"","Children":[
          {"id":1,"Text":"DESKTOP","Value":"","ImageURL":"images_icon/computer.png","Children":[
            {"id":2,"Text":"ASUS PRIME","Value":"","ImageURL":"images_icon/mainboard.png","Children":[
              {"id":3,"Text":"Nuvoton","Value":"","ImageURL":"images_icon/chip.png","Children":[
                {"id":4,"Text":"Fans","Value":"","ImageURL":"images_icon/fan.png","Children":[
                  {"id":5,"Text":"CPU Fan","Value":"1180 RPM","ImageURL":"images/transparent.png","Children":[]},
                  {"id":6,"Text":"Chassis Fan #1","Value":"2400 RPM","ImageURL":"images/transparent.png","Children":[]}]},
                {"id":7,"Text":"Temperatures","Value":"","Children":[
                  {"id":8,"Text":"System","Value":"88,0 °C","Children":[]}]}]}]},
            {"id":9,"Text":"AMD Ryzen 7 5800X","Value":"","ImageURL":"images_icon/cpu.png","Children":[
              {"id":10,"Text":"Temperatures","Value":"","ImageURL":"images_icon/temperature.png","Children":[
                {"id":11,"Text":"Core (Tctl/Tdie)","Value":"61,4 °C","Children":[]},
                {"id":12,"Text":"CCD1 (Tdie)","Value":"58,0 °C","Children":[]},
                {"id":13,"Text":"Core #1","Value":"72,0 °C","Children":[]}]}]}]}]}
        """;

    [Fact]
    public void S1146_monitor_prefers_the_package_reading_inside_the_processor()
    {
        var readout = HardwareMonitor.Parse(MonitorTree);
        Assert.Equal(61.4, readout.Celsius!.Value, 6);
    }

    [Fact]
    public void S1147_monitor_fan_is_the_one_named_for_the_processor()
        => Assert.Equal(1180, HardwareMonitor.Parse(MonitorTree).FanRpm!.Value, 6);

    [Fact]
    public void S1146_without_a_package_sensor_the_hottest_core_wins()
    {
        const string json = """
            {"Text":"Sensor","Children":[{"Text":"Intel Core i7","ImageURL":"images_icon/cpu.png","Children":[
              {"Text":"Core #1","Value":"55.0 °C"},{"Text":"Core #2","Value":"63.5 °C"},{"Text":"Core #3","Value":"200.0 °C"}]}]}
            """;

        Assert.Equal(63.5, HardwareMonitor.Parse(json).Celsius!.Value, 6);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{\"Text\":\"Sensor\"}")]
    [InlineData("{\"Children\":[{\"Text\":\"GPU Core\",\"Value\":\"60 °C\",\"ImageURL\":\"images_icon/nvidia.png\"}]}")]
    public void S1146_a_document_without_processor_sensors_reads_nothing(string json)
        => Assert.Equal(default, HardwareMonitor.Parse(json));

    [Fact]
    public void S1146_outside_a_processor_node_only_a_probe_named_cpu_counts()
    {
        const string json = """
            {"Children":[{"Text":"Board","ImageURL":"images_icon/mainboard.png","Children":[
              {"Text":"CPU","Value":"48 °C"},{"Text":"System","Value":"70 °C"}]}]}
            """;

        Assert.Equal(48, HardwareMonitor.Parse(json).Celsius!.Value, 6);
    }

    [Theory]
    [InlineData("45,5 °C", "°C", 45.5)]
    [InlineData(" 1200 RPM ", "RPM", 1200.0)]
    [InlineData("45.5 °C", "RPM", null)]
    [InlineData("abc °C", "°C", null)]
    public void S1146_values_carry_their_unit_in_either_decimal_style(string value, string unit, double? expected)
        => Assert.Equal(expected, HardwareMonitor.Number(value, unit));

    // ---- S-1149 config ------------------------------------------------------------------------

    [Fact]
    public void S1149_defaults_show_what_always_has_data_and_leave_sensors_off()
    {
        var cpu = new CpuConfig();
        Assert.True(cpu.ShowFrequency && cpu.ShowCores && cpu.ShowUptime && cpu.ShowProcessCount && cpu.ShowTopProcesses);
        Assert.False(cpu.ShowTemperature || cpu.ShowFan);
        Assert.Equal(CpuConfig.MaxTopProcesses, cpu.TopProcessCount);
    }

    [Fact]
    public void S1149_clone_and_repair_keep_every_metric()
    {
        var cpu = new CpuConfig { ShowFrequency = false, ShowCores = false, ShowTemperature = true, ShowFan = true, TopProcessCount = 99 };
        var notes = cpu.Repair();

        Assert.Single(notes);
        Assert.Equal(CpuConfig.MaxTopProcesses, cpu.TopProcessCount);

        var copy = (CpuConfig)cpu.Clone();
        Assert.False(copy.ShowFrequency || copy.ShowCores);
        Assert.True(copy.ShowTemperature && copy.ShowFan);
    }

    // ---- S-1150 per-core strip ----------------------------------------------------------------

    [Theory]
    [InlineData(8, 168, 8, 1)] // a tall column: one row
    [InlineData(16, 168, 16, 1)]
    [InlineData(64, 168, 22, 3)] // 28 fit a row: three balanced rows
    [InlineData(20, 94, 10, 2)] // 16 fit a row: 10 + 10, not 16 + 4
    [InlineData(1, 10, 1, 1)]
    public void S1150_bars_fill_balanced_rows(int count, double width, int columns, int rows)
    {
        var grid = MeterCellGrid.Fit(count, width, minCell: 4, gap: 2, labelWidth: 30);
        Assert.Equal((columns, rows), (grid.Columns, grid.Rows));
    }

    [Theory]
    [InlineData(16, 528, true)] // L: 31 px a bar
    [InlineData(16, 368, false)] // XL panel: 21 px
    public void S1150_figures_only_where_every_bar_holds_one(int count, double width, bool labels)
        => Assert.Equal(labels, MeterCellGrid.Fit(count, width, 4, 2, 30).Labels);

    [Theory]
    [InlineData(0, 100)]
    [InlineData(8, 0)]
    [InlineData(8, double.NaN)]
    public void S1150_nothing_to_lay_out_is_empty(int count, double width)
        => Assert.True(MeterCellGrid.Fit(count, width, 4, 2, 30).IsEmpty);

    // ---- S-1151 / S-1152 processes ------------------------------------------------------------

    private static readonly ProcessColumnOptions All = new(ProcessSort.Cpu, Memory: true, Threads: true, Pid: true);

    [Theory]
    [InlineData(88, ProcessColumn.Cpu)] // XS: the ranked figure even when the name is short
    [InlineData(231, ProcessColumn.Cpu)] // the [S-648] boundary, one short
    [InlineData(232, ProcessColumn.Cpu | ProcessColumn.Memory)] // the [S-648] boundary
    [InlineData(292, ProcessColumn.Cpu | ProcessColumn.Memory | ProcessColumn.Threads)]
    [InlineData(352, ProcessColumn.Cpu | ProcessColumn.Memory | ProcessColumn.Threads | ProcessColumn.Pid)]
    public void S1152_columns_join_in_order_as_the_width_allows(double width, ProcessColumn expected)
        => Assert.Equal(expected, ProcessTable.Columns(width, All));

    [Fact]
    public void S1152_ranked_by_memory_the_memory_column_comes_first()
        => Assert.Equal(ProcessColumn.Memory, ProcessTable.Columns(120, All with { Sort = ProcessSort.Memory }));

    [Fact]
    public void S1152_a_column_turned_off_is_skipped_and_the_ladder_goes_on()
        => Assert.Equal(
            ProcessColumn.Cpu | ProcessColumn.Pid,
            ProcessTable.Columns(800, new ProcessColumnOptions(ProcessSort.Cpu, Memory: false, Threads: false, Pid: true)));

    [Fact]
    public void S1151_row_ceiling_defaults_above_what_any_tile_fits_and_is_repaired()
    {
        Assert.Equal(20, new ProcessesConfig().MaxCount);

        var config = new ProcessesConfig { MaxCount = 0 };
        Assert.Single(config.Repair());
        Assert.Equal(1, config.MaxCount);
        Assert.Empty(new ProcessesConfig().Repair());

        var copy = (ProcessesConfig)new ProcessesConfig { ShowThreads = false, ShowPid = false, ShowTotals = false }.Clone();
        Assert.False(copy.ShowThreads || copy.ShowPid || copy.ShowTotals);
    }

    [Theory]
    [InlineData(76, 2)] // XS slot
    [InlineData(356, 11)] // Tall slot
    [InlineData(516, 16)] // XL slot
    public void S1151_rows_are_whole_rows_of_the_slot(double height, int rows)
        => Assert.Equal(rows, Disclosure.WholeRows(height, 32));
}
