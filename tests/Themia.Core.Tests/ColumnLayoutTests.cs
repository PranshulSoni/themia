using Themia.Core.Widgets;
using Xunit;

namespace Themia.Core.Tests;

/// <summary>
/// The widget picker's column split. Every case guards one of three promises: reading order is
/// kept, a section is never torn in half, and the tallest column is as short as it can be.
/// </summary>
public sealed class ColumnLayoutTests
{
    [Fact]
    public void S420_NoSectionsGiveNoColumns()
    {
        Assert.Empty(ColumnLayout.Partition([], 4));
    }

    [Fact]
    public void S420_OneColumnHoldsEverything()
    {
        Assert.Equal([0, 0, 0], ColumnLayout.Partition([3, 5, 2], 1));
    }

    /// <summary>An empty column is a hole in the picker, never a layout.</summary>
    [Fact]
    public void S420_MoreColumnsThanSectionsGivesOneColumnEach()
    {
        Assert.Equal([0, 1, 2], ColumnLayout.Partition([3, 5, 2], 6));
    }

    [Fact]
    public void S420_ColumnsOnlyEverIncreaseInReadingOrder()
    {
        var result = ColumnLayout.Partition([5, 11, 5, 5, 3, 5, 7, 3, 4], 4);

        for (var i = 1; i < result.Length; i++)
        {
            Assert.True(result[i] == result[i - 1] || result[i] == result[i - 1] + 1);
        }

        Assert.Equal(0, result[0]);
        Assert.Equal(3, result[^1]);
    }

    /// <summary>
    /// The picker's own shape: an eleven-row System section among small ones. Fourteen is the best
    /// any contiguous four-column split of these can do.
    /// </summary>
    [Fact]
    public void S420_ThePickersCategoriesSplitIntoFourteenRows()
    {
        int[] heights = [5, 5, 11, 5, 3, 5, 7, 3, 4];
        Assert.Equal(14, Tallest(heights, ColumnLayout.Partition(heights, 4), 4));
    }

    /// <summary>Checked against every contiguous split there is, not against a number worked out by hand.</summary>
    [Theory]
    [InlineData(new[] { 5, 11, 5, 5, 3, 5, 7, 3, 4 }, 4)]
    [InlineData(new[] { 1, 9, 1, 1, 1, 9 }, 3)]
    [InlineData(new[] { 4, 4, 4, 4, 4 }, 2)]
    [InlineData(new[] { 7, 1, 1, 1, 1, 1, 1, 7 }, 3)]
    [InlineData(new[] { 3 }, 2)]
    public void S420_TheTallestColumnIsAsShortAsAnyContiguousSplitAllows(int[] heights, int columns)
    {
        var used = Math.Min(columns, heights.Length);
        var result = ColumnLayout.Partition(heights, columns);

        Assert.Equal(BruteForce(heights, used), Tallest(heights, result, used));
    }

    private static int Tallest(int[] heights, int[] columnOf, int columns)
        => Enumerable.Range(0, columns)
            .Select(column => heights.Where((_, i) => columnOf[i] == column).Sum())
            .Max();

    /// <summary>The smallest tallest-column over every way to cut the list into that many non-empty runs.</summary>
    private static int BruteForce(int[] heights, int columns)
    {
        var best = int.MaxValue;
        Search(0, columns, 0);
        return best;

        void Search(int start, int left, int tallest)
        {
            if (left == 1)
            {
                best = Math.Min(best, Math.Max(tallest, heights[start..].Sum()));
                return;
            }

            // Leave at least one section for each of the columns still to come.
            for (var end = start + 1; end <= heights.Length - (left - 1); end++)
            {
                Search(end, left - 1, Math.Max(tallest, heights[start..end].Sum()));
            }
        }
    }

    [Fact]
    public void S420_EqualSectionsSplitEvenly()
    {
        Assert.Equal([0, 0, 1, 1], ColumnLayout.Partition([2, 2, 2, 2], 2));
    }

    /// <summary>One section taller than all the rest together still gets a column to itself.</summary>
    [Fact]
    public void S420_AGiantSectionStandsAlone()
    {
        Assert.Equal([0, 1, 1, 1], ColumnLayout.Partition([20, 1, 1, 1], 2));
    }

    /// <summary>Ties go left, so the same input always draws the same picker.</summary>
    [Fact]
    public void S420_TiesFavourTheEarliestSplit()
    {
        // [1 | 1 1] and [1 1 | 1] are both two tall; the earliest split is [1 | 1 1].
        Assert.Equal([0, 1, 1], ColumnLayout.Partition([1, 1, 1], 2));
        Assert.Equal(ColumnLayout.Partition([1, 1, 1], 2), ColumnLayout.Partition([1, 1, 1], 2));
    }

    [Fact]
    public void S420_ZeroHeightSectionsAreAllowed()
    {
        var result = ColumnLayout.Partition([0, 0, 4], 2);
        Assert.Equal(3, result.Length);
        Assert.Equal(1, result[^1]);
    }

    [Fact]
    public void S420_RefusesNonsense()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ColumnLayout.Partition([1, 2], 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => ColumnLayout.Partition([1, -2], 2));
        Assert.Throws<ArgumentNullException>(() => ColumnLayout.Partition(null!, 2));
    }
}
