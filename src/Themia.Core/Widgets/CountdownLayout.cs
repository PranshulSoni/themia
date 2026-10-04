using Themia.Core.Model;

namespace Themia.Core.Widgets;

/// <summary>Where a Countdown tile puts its featured timer, if anywhere. [S-1447]</summary>
public enum CountdownHero
{
    /// <summary>No hero: the plain list.</summary>
    None,

    /// <summary>The hero is the whole tile (a small tile, or a single timer).</summary>
    Only,

    /// <summary>The hero across the top, the other timers as rows under it.</summary>
    Top,

    /// <summary>The hero on the left of a wide strip, the other timers beside it.</summary>
    Left,
}

/// <summary>
/// [S-1447] What one Countdown tile shows, from its area and its number of timers.
/// </summary>
/// <param name="Hero">Where the featured timer goes.</param>
/// <param name="NextLine">Under a hero that is the whole tile, a "Next: …" line naming the timer after it.</param>
/// <param name="CompactRows">Rows at 32px with no date line.</param>
/// <param name="HeroProgress">A progress bar under the hero (time gone from creation to target).</param>
/// <param name="RowProgress">A progress bar under each row.</param>
public readonly record struct CountdownPlan(CountdownHero Hero, bool NextLine, bool CompactRows, bool HeroProgress, bool RowProgress);

/// <summary>
/// The Countdown widget's ladder and the rules behind its hero: which timer is featured, what the
/// "next" line says, and how far along a timer is. Pure; the view only measures.
/// </summary>
public static class CountdownLayout
{
    /// <summary>
    /// [S-1447] XS/S: the featured timer alone (+ "next" line). One timer: alone at any size, since a
    /// one-row list on a big tile is mostly empty. Wide strip: hero left, list right. M, Tall, L, XL:
    /// hero on top and the others as rows under it. Rows go compact on a short or narrow tile (a Wide
    /// strip keeps the date as a column). L/XL add a progress bar under the hero; XL under
    /// each row too.
    /// </summary>
    public static CountdownPlan Plan(WidgetArea area, int timers)
    {
        if (timers <= 0)
        {
            return new CountdownPlan(CountdownHero.None, false, false, false, false);
        }

        var small = area.HeightClass == SizeClass.Compact && area.WidthClass <= SizeClass.Regular;
        var hero = timers == 1 || small
            ? CountdownHero.Only
            : area.Shape == AreaShape.Wide || (area.WidthClass >= SizeClass.Expanded && area.HeightClass == SizeClass.Compact)
                ? CountdownHero.Left
                : CountdownHero.Top;

        // A short tile holds more rows at 32px; a wide one keeps the date in its own column.
        var compactRows = area.WidthClass == SizeClass.Compact || area.HeightClass == SizeClass.Compact;

        var large = area.AtLeast(SizeClass.Expanded, SizeClass.Expanded);

        return new CountdownPlan(
            hero,
            NextLine: hero == CountdownHero.Only && timers > 1,
            CompactRows: compactRows,
            HeroProgress: large,
            RowProgress: large && area.WidthClass >= SizeClass.Huge);
    }

    /// <summary>
    /// [S-1446] The featured timer: the soonest one still ahead; with none ahead, the one that went by
    /// most recently. -1 for no timers.
    /// </summary>
    public static int Featured(IReadOnlyList<CountdownTimer> timers, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(timers);

        var best = -1;

        for (var i = 0; i < timers.Count; i++)
        {
            if (best < 0 || Before(timers[i].Target, timers[best].Target, now))
            {
                best = i;
            }
        }

        return best;
    }

    /// <summary>
    /// [S-1446] "Next: Holiday in 12 days" — the soonest timer still ahead other than
    /// <paramref name="featured"/>, or null when there is none.
    /// </summary>
    public static string? NextLine(IReadOnlyList<CountdownTimer> timers, int featured, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(timers);

        CountdownTimer? next = null;

        for (var i = 0; i < timers.Count; i++)
        {
            var timer = timers[i];

            if (i == featured || timer.Target <= now)
            {
                continue;
            }

            if (next is null || timer.Target < next.Target)
            {
                next = timer;
            }
        }

        if (next is null)
        {
            return null;
        }

        var label = string.IsNullOrWhiteSpace(next.Label) ? CountdownFormatter.UntitledLabel : next.Label;
        var reading = CountdownFormatter.Read(next.Target, now, CountdownFormat.Auto).Text;

        return reading == "in a moment" ? $"Next: {label} in a moment" : $"Next: {label} in {reading}";
    }

    /// <summary>
    /// [S-1448] How far along a timer is, 0 to 1, from when it was created to its target; null when
    /// that cannot be said (no creation time, or one at or after the target).
    /// </summary>
    public static double? Progress(CountdownTimer timer, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(timer);

        if (timer.Created == default || timer.Created >= timer.Target)
        {
            return null;
        }

        var whole = (timer.Target - timer.Created).TotalSeconds;
        var gone = (now - timer.Created).TotalSeconds;

        return Math.Clamp(gone / whole, 0, 1);
    }

    /// <summary>
    /// [S-1447] The widest a duration in <paramref name="format"/> gets, in characters, so the hero's
    /// type is sized once for the format rather than hopping as the digits tick ("10h" → "9h").
    /// </summary>
    public static int HeroCharacters(CountdownFormat format, bool elapsed)
    {
        var widest = format switch
        {
            CountdownFormat.Days => 9,
            CountdownFormat.DaysHoursMinutes => 11,
            CountdownFormat.DaysHoursMinutesSeconds => 15,
            _ => 9,
        };

        return elapsed ? widest + 4 : widest;
    }

    /// <summary>Upcoming beats past; among upcoming the sooner; among past the more recent.</summary>
    private static bool Before(DateTimeOffset candidate, DateTimeOffset incumbent, DateTimeOffset now)
    {
        var candidateAhead = candidate > now;
        var incumbentAhead = incumbent > now;

        if (candidateAhead != incumbentAhead)
        {
            return candidateAhead;
        }

        return candidateAhead ? candidate < incumbent : candidate > incumbent;
    }
}
