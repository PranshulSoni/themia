using Themia.Core.Model;

namespace Themia.Core.Widgets;

/// <summary>How a Mood tile is arranged. [S-1453]</summary>
public enum MoodArrangement
{
    /// <summary>Picker on top, the heatmap under it with weeks as rows, then the thought box and the footer.</summary>
    Stacked,

    /// <summary>
    /// A wide tile: a left pane (picker, thought, footer, insights) and the heatmap beside it with
    /// weeks as <b>columns</b> and weekdays as rows, so the map runs along the width it has.
    /// </summary>
    Split,
}

/// <summary>
/// [S-1452] [S-1453] [S-1454] What one Mood tile shows and how big its squares are.
/// </summary>
/// <param name="Arrangement">Stacked or split.</param>
/// <param name="Cell">The side of one day square, DIPs.</param>
/// <param name="Weeks">How many weeks the map shows (rows when stacked, columns when split).</param>
/// <param name="Labels">Weekday headers (and, split, month labels): only when a square is big enough.</param>
/// <param name="Note">The thought box.</param>
/// <param name="Stats">The run / average line.</param>
/// <param name="LeftWidth">Split only: the left pane's width.</param>
/// <param name="MapWidth">Split only: the map's width, labels included.</param>
/// <param name="Distribution">Split only: how often each of the five levels was logged.</param>
/// <param name="Weekdays">Split only: the average for each weekday.</param>
/// <param name="RecentNotes">Split only: how many recent thoughts are listed under the map.</param>
/// <param name="Summary">Split only: days logged, best run, thoughts (when the left pane has the room).</param>
/// <param name="Recent">
/// [S-1573] Month span only: the tile cannot hold the whole month at a legible square, so the map shows
/// the most recent <see cref="Weeks"/> weeks (this one last) instead — never a partial month. The
/// header says so (<see cref="MoodLayout.Caption"/>).
/// </param>
public readonly record struct MoodPlan(
    MoodArrangement Arrangement,
    double Cell,
    int Weeks,
    bool Labels,
    bool Note,
    bool Stats,
    double LeftWidth,
    double MapWidth,
    bool Distribution,
    bool Weekdays,
    int RecentNotes,
    bool Summary = false,
    bool Recent = false);

/// <summary>
/// The Mood widget's ladder. Every number the view lays out with is here, so the view and the plan
/// cannot disagree about what a part costs.
/// </summary>
public static class MoodLayout
{
    /// <summary>Side inset of every part.</summary>
    public const double Inset = 16;

    /// <summary>Above the first part and under the last.</summary>
    public const double Top = 4;

    public const double Bottom = 12;

    /// <summary>Between two parts.</summary>
    public const double Gap = 8;

    /// <summary>The face buttons.</summary>
    public const double PickerHeight = 28;

    /// <summary>The narrowest the five faces can be laid out at (and the left pane's minimum).</summary>
    public const double PickerMinWidth = 176;

    public const double NoteHeight = 32;

    /// <summary>One caption line: the footer, a label, a recent-thought row.</summary>
    public const double Line = 16;

    /// <summary>A recent-thought row (Body line).</summary>
    public const double NoteRow = 20;

    /// <summary>Between two squares.</summary>
    public const double CellGap = 4;

    /// <summary>The smallest square still worth drawing.</summary>
    public const double MinCell = 9;

    /// <summary>The largest: a heatmap is read as a texture, so past this the tile holds more weeks instead.</summary>
    public const double MaxCell = 28;

    /// <summary>At or above this a square holds its level as a digit, and the map has room for labels.</summary>
    public const double LabelCell = 18;

    /// <summary>Split: the weekday label column beside the rows.</summary>
    public const double WeekdayLabelWidth = 24;

    /// <summary>Split: title line + five bars.</summary>
    public const double DistributionHeight = Line + (5 * (Line + 2));

    /// <summary>Split: title line + seven rows.</summary>
    public const double WeekdaysHeight = Line + (7 * Line);

    /// <summary>Split: title line + three lines.</summary>
    public const double SummaryHeight = Line + (3 * Line);

    /// <summary>Stacked rolling maps prefer at least this many weeks to bigger squares.</summary>
    private const int PreferredRows = 3;

    /// <param name="area">The view's slot.</param>
    /// <param name="config">The settings: thought box, footer, span, weeks ceiling.</param>
    /// <param name="monthWeeks">How many week lines the current month spans (Month span only).</param>
    public static MoodPlan Plan(WidgetArea area, MoodTrackerConfig config, int monthWeeks)
    {
        ArgumentNullException.ThrowIfNull(config);

        var width = Math.Max(0, area.Width - (2 * Inset));
        var height = Math.Max(0, area.Height - Top - Bottom);
        var month = config.Span == MoodSpan.Month;
        var ceiling = month ? Math.Max(1, monthWeeks) : config.EffectiveHistoryWeeks;

        return area.IsMeasured && area.WidthClass >= SizeClass.Expanded
            ? Split(area, config, width, height, month, ceiling)
            : Stacked(area, config, width, height, month, ceiling);
    }

    private static MoodPlan Stacked(WidgetArea area, MoodTrackerConfig config, double width, double height, bool month, int ceiling)
    {
        var room = new Disclosure(width, height, Gap);
        room.Reserve(PickerHeight);

        // [S-1573] A month is whole or it is not a month: its weeks at the smallest square are the
        // map's floor, taken before the thought box and the footer (those, and the weekday labels,
        // are what gives way first). When even that does not fit, the month is replaced by the most
        // recent weeks, flagged in the plan so the header can say so.
        var monthFloor = MapHeight(ceiling, MinCell);
        var whole = month && monthFloor + Gap <= room.Remaining + Disclosure.Tolerance;
        var floor = whole ? monthFloor : MinCell;
        room.Reserve(floor);                                 // the map: the whole month, else one row at the least

        // The thought box before the footer, and both before the map grows: on an M tile the box is
        // worth more than a fourth week of squares.
        var note = config.ShowNote && area.HeightClass >= SizeClass.Regular && room.Take(NoteHeight);
        var stats = config.ShowStats && area.HeightClass >= SizeClass.Regular && room.Take(Line);

        var mapHeight = room.Remaining + floor;
        var byWidth = (width - (CellGap * (MoodGrid.Columns - 1))) / MoodGrid.Columns;

        if (whole)
        {
            // Every week, squares as big as the room allows; the labels only if they still leave a labelled square.
            var (wholeCell, wholeLabels) = FitStacked(byWidth, mapHeight, ceiling, exact: true);
            return new MoodPlan(MoodArrangement.Stacked, wholeCell, ceiling, wholeLabels, note, stats, 0, width, false, false, 0);
        }

        if (month)
        {
            // The weeks that fit at the smallest square, fewer than the month has (or it would be whole).
            var recent = Math.Clamp(Rows(mapHeight, MinCell), 1, Math.Max(1, ceiling - 1));
            var (recentCell, recentLabels) = FitStacked(byWidth, mapHeight, recent, exact: true);
            return new MoodPlan(MoodArrangement.Stacked, recentCell, recent, recentLabels, note, stats, 0, width, false, false, 0, Recent: true);
        }

        var (cell, labels) = FitStacked(byWidth, mapHeight, Math.Min(ceiling, PreferredRows), exact: false);
        var rows = Math.Clamp(Rows(mapHeight - LabelCost(labels), cell), 1, ceiling);

        return new MoodPlan(MoodArrangement.Stacked, cell, rows, labels, note, stats, 0, width, false, false, 0);
    }

    /// <summary>
    /// [S-1573] What the header says about the map when it is not the plain rolling one: the month's
    /// name over a whole month, "This week" / "Last 3 weeks" where the month had to give way to the
    /// most recent weeks. Null for a rolling map (nothing to explain).
    /// </summary>
    /// <param name="plan">The plan the map is drawn from.</param>
    /// <param name="config">The settings (the span).</param>
    /// <param name="month">The current month, 1..12, for the month's name.</param>
    public static string? Caption(MoodPlan plan, MoodTrackerConfig config, int month)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (config.Span != MoodSpan.Month)
        {
            return null;
        }

        if (!plan.Recent)
        {
            return MoodInsights.MonthName(month);
        }

        return plan.Weeks <= 1
            ? "This week"
            : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Last {plan.Weeks} weeks");
    }

    /// <summary>The height of <paramref name="weeks"/> rows of <paramref name="cell"/>-sized squares.</summary>
    public static double MapHeight(int weeks, double cell)
        => (Math.Max(1, weeks) * cell) + ((Math.Max(1, weeks) - 1) * CellGap);

    /// <summary>The square for a stacked map that has to show <paramref name="rows"/> rows: labels if they still leave a labelled square.</summary>
    private static (double Cell, bool Labels) FitStacked(double byWidth, double mapHeight, int rows, bool exact)
    {
        foreach (var labels in (bool[])[true, false])
        {
            var byHeight = ((mapHeight - LabelCost(labels) + CellGap) / Math.Max(1, rows)) - CellGap;
            var cell = Math.Min(Math.Min(byWidth, MaxCell), exact ? byHeight : Math.Max(byHeight, MinCell));
            cell = Math.Max(cell, MinCell);

            if (!labels || cell >= LabelCell)
            {
                return (cell, labels && cell >= LabelCell);
            }
        }

        return (MinCell, false);
    }

    private static MoodPlan Split(WidgetArea area, MoodTrackerConfig config, double width, double height, bool month, int ceiling)
    {
        // The map: seven rows of squares as tall as the tile allows, labels when they fit.
        var labels = true;
        var cell = ((height - LabelCost(true) - (6 * CellGap)) / 7);

        if (cell < LabelCell)
        {
            labels = false;
            cell = (height - (6 * CellGap)) / 7;
        }

        cell = Math.Clamp(cell, MinCell, MaxCell);

        var labelWidth = labels ? WeekdayLabelWidth + CellGap : 0;
        var mapRoom = width - PickerMinWidth - (2 * Gap) - labelWidth;
        var fit = Math.Max(1, (int)Math.Floor((mapRoom + CellGap) / (cell + CellGap)));
        var weeks = Math.Min(ceiling, fit);

        if (month && fit < ceiling)
        {
            // A month is shown whole: the squares give instead.
            weeks = ceiling;
            cell = Math.Max(MinCell, ((mapRoom + CellGap) / ceiling) - CellGap);
            labels = labels && cell >= LabelCell;
            labelWidth = labels ? WeekdayLabelWidth + CellGap : 0;
        }

        var mapWidth = labelWidth + (weeks * (cell + CellGap)) - CellGap;
        var mapHeight = LabelCost(labels) + (7 * (cell + CellGap)) - CellGap;
        var left = Math.Max(PickerMinWidth, width - mapWidth - (2 * Gap));

        // The left pane, in priority order.
        var pane = new Disclosure(left, height, Gap);
        pane.Reserve(PickerHeight);
        var note = config.ShowNote && pane.Take(NoteHeight);
        var stats = config.ShowStats && pane.Take(Line);
        var distribution = area.HeightClass >= SizeClass.Regular && pane.Take(DistributionHeight);
        var weekdays = distribution && pane.Take(WeekdaysHeight);
        var summary = weekdays && pane.Take(SummaryHeight);

        // Under the map: the recent thoughts, as many whole rows as the height leaves.
        var notes = 0;

        if (area.HeightClass >= SizeClass.Expanded)
        {
            var under = height - mapHeight - Gap - Line - CellGap;
            notes = Disclosure.WholeRows(under, NoteRow, CellGap);
        }

        return new MoodPlan(MoodArrangement.Split, cell, weeks, labels, note, stats, left, mapWidth, distribution, weekdays, Math.Max(0, notes), summary);
    }

    private static int Rows(double height, double cell)
        => Math.Max(1, (int)Math.Floor((height + CellGap + Disclosure.Tolerance) / (cell + CellGap)));

    private static double LabelCost(bool labels) => labels ? Line + CellGap : 0;
}
