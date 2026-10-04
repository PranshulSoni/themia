namespace Themia.Core.Widgets;

/// <summary>
/// [S-1526] The keyboard cursor of items laid out in sections of a fixed number of columns — the
/// Apps widget's folder sections of tiles or rows. Items are numbered in reading order across the
/// sections; each section starts on a new row, so its last row may be short.
/// <para>
/// Left / Right step one item (clamped, across section ends), Up / Down step one visual row and
/// keep the column — on a shorter row they land on its last item — Page Up / Page Down step
/// <c>pageRows</c> rows, Home / End jump to the ends, Escape clears. With no cursor, a forward key or
/// Home lands on the first item and a backward key or End on the last. Pure, so the arithmetic at a
/// section's ragged last row is tested here and not found by a user.
/// </para>
/// </summary>
public static class SectionCursor
{
    /// <param name="sections">How many items each section holds, in order. Empty sections are allowed and take no rows.</param>
    /// <param name="columns">Items per row (1 for a one-column list).</param>
    /// <param name="current">The item under the cursor, or null.</param>
    /// <param name="key">The key pressed.</param>
    /// <param name="pageRows">How many rows Page Up / Page Down step.</param>
    /// <returns>The cursor after the key, and whether the key meant anything to the list.</returns>
    public static (int? Index, bool Used) Move(IReadOnlyList<int> sections, int columns, int? current, WidgetKey key, int pageRows = 4)
    {
        ArgumentNullException.ThrowIfNull(sections);

        var rows = Rows(sections, Math.Max(1, columns));
        var count = rows.Count == 0 ? 0 : rows[^1].Start + rows[^1].Length;
        int? at = current is { } c && c >= 0 && c < count ? c : null;

        if (key == WidgetKey.Escape)
        {
            return (null, at is not null);
        }

        if (count == 0)
        {
            return (null, false);
        }

        var forward = key is WidgetKey.Right or WidgetKey.Down or WidgetKey.PageDown or WidgetKey.Home;
        var backward = key is WidgetKey.Left or WidgetKey.Up or WidgetKey.PageUp or WidgetKey.End;

        if (!forward && !backward)
        {
            return (at, false);
        }

        if (key == WidgetKey.Home)
        {
            return (0, true);
        }

        if (key == WidgetKey.End)
        {
            return (count - 1, true);
        }

        if (at is not { } index)
        {
            return (forward ? 0 : count - 1, true);
        }

        switch (key)
        {
            case WidgetKey.Left:
                return (Math.Max(0, index - 1), true);

            case WidgetKey.Right:
                return (Math.Min(count - 1, index + 1), true);
        }

        var row = rows.FindIndex(r => index >= r.Start && index < r.Start + r.Length);
        var column = index - rows[row].Start;
        var step = key is WidgetKey.PageUp or WidgetKey.PageDown ? Math.Max(1, pageRows) : 1;
        var target = Math.Clamp(row + (forward ? step : -step), 0, rows.Count - 1);

        return (rows[target].Start + Math.Min(column, rows[target].Length - 1), true);
    }

    /// <summary>The visual rows: where each starts in reading order and how many items it holds.</summary>
    private static List<(int Start, int Length)> Rows(IReadOnlyList<int> sections, int columns)
    {
        var rows = new List<(int Start, int Length)>();
        var start = 0;

        foreach (var size in sections)
        {
            for (var offset = 0; offset < size; offset += columns)
            {
                rows.Add((start + offset, Math.Min(columns, size - offset)));
            }

            start += Math.Max(0, size);
        }

        return rows;
    }
}
