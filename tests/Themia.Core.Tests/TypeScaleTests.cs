using Themia.Core.Widgets;
using Xunit;

namespace Themia.Core.Tests;

public class TypeScaleTests
{
    [Fact]
    public void Text_grows_with_the_widget()
    {
        var small = TypeScale.Fit(200, 120, 0.34, 0.42, 14, 200);
        var large = TypeScale.Fit(600, 360, 0.34, 0.42, 14, 200);

        Assert.True(large > small, "a bigger tile must get bigger text");
    }

    [Fact]
    public void Scaling_is_proportional_between_the_clamps()
    {
        var single = TypeScale.Fit(200, 200, 0.5, 0.5, 1, 1000);
        var doubled = TypeScale.Fit(400, 400, 0.5, 0.5, 1, 1000);

        Assert.Equal(single * 2, doubled, 6);
    }

    [Fact]
    public void The_tighter_axis_wins()
    {
        // Wide but short: height must decide, or the text is clipped top and bottom.
        var wideShort = TypeScale.Fit(1000, 100, 0.34, 0.42, 8, 500);
        Assert.Equal(100 * 0.42, wideShort, 6);

        // Tall but narrow: width must decide, or it overflows sideways.
        var tallNarrow = TypeScale.Fit(100, 1000, 0.34, 0.42, 8, 500);
        Assert.Equal(100 * 0.34, tallNarrow, 6);
    }

    [Fact]
    public void A_tiny_widget_still_gets_readable_text()
    {
        var tiny = TypeScale.Fit(20, 10, 0.34, 0.42, 12, 200);

        Assert.Equal(12, tiny);
    }

    [Fact]
    public void A_huge_widget_does_not_get_absurd_text()
    {
        var huge = TypeScale.Fit(4000, 3000, 0.34, 0.42, 12, 120);

        Assert.Equal(120, huge);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-50)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void An_unmeasured_or_degenerate_box_falls_back_to_the_floor(double width)
    {
        // A NaN reaching FontSize throws at render time, so this must never return one.
        var size = TypeScale.Fit(width, 100, 0.34, 0.42, 13, 200);

        Assert.Equal(13, size);
        Assert.True(double.IsFinite(size));
    }

    [Fact]
    public void Reversed_clamps_are_tolerated_rather_than_inverting_the_result()
    {
        var size = TypeScale.Fit(300, 300, 0.5, 0.5, max: 10, min: 40);

        Assert.InRange(size, 10, 40);
    }

    [Fact]
    public void FitWidth_ignores_height_entirely()
    {
        var shortBox = TypeScale.FitWidth(400, 0.25, 8, 500);
        var tallBox = TypeScale.FitWidth(400, 0.25, 8, 500);

        Assert.Equal(100, shortBox, 6);
        Assert.Equal(shortBox, tallBox);
    }

    [Fact]
    public void The_result_is_always_finite_and_positive()
    {
        foreach (var width in new[] { 0d, 1, 50, 1000, double.NaN })
        {
            foreach (var height in new[] { 0d, 1, 50, 1000, double.NaN })
            {
                var size = TypeScale.Fit(width, height, 0.34, 0.42, 11, 96);

                Assert.True(double.IsFinite(size));
                Assert.True(size > 0);
            }
        }
    }
}
