using Themia.Core.Model;
using Themia.Core.Widgets;
using Xunit;

namespace Themia.Core.Tests;

/// <summary>The processor meter's extras and the process list's ladder (S-1140..S-1159).</summary>
public sealed class CpuProcessesTests
{
    // ---- S-1142 per-core load ----------------------------------------------------------------

    // ---- S-1141 clock -------------------------------------------------------------------------

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

    // ---- S-1149 config ------------------------------------------------------------------------

    // ---- S-1150 per-core strip ----------------------------------------------------------------

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
