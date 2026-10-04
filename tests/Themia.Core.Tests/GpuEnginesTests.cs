using Themia.Core.Model;
using Themia.Core.Widgets;
using Xunit;

namespace Themia.Core.Tests;

public sealed class GpuEnginesTests
{
    private const string Luid = "luid_0x00000000_0x0000abcd";
    private const string OtherLuid = "luid_0x00000000_0x00001234";

    private static string Engine(int pid, int engine, string type, string luid = Luid)
        => $"pid_{pid}_{luid}_phys_0_eng_{engine}_engtype_{type}";

    // ---- [S-1203] engine kinds ---------------------------------------------------------------

    [Theory]
    [InlineData("3D", GpuEngineKind.Graphics)]
    [InlineData("Graphics_1", GpuEngineKind.Graphics)]
    [InlineData("High Priority 3D", GpuEngineKind.Graphics)]
    [InlineData("Compute_0", GpuEngineKind.Compute)]
    [InlineData("Cuda", GpuEngineKind.Compute)]
    [InlineData("High Priority Compute", GpuEngineKind.Compute)]
    [InlineData("Copy", GpuEngineKind.Copy)]
    [InlineData("VideoDecode", GpuEngineKind.VideoDecode)]
    [InlineData("VideoEncode", GpuEngineKind.VideoEncode)]
    [InlineData("VideoProcessing", GpuEngineKind.VideoProcessing)]
    public void S1203_engine_types_map_onto_the_kinds_vendors_spell_differently(string type, GpuEngineKind kind)
    {
        Assert.Equal(kind, GpuEngines.KindOf(Engine(1, 0, type)));
    }

    [Theory]
    [InlineData("pid_1_luid_0x0_0x1_phys_0_eng_0_engtype_Security")]
    [InlineData("pid_1_luid_0x0_0x1_phys_0_eng_0")]
    [InlineData("")]
    public void S1203_unknown_or_missing_types_are_not_drawn(string instance)
    {
        Assert.Null(GpuEngines.KindOf(instance));
    }

    [Fact]
    public void S1203_a_kind_is_its_busiest_engine_summed_across_processes()
    {
        var loads = GpuEngines.Loads(
            [
                (Engine(10, 0, "3D"), 20),
                (Engine(11, 0, "3D"), 15),     // engine 0: 35
                (Engine(10, 1, "3D"), 30),     // engine 1: 30
                (Engine(10, 2, "Copy"), 4),
                (Engine(10, 3, "VideoDecode"), 0),
                (Engine(10, 0, "3D", OtherLuid), 90),
            ],
            Luid);

        Assert.Equal(
            [
                new GpuEngineLoad(GpuEngineKind.Graphics, 35),
                new GpuEngineLoad(GpuEngineKind.Copy, 4),
                new GpuEngineLoad(GpuEngineKind.VideoDecode, 0),
            ],
            loads);
    }

    [Fact]
    public void S1203_junk_readings_count_as_nothing_and_loads_clamp()
    {
        var loads = GpuEngines.Loads(
            [
                (Engine(10, 0, "3D"), double.NaN),
                (Engine(11, 0, "3D"), -5),
                (Engine(10, 1, "Compute_0"), 80),
                (Engine(11, 1, "Compute_0"), 70),
            ],
            Luid);

        Assert.Equal(
            [new GpuEngineLoad(GpuEngineKind.Graphics, 0), new GpuEngineLoad(GpuEngineKind.Compute, 100)],
            loads);
    }

    // ---- [S-1204] the driver's video figures ---------------------------------------------------

    [Fact]
    public void S1204_driver_video_figures_replace_or_join_the_counters()
    {
        var merged = GpuEngines.WithDriverVideo(
            [new GpuEngineLoad(GpuEngineKind.Graphics, 10), new GpuEngineLoad(GpuEngineKind.VideoDecode, 2)],
            encoder: 140,
            decoder: 7);

        Assert.Equal(
            [
                new GpuEngineLoad(GpuEngineKind.Graphics, 10),
                new GpuEngineLoad(GpuEngineKind.VideoDecode, 7),
                new GpuEngineLoad(GpuEngineKind.VideoEncode, 100),
            ],
            merged);
    }

    [Fact]
    public void S1204_no_driver_leaves_the_counters_alone()
    {
        GpuEngineLoad[] counters = [new(GpuEngineKind.VideoEncode, 3)];

        Assert.Equal(counters, GpuEngines.WithDriverVideo(counters, null, double.NaN));
    }

    // ---- [S-1205] processes ----------------------------------------------------------------------

    [Theory]
    [InlineData("pid_1234_luid_0x0_0x1_phys_0_eng_0_engtype_3D", 1234)]
    [InlineData("pid_7", 7)]
    [InlineData("luid_0x0_0x1_phys_0", null)]
    [InlineData("pid_x_luid", null)]
    public void S1205_the_pid_is_read_from_the_front_of_the_name(string instance, int? pid)
    {
        Assert.Equal(pid, GpuEngines.PidOf(instance));
    }

    [Fact]
    public void S1205_a_process_is_its_busiest_engine_busiest_first_idle_and_pid_0_left_out()
    {
        var top = GpuEngines.TopProcesses(
            [
                (Engine(10, 0, "3D"), 20),
                (Engine(10, 2, "VideoDecode"), 45),
                (Engine(11, 0, "3D"), 30),
                (Engine(12, 0, "3D"), 30),
                (Engine(13, 0, "3D"), 0),
                (Engine(0, 0, "3D"), 60),
                (Engine(14, 0, "3D", OtherLuid), 99),
            ],
            Luid,
            count: 3);

        Assert.Equal([(10, 45.0), (11, 30.0), (12, 30.0)], top);
    }

    [Fact]
    public void S1205_no_room_means_no_processes()
    {
        Assert.Empty(GpuEngines.TopProcesses([(Engine(10, 0, "3D"), 20)], Luid, 0));
    }

    // ---- [S-1206] the caption ----------------------------------------------------------------

    [Fact]
    public void S1206_caption_names_the_busiest_engine_and_on_a_large_tile_its_own_figure()
    {
        GpuEngineLoad[] loads = [new(GpuEngineKind.Graphics, 3), new(GpuEngineKind.VideoDecode, 12.44)];

        Assert.Equal("Video decode", GpuEngines.Caption(loads, precise: false));
        Assert.Equal("Video decode 12.4%", GpuEngines.Caption(loads, precise: true));
    }

    [Fact]
    public void S1206_idle_engines_name_nothing()
    {
        GpuEngineLoad[] idle = [new(GpuEngineKind.Graphics, 0)];

        Assert.Equal(string.Empty, GpuEngines.Caption(idle, precise: false));
        Assert.Equal(string.Empty, GpuEngines.Caption(idle, precise: true));
        Assert.Equal(string.Empty, GpuEngines.Caption([], precise: true));
    }

    [Fact]
    public void S1206_the_layout_budget_covers_every_caption()
    {
        foreach (var kind in Enum.GetValues<GpuEngineKind>())
        {
            GpuEngineLoad[] loads = [new(kind, 100)];
            Assert.True(GpuEngines.Caption(loads, precise: true).Length <= GpuEngines.CaptionCharacters(precise: true));
            Assert.True(GpuEngines.Caption(loads, precise: false).Length <= GpuEngines.CaptionCharacters(precise: false));
        }
    }

    // ---- [S-1202] / [S-1207] arrangement --------------------------------------------------------

    [Theory]
    [InlineData(120, 76, GpuArrangement.Stack)]    // XS
    [InlineData(200, 106, GpuArrangement.Stack)]   // S
    [InlineData(320, 196, GpuArrangement.Stack)]   // M
    [InlineData(480, 116, GpuArrangement.Side)]    // Wide
    [InlineData(200, 356, GpuArrangement.Stack)]   // Tall
    [InlineData(560, 376, GpuArrangement.Panels)]  // L
    [InlineData(800, 516, GpuArrangement.Panels)]  // XL
    [InlineData(800, 252, GpuArrangement.Panels)]  // one of two adapters on XL
    [InlineData(560, 182, GpuArrangement.Panels)]  // one of two adapters on L
    [InlineData(400, 127, GpuArrangement.Side)]    // a strip just under Regular height
    public void S1202_each_gallery_size_has_its_arrangement(double width, double height, GpuArrangement expected)
    {
        Assert.Equal(expected, GpuLayout.Arrange(new WidgetArea(width, height)));
    }

    [Fact]
    public void S1202_an_unmeasured_area_stacks()
    {
        Assert.Equal(GpuArrangement.Stack, GpuLayout.Arrange(default));
    }

    [Theory]
    [InlineData(480, 116, 2, 2, 1)]  // a strip: side by side, not two slivers
    [InlineData(120, 76, 2, 1, 2)]   // XS: two "name  number" lines, not two 54 px columns
    [InlineData(200, 106, 2, 1, 2)]
    [InlineData(320, 196, 2, 1, 2)]
    [InlineData(200, 356, 2, 1, 2)]  // a column: stacked
    [InlineData(560, 376, 2, 1, 2)]
    [InlineData(800, 516, 2, 1, 2)]
    [InlineData(800, 516, 3, 1, 3)]
    [InlineData(800, 300, 4, 2, 2)]
    [InlineData(120, 76, 1, 1, 1)]
    public void S1207_adapters_share_the_tile_without_scrolling(double width, double height, int count, int columns, int rows)
    {
        Assert.Equal((columns, rows), GpuLayout.Grid(new WidgetArea(width, height), count, 12));
    }

    [Fact]
    public void S1207_cells_split_the_area_less_the_gaps()
    {
        var cell = GpuLayout.Cell(new WidgetArea(500, 200), 2, 1, 12);

        Assert.Equal(244, cell.Width);
        Assert.Equal(200, cell.Height);
    }

    // ---- [S-1208] config ---------------------------------------------------------------------------

    [Fact]
    public void S1208_driver_lines_engines_and_processes_default_on_and_clone()
    {
        var config = new GpuConfig();

        Assert.True(config.ShowTemperature && config.ShowPower && config.ShowFan && config.ShowClocks);
        Assert.True(config.ShowEngines && config.ShowProcesses);

        config.ShowEngines = false;
        config.ShowProcesses = false;
        var copy = (GpuConfig)config.Clone();

        Assert.False(copy.ShowEngines);
        Assert.False(copy.ShowProcesses);
    }

    [Fact]
    public void S1200_the_chart_holds_the_span_at_the_refresh()
    {
        var config = new GpuConfig { GraphSpanSeconds = 300, IntervalSeconds = 2 };

        Assert.Equal(150, config.GraphCapacity);
    }
}
