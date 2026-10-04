using Themia.Core.Widgets;
using Xunit;

namespace Themia.Core.Tests;

/// <summary>
/// The sparkline x-axis. Every case here is the same defect from a different angle: a graph that
/// rescales horizontally while it fills is reporting its own buffer length, not the data.
/// </summary>
public sealed class SparklineTests
{
    private const int Capacity = 60;

    [Fact]
    public void S111_NewestSampleIsAlwaysAtTheRightEdge()
    {
        foreach (var count in new[] { 2, 5, 30, Capacity })
        {
            Assert.Equal(1.0, Sparkline.SampleX(count - 1, count, Capacity), 9);
        }
    }

    /// <summary>The whole point: adding a sample must not move the ones already drawn.</summary>
    [Fact]
    public void S111_OlderSamplesDoNotMoveAsTheWindowFills()
    {
        // The sample two slots back from newest is two slots back from the right, always.
        var atTen = Sparkline.SampleX(7, 10, Capacity);
        var atForty = Sparkline.SampleX(37, 40, Capacity);

        Assert.Equal(atTen, atForty, 9);
    }

    [Fact]
    public void S111_OneFullWindowSpansTheWholeWidth()
    {
        Assert.Equal(0.0, Sparkline.SampleX(0, Capacity, Capacity), 9);
        Assert.Equal(1.0, Sparkline.SampleX(Capacity - 1, Capacity, Capacity), 9);
    }

    /// <summary>
    /// Two samples occupy one slot at the right, not the whole width — that misreading is what made
    /// a just-started graph look like a full minute of history.
    /// </summary>
    [Fact]
    public void S111_TwoSamplesOccupyOneSlot()
    {
        var newest = Sparkline.SampleX(1, 2, Capacity);
        var older = Sparkline.SampleX(0, 2, Capacity);

        Assert.Equal(1.0, newest, 9);
        Assert.Equal(1.0 - (1.0 / (Capacity - 1)), older, 9);
    }

    [Fact]
    public void S111_SpacingIsOneSlotRegardlessOfHowManyAreHeld()
    {
        var step = 1.0 / (Capacity - 1);

        Assert.Equal(step, Sparkline.SampleX(9, 10, Capacity) - Sparkline.SampleX(8, 10, Capacity), 9);
        Assert.Equal(step, Sparkline.SampleX(1, 2, Capacity) - Sparkline.SampleX(0, 2, Capacity), 9);
    }

    /// <summary>A buffer briefly longer than its window must not emit a negative x.</summary>
    [Fact]
    public void S111_OverfullBufferClampsToTheLeftEdge()
        => Assert.Equal(0.0, Sparkline.SampleX(0, Capacity + 20, Capacity), 9);

    [Fact]
    public void S111_DegenerateCapacityStillProducesALine()
    {
        Assert.Equal(0.0, Sparkline.SampleX(0, 2, 0), 9);
        Assert.Equal(1.0, Sparkline.SampleX(1, 2, 1), 9);
    }

    [Theory]
    [InlineData(0.0, 1.0)]
    [InlineData(1.0, 0.0)]
    [InlineData(0.25, 0.75)]
    [InlineData(-5, 1.0)]
    [InlineData(5, 0.0)]
    public void S111_YIsInvertedAndClamped(double fraction, double expected)
        => Assert.Equal(expected, Sparkline.SampleY(fraction), 9);
}
