using Themia.Core.Geometry;
using Xunit;

namespace Themia.Core.Tests;

public class GridGeometryTests
{
    private static readonly ScreenMetrics Screen = new(1920, 1080);

    [Fact]
    public void S09_clamps_a_widget_fully_on_screen()
    {
        var offRight = new FracRect(0.9, 0.9, 0.3, 0.3);

        var clamped = GridGeometry.Clamp(offRight, Screen);

        Assert.True(clamped.X >= 0);
        Assert.True(clamped.Y >= 0);
        Assert.True(clamped.Right <= 1.0000001);
        Assert.True(clamped.Bottom <= 1.0000001);
    }

    [Fact]
    public void S09_clamp_collapses_non_finite_input_instead_of_storing_nan()
    {
        var poisoned = new FracRect(double.NaN, 0.2, double.PositiveInfinity, 0.2);

        var clamped = GridGeometry.Clamp(poisoned, Screen);

        Assert.True(clamped.IsFinite);
        Assert.True(clamped.W > 0);
        Assert.True(clamped.H > 0);
    }

    [Fact]
    public void S08_clamp_lifts_a_sub_pixel_floor_widget_back_to_the_minimum()
    {
        var tiny = new FracRect(0.5, 0.5, 0.0001, 0.0001);

        var clamped = GridGeometry.Clamp(tiny, Screen);

        Assert.True(clamped.W * Screen.Width >= GridGeometry.MinWidgetPx - 0.001);
        Assert.True(clamped.H * Screen.Height >= GridGeometry.MinWidgetPx - 0.001);
    }

    [Fact]
    public void S06_snap_rounds_onto_the_grid_and_is_idempotent()
    {
        var raw = new FracRect(0.123, 0.456, 0.2345, 0.3456);

        var once = GridGeometry.Snap(raw, Screen);
        var twice = GridGeometry.Snap(once, Screen);

        Assert.Equal(once, twice);
    }

    [Fact]
    public void S06_settle_without_snap_keeps_free_fractional_positioning()
    {
        var raw = new FracRect(0.1234, 0.5678, 0.2, 0.2);

        var free = GridGeometry.Settle(raw, Screen, snapEnabled: false);

        Assert.Equal(0.1234, free.X, 6);
        Assert.Equal(0.5678, free.Y, 6);
    }

    [Theory]
    [InlineData(ResizeHandle.Left)]
    [InlineData(ResizeHandle.Right)]
    [InlineData(ResizeHandle.Top)]
    [InlineData(ResizeHandle.Bottom)]
    public void S31_a_side_handle_moves_only_its_own_edge(ResizeHandle handle)
    {
        var start = new FracRect(0.3, 0.3, 0.3, 0.3);

        var resized = GridGeometry.Resize(start, handle, 0.05, 0.05, Screen);

        if (!GridGeometry.MovesLeft(handle))
        {
            Assert.Equal(start.X, resized.X, 9);
        }

        if (!GridGeometry.MovesTop(handle))
        {
            Assert.Equal(start.Y, resized.Y, 9);
        }

        if (!GridGeometry.MovesRight(handle))
        {
            Assert.Equal(start.Right, resized.Right, 9);
        }

        if (!GridGeometry.MovesBottom(handle))
        {
            Assert.Equal(start.Bottom, resized.Bottom, 9);
        }
    }

    [Fact]
    public void S31_dragging_an_edge_past_its_opposite_stops_at_the_floor_instead_of_inverting()
    {
        var start = new FracRect(0.3, 0.3, 0.2, 0.2);

        // Drag the left edge far past the right edge.
        var resized = GridGeometry.Resize(start, ResizeHandle.Left, 5.0, 0, Screen);

        Assert.True(resized.W > 0);
        Assert.Equal(start.Right, resized.Right, 9);
        Assert.True(resized.W * Screen.Width >= GridGeometry.MinWidgetPx - 0.001);
    }

    [Fact]
    public void S31_a_corner_handle_anchors_the_opposite_corner()
    {
        var start = new FracRect(0.2, 0.2, 0.3, 0.3);

        var resized = GridGeometry.Resize(start, ResizeHandle.TopLeft, 0.05, 0.05, Screen);

        Assert.Equal(start.Right, resized.Right, 9);
        Assert.Equal(start.Bottom, resized.Bottom, 9);
        Assert.Equal(0.25, resized.X, 9);
        Assert.Equal(0.25, resized.Y, 9);
    }

    [Fact]
    public void S02_the_same_fraction_is_proportionally_larger_on_a_larger_screen()
    {
        var rect = new FracRect(0.25, 0.25, 0.5, 0.5);
        var small = new ScreenMetrics(1280, 720);
        var large = new ScreenMetrics(3840, 2160);

        var onSmall = GridGeometry.ToPixels(rect, small, gapPx: 0);
        var onLarge = GridGeometry.ToPixels(rect, large, gapPx: 0);

        Assert.Equal(onLarge.Width / onSmall.Width, large.Width / small.Width, 3);
        Assert.Equal(onLarge.X / onSmall.X, large.Width / small.Width, 3);
    }

    [Fact]
    public void S10_a_default_pixel_size_lands_the_same_physical_size_on_any_monitor()
    {
        var small = new ScreenMetrics(1280, 720);
        var large = new ScreenMetrics(3840, 2160);

        var onSmall = GridGeometry.PlaceAt(0.5, 0.5, 320, 160, small, snapEnabled: false);
        var onLarge = GridGeometry.PlaceAt(0.5, 0.5, 320, 160, large, snapEnabled: false);

        Assert.Equal(320, onSmall.W * small.Width, 1);
        Assert.Equal(320, onLarge.W * large.Width, 1);
    }

    [Fact]
    public void S04_neighbouring_widgets_meet_exactly_with_no_gap()
    {
        var left = new FracRect(0.0, 0.0, 0.5, 1.0);
        var right = new FracRect(0.5, 0.0, 0.5, 1.0);

        var l = GridGeometry.ToPixels(left, Screen, gapPx: 0);
        var r = GridGeometry.ToPixels(right, Screen, gapPx: 0);

        Assert.Equal(l.X + l.Width, r.X, 9);
    }

    [Fact]
    public void S04_the_gap_insets_each_side_by_half_and_never_moves_the_logical_rect()
    {
        var rect = new FracRect(0.25, 0.25, 0.5, 0.5);

        var noGap = GridGeometry.ToPixels(rect, Screen, gapPx: 0);
        var gapped = GridGeometry.ToPixels(rect, Screen, gapPx: 16);

        Assert.Equal(noGap.X + 8, gapped.X, 9);
        Assert.Equal(noGap.Width - 16, gapped.Width, 9);
    }

    [Fact]
    public void S11_placement_is_centred_on_the_drop_point()
    {
        var placed = GridGeometry.PlaceAt(0.5, 0.5, 320, 160, Screen, snapEnabled: false);

        Assert.Equal(0.5, placed.X + (placed.W / 2), 6);
        Assert.Equal(0.5, placed.Y + (placed.H / 2), 6);
    }

    [Fact]
    public void S11_placement_at_a_screen_corner_is_still_fully_on_screen()
    {
        var placed = GridGeometry.PlaceAt(0.0, 0.0, 320, 160, Screen, snapEnabled: true);

        Assert.True(placed.X >= 0);
        Assert.True(placed.Y >= 0);
        Assert.True(placed.Right <= 1.0000001);
        Assert.True(placed.Bottom <= 1.0000001);
    }

    [Fact]
    public void GridDims_never_returns_a_degenerate_grid_for_a_degenerate_screen()
    {
        var (cols, rows) = GridGeometry.GridDims(new ScreenMetrics(0, 0));

        Assert.True(cols >= 1);
        Assert.True(rows >= 1);
    }
}
