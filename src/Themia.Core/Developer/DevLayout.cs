namespace Themia.Core.Developer;

/// <summary>
/// [S-741][S-742] How the developer widgets share their height between several lists, and when a
/// list area is wide enough for two columns of them.
/// </summary>
public static class DevLayout
{
    /// <summary>[S-742] From this width the repository widget's lists sit in two columns, not one.</summary>
    public const double TwoColumnsFrom = 520;

    /// <summary>Layout rounding: a row short by a fraction of a pixel still fits.</summary>
    private const double Tolerance = 0.5;

    /// <summary>
    /// [S-741] Rows per section for sections stacked in <paramref name="height"/>, in priority order:
    /// first every section that has something gets its heading and one row while they fit, then
    /// rows are dealt out one at a time, section by section, until nothing more fits or every
    /// section is shown whole. So a mid-sized tile shows a little of each list rather than all of
    /// the first and none of the last, and a big one shows them all.
    /// </summary>
    /// <param name="height">The height the sections share, in DIPs.</param>
    /// <param name="rowHeight">One row.</param>
    /// <param name="headingHeight">A section's heading, paid once by each section shown; 0 for none.</param>
    /// <param name="available">How many rows each section has, in priority order.</param>
    /// <returns>Rows to show per section, the same order; 0 hides a section and its heading.</returns>
    public static int[] Quota(double height, double rowHeight, double headingHeight, IReadOnlyList<int> available)
    {
        ArgumentNullException.ThrowIfNull(available);

        var shown = new int[available.Count];
        if (!double.IsFinite(height) || height <= 0 || !double.IsFinite(rowHeight) || rowHeight <= 0)
        {
            return shown;
        }

        var heading = double.IsFinite(headingHeight) ? Math.Max(0, headingHeight) : 0;
        var used = 0.0;

        // A heading and a first row for each section, most wanted first.
        for (var i = 0; i < available.Count; i++)
        {
            if (available[i] > 0 && used + heading + rowHeight <= height + Tolerance)
            {
                shown[i] = 1;
                used += heading + rowHeight;
            }
        }

        // Then the rest dealt out a row at a time, in the same order.
        var progress = true;
        while (progress)
        {
            progress = false;
            for (var i = 0; i < available.Count; i++)
            {
                if (shown[i] > 0 && shown[i] < available[i] && used + rowHeight <= height + Tolerance)
                {
                    shown[i]++;
                    used += rowHeight;
                    progress = true;
                }
            }
        }

        return shown;
    }

    /// <summary>[S-742] How many list columns a width takes: two from <see cref="TwoColumnsFrom"/>, else one. An unmeasured width is one.</summary>
    public static int Columns(double width) => double.IsFinite(width) && width >= TwoColumnsFrom ? 2 : 1;
}
