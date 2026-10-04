namespace Themia.Core.Widgets;

/// <summary>
/// Splitting an ordered list of sections into side-by-side columns of even height.
/// <para>
/// Used by the widget picker, whose categories are laid out as columns. Contiguous on purpose: the
/// categories have an order the reader scans top-to-bottom, left-to-right, and a "best fit" packing
/// that put the third category under the first would read as shuffled. A section is never split
/// across two columns, because a header separated from its items is a header nobody can use.
/// </para>
/// </summary>
public static class ColumnLayout
{
    /// <summary>
    /// The column each section lands in, minimising the tallest column.
    /// <para>
    /// Never produces an empty column: asking for more columns than there are sections gives one
    /// column per section. Ties go to the earliest split, so the result is deterministic and the
    /// left-hand columns are the fuller ones.
    /// </para>
    /// </summary>
    /// <param name="heights">Each section's height, in any unit, in reading order.</param>
    /// <param name="columns">How many columns are wanted. Must be at least one.</param>
    public static int[] Partition(IReadOnlyList<int> heights, int columns)
    {
        ArgumentNullException.ThrowIfNull(heights);
        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 1);

        var n = heights.Count;
        if (n == 0)
        {
            return [];
        }

        // Prefix sums make every "sections a..b in one column" height a subtraction.
        var prefix = new long[n + 1];
        for (var i = 0; i < n; i++)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(heights[i]);
            prefix[i + 1] = prefix[i] + heights[i];
        }

        var k = Math.Min(columns, n);

        // best[c, i]: the tallest column when the first i sections go into c columns, none empty.
        // split[c, i]: where the last of those c columns starts.
        var best = new long[k + 1, n + 1];
        var split = new int[k + 1, n + 1];

        for (var i = 1; i <= n; i++)
        {
            best[1, i] = prefix[i];
        }

        for (var c = 2; c <= k; c++)
        {
            for (var i = c; i <= n; i++)
            {
                best[c, i] = long.MaxValue;

                // The last column holds sections start..i-1; the c-1 before it hold the rest, and
                // each of those needs at least one section.
                for (var start = c - 1; start < i; start++)
                {
                    var tallest = Math.Max(best[c - 1, start], prefix[i] - prefix[start]);
                    if (tallest < best[c, i])
                    {
                        best[c, i] = tallest;
                        split[c, i] = start;
                    }
                }
            }
        }

        var result = new int[n];
        var end = n;

        for (var c = k; c >= 1; c--)
        {
            var start = c == 1 ? 0 : split[c, end];
            for (var i = start; i < end; i++)
            {
                result[i] = c - 1;
            }

            end = start;
        }

        return result;
    }
}
