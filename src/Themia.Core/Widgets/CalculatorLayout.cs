namespace Themia.Core.Widgets;

/// <summary>How the calculator's parts are placed.</summary>
public enum CalculatorArrangement
{
    /// <summary>History above the line, the keypad (when it fits) under it.</summary>
    Stacked,

    /// <summary>A wide strip too short for a keypad: the line on the left, the history beside it.</summary>
    Strip,

    /// <summary>A big tile: history and line on the left, the keypad filling the right half.</summary>
    SideKeypad,
}

/// <summary>The calculator's layout for one size.</summary>
/// <param name="Arrangement">Where the parts go.</param>
/// <param name="HistoryRows">Whole history rows shown (0 = the list is hidden).</param>
/// <param name="HistoryRegion">Whether there is room for the history at all (the empty state uses it when there is none).</param>
/// <param name="KeypadRows">0 (no keypad), 5 (standard) or 6 (with the Ans / % / ^ / x² row).</param>
/// <param name="KeyHeight">Each key's height.</param>
/// <param name="ResultLine">The height the live result may use: one Body line, plus whatever is left over.</param>
/// <param name="LeftWidth">Strip and SideKeypad: the left column's width (the line, and the history over it).</param>
public readonly record struct CalculatorPlan(
    CalculatorArrangement Arrangement,
    int HistoryRows,
    bool HistoryRegion,
    int KeypadRows,
    double KeyHeight,
    double ResultLine,
    double LeftWidth);

/// <summary>
/// [S-1514] The calculator's disclosure ladder. The one thing it must show at any size is the line
/// being typed and its live result. Then, as room allows: the newest history row, a keypad under
/// the line (narrow tall tiles), more history rows, and the leftover height to the result, which
/// grows within the type ramp like Windows Calculator's display. A wide strip puts the history
/// beside the line instead of over it; a big tile puts the keypad beside everything else and, when
/// tall enough, adds the Ans / % / ^ / x² row.
/// <para>
/// Pure, so the boundaries between the tiers are tested; the view only places what the plan says.
/// </para>
/// </summary>
public static class CalculatorLayout
{
    /// <summary>The inset round the content (the widget content inset).</summary>
    public const double Inset = 16;

    /// <summary>The text box: the standard Windows 11 control height.</summary>
    public const double InputHeight = 32;

    /// <summary>Between the result line and the text box.</summary>
    public const double EntryGap = 4;

    /// <summary>A history row: the standard Fluent row.</summary>
    public const double Row = 40;

    /// <summary>Over the history (the list inset) and between it and the line (the divider's padding).</summary>
    public const double HistoryChrome = 2 + 12;

    /// <summary>The smallest key: the compact control height.</summary>
    public const double KeyMin = 32;

    /// <summary>A key under the line stops growing here; the rest of the height goes to the history and the result.</summary>
    public const double KeyMaxStacked = 56;

    /// <summary>A key tall enough that the sixth row is worth its room.</summary>
    public const double KeyExtendedMin = 44;

    /// <summary>Between keys, both ways.</summary>
    public const double KeyGap = 4;

    /// <summary>The keypad's own minimum: four columns of the smallest key.</summary>
    public static double KeypadMinWidth => (CalculatorKeypad.Columns * KeyMin) + ((CalculatorKeypad.Columns - 1) * KeyGap);

    /// <param name="area">The view's slot.</param>
    /// <param name="historyCount">Rows in the history now.</param>
    /// <param name="keypad">The user's "show keypad" setting.</param>
    /// <param name="resultMin">The result's smallest line height (Body).</param>
    public static CalculatorPlan Plan(WidgetArea area, int historyCount, bool keypad, double resultMin)
    {
        if (!area.IsMeasured)
        {
            return new CalculatorPlan(CalculatorArrangement.Stacked, historyCount > 0 ? 1 : 0, historyCount > 0, 0, KeyMin, resultMin, 0);
        }

        // A big tile: the keypad beside everything else, full height.
        if (keypad && area.WidthClass >= SizeClass.Expanded && area.Height >= KeypadHeight(5, KeyMin) + (2 * Inset))
        {
            var left = Math.Floor(area.Width / 2);
            var column = Stacked(new WidgetArea(left, area.Height), historyCount, keypad: false, resultMin);
            var keysHeight = area.Height - (2 * Inset);
            var extended = KeyFor(keysHeight, 6) >= KeyExtendedMin;
            var rows = extended ? 6 : 5;

            return column with
            {
                Arrangement = CalculatorArrangement.SideKeypad,
                KeypadRows = rows,
                KeyHeight = KeyFor(keysHeight, rows),
                LeftWidth = left,
            };
        }

        // A strip: the history would get one row over the line, beside it gets the full height.
        if (area.Shape == AreaShape.Wide)
        {
            var left = Math.Floor(area.Width / 2);
            var rows = historyCount > 0 ? Math.Min(historyCount, Disclosure.WholeRows(area.Height - HistoryChrome, Row)) : 0;
            var result = Math.Max(resultMin, area.Height - (2 * Inset) - EntryGap - InputHeight);

            return new CalculatorPlan(CalculatorArrangement.Strip, rows, area.Height >= Row + HistoryChrome, 0, KeyMin, result, left);
        }

        return Stacked(area, historyCount, keypad, resultMin);
    }

    private static CalculatorPlan Stacked(WidgetArea area, int historyCount, bool keypad, double resultMin)
    {
        var room = new Disclosure(area.Width, area.Height);

        // Rung 0: the line and its result, at their smallest, with the inset under them.
        room.Reserve(resultMin + EntryGap + InputHeight + Inset);

        // Rung 1: room for the history (its newest row, or the empty state in that place).
        var region = room.Take(HistoryChrome + Row);

        if (!region)
        {
            // No list over the line: the inset is the line's own top margin.
            room.Reserve(Inset);
        }

        // Rung 2: the keypad under the line. Asked only when it fits, so a miss does not close the
        // ladder on the history rows a medium tile should still show.
        // The line's bottom inset is the gap over the keys; the keypad brings its own under them.
        var keypadCost = KeypadHeight(5, KeyMin) + Inset;
        var showKeypad = keypad
            && area.Width >= KeypadMinWidth + (2 * Inset)
            && room.Remaining + Disclosure.Tolerance >= keypadCost
            && room.Take(keypadCost);

        // Rung 3: the rest of the history, whole rows.
        var rows = region && historyCount > 0 ? 1 + room.TakeRows(Row, historyCount - 1) : 0;

        // What is left: to the keys (up to their cap), then to the result.
        var keyHeight = KeyMin;

        if (showKeypad)
        {
            keyHeight = Math.Min(KeyMaxStacked, KeyMin + Math.Floor(room.Remaining / 5));
            room.Reserve((keyHeight - KeyMin) * 5);
        }

        // An empty history's region takes what is left (the empty state is centred in it).
        var result = region && historyCount == 0 ? resultMin : resultMin + room.Remaining;

        return new CalculatorPlan(CalculatorArrangement.Stacked, rows, region, showKeypad ? 5 : 0, keyHeight, result, 0);
    }

    /// <summary>A keypad of <paramref name="rows"/> keys this tall, with the gaps between them.</summary>
    public static double KeypadHeight(int rows, double key) => (rows * key) + ((rows - 1) * KeyGap);

    private static double KeyFor(double height, int rows) => Math.Floor((height - ((rows - 1) * KeyGap)) / rows);
}
