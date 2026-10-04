using System.Globalization;

namespace Themia.Core.Office;

public enum RecurrenceFrequency
{
    Daily,
    Weekly,
    Monthly,
    Yearly,
}

/// <summary>
/// [S-577] The part of an RFC 5545 <c>RRULE</c> this app expands.
/// <para>
/// The server is asked to expand recurrences itself (<c>CALDAV:expand</c>), and most do. This is
/// the fallback for one that sends the master event instead: the rules people actually make —
/// every N days, weeks on chosen weekdays, a day of the month, the Nth weekday of a month, a date a
/// year, with <c>COUNT</c> or <c>UNTIL</c> — plus the yearly rules every <c>VTIMEZONE</c> is written
/// in. A rule using anything else (<c>BYSETPOS</c>, <c>BYWEEKNO</c>, <c>BYYEARDAY</c>, sub-daily
/// frequencies) is marked unsupported and its event is shown once, at its own start, rather than
/// guessed at.
/// </para>
/// </summary>
public sealed record RecurrenceRule(
    RecurrenceFrequency Frequency,
    int Interval,
    int? Count,
    IcalTime? Until,
    IReadOnlyList<(int Ordinal, DayOfWeek Day)> ByDay,
    IReadOnlyList<int> ByMonthDay,
    IReadOnlyList<int> ByMonth,
    DayOfWeek WeekStart,
    bool Supported)
{
    private static readonly string[] DayCodes = ["SU", "MO", "TU", "WE", "TH", "FR", "SA"];

    /// <summary>The rule in an <c>RRULE</c> value, or null when it has no usable <c>FREQ</c>.</summary>
    public static RecurrenceRule? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        RecurrenceFrequency? frequency = null;
        var supported = true;
        var interval = 1;
        int? count = null;
        IcalTime? until = null;
        var byDay = new List<(int, DayOfWeek)>();
        var byMonthDay = new List<int>();
        var byMonth = new List<int>();
        var weekStart = DayOfWeek.Monday;

        foreach (var part in value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eq = part.IndexOf('=', StringComparison.Ordinal);
            if (eq <= 0)
            {
                continue;
            }

            var key = part[..eq].ToUpperInvariant();
            var val = part[(eq + 1)..].ToUpperInvariant();

            switch (key)
            {
                case "FREQ":
                    frequency = val switch
                    {
                        "DAILY" => RecurrenceFrequency.Daily,
                        "WEEKLY" => RecurrenceFrequency.Weekly,
                        "MONTHLY" => RecurrenceFrequency.Monthly,
                        "YEARLY" => RecurrenceFrequency.Yearly,
                        _ => null,
                    };

                    supported &= frequency is not null;
                    break;

                case "INTERVAL":
                    if (int.TryParse(val, NumberStyles.None, CultureInfo.InvariantCulture, out var i) && i is >= 1 and <= 1000)
                    {
                        interval = i;
                    }

                    break;

                case "COUNT":
                    if (int.TryParse(val, NumberStyles.None, CultureInfo.InvariantCulture, out var c) && c >= 1)
                    {
                        count = c;
                    }

                    break;

                case "UNTIL":
                    until = IcalTime.Parse(val);
                    break;

                case "BYDAY":
                    foreach (var token in val.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        if (ParseDay(token) is { } day)
                        {
                            byDay.Add(day);
                        }
                        else
                        {
                            supported = false;
                        }
                    }

                    break;

                case "BYMONTHDAY":
                    supported &= ParseInts(val, -31, 31, byMonthDay);
                    break;

                case "BYMONTH":
                    supported &= ParseInts(val, 1, 12, byMonth);
                    break;

                case "WKST":
                    var index = Array.IndexOf(DayCodes, val);
                    if (index >= 0)
                    {
                        weekStart = (DayOfWeek)index;
                    }

                    break;

                default:
                    // BYSETPOS, BYWEEKNO, BYYEARDAY, BYHOUR… change which instances exist; ignoring
                    // them would invent occurrences, so the rule is not expanded at all.
                    supported = false;
                    break;
            }
        }

        if (frequency is null)
        {
            return null;
        }

        // A yearly rule with BYDAY or BYMONTHDAY and no BYMONTH counts across the whole year
        // ("the 20th Monday"); this expands within months only, so it would get those wrong.
        if (frequency == RecurrenceFrequency.Yearly && byMonth.Count == 0 && (byDay.Count > 0 || byMonthDay.Count > 0))
        {
            supported = false;
        }

        return new RecurrenceRule(frequency.Value, interval, count, until, byDay, byMonthDay, byMonth, weekStart, supported);
    }

    /// <summary>
    /// The occurrences' wall clocks, in order, from <paramref name="start"/> (always the first one,
    /// as RFC 5545 counts it). <c>COUNT</c> is applied here because it counts from the very start;
    /// <c>UNTIL</c> and the window end are the caller's, who knows the zone. Lazy: stop enumerating
    /// when far enough. Bounded, so a rule that never matches (31 February) cannot spin forever.
    /// </summary>
    public IEnumerable<DateTime> Occurrences(DateTime start)
    {
        var produced = 0;
        var time = start.TimeOfDay;

        for (var period = 0; period < 20_000; period++)
        {
            foreach (var day in PeriodDays(start, period))
            {
                var occurrence = day.ToDateTime(TimeOnly.FromTimeSpan(time));

                if (occurrence < start)
                {
                    continue;
                }

                if (Count is { } limit && produced >= limit)
                {
                    yield break;
                }

                produced++;
                yield return occurrence;
            }

            // The first period always yields the start (it matches its own rule, or RFC 5545 counts it
            // anyway); past that a rule that has produced nothing for a long time never will.
            if (produced == 0 && period > 400)
            {
                yield break;
            }
        }
    }

    /// <summary>The candidate days of one period, sorted, the start's own day included in period 0.</summary>
    private IEnumerable<DateOnly> PeriodDays(DateTime start, int period)
    {
        var first = DateOnly.FromDateTime(start);
        var step = period * Interval;
        var days = new SortedSet<DateOnly>();

        switch (Frequency)
        {
            case RecurrenceFrequency.Daily:
                var day = first.AddDays(step);
                if (MatchesMonth(day) && MatchesMonthDay(day) && MatchesWeekday(day))
                {
                    days.Add(day);
                }

                break;

            case RecurrenceFrequency.Weekly:
                var weekStart = first.AddDays(-(((int)first.DayOfWeek - (int)WeekStart + 7) % 7)).AddDays(7 * step);
                var wanted = ByDay.Count > 0 ? ByDay.Select(d => d.Day).ToHashSet() : [first.DayOfWeek];
                for (var i = 0; i < 7; i++)
                {
                    var candidate = weekStart.AddDays(i);
                    if (wanted.Contains(candidate.DayOfWeek) && MatchesMonth(candidate))
                    {
                        days.Add(candidate);
                    }
                }

                break;

            case RecurrenceFrequency.Monthly:
                var month = new DateOnly(first.Year, first.Month, 1).AddMonths(step);
                if (MatchesMonth(month))
                {
                    AddMonthDays(days, month.Year, month.Month, first.Day);
                }

                break;

            case RecurrenceFrequency.Yearly:
                var year = first.Year + step;
                if (year > 9998)
                {
                    break;
                }

                foreach (var m in ByMonth.Count > 0 ? ByMonth : [first.Month])
                {
                    if (ByDay.Count == 0 && ByMonthDay.Count == 0)
                    {
                        // A plain yearly date. 29 February only exists in leap years (RFC 5545).
                        if (first.Day <= DateTime.DaysInMonth(year, m))
                        {
                            days.Add(new DateOnly(year, m, first.Day));
                        }
                    }
                    else
                    {
                        AddMonthDays(days, year, m, first.Day);
                    }
                }

                break;
        }

        // Period 0 always holds the start itself, matching or not: RFC 5545 counts DTSTART as the
        // first instance.
        if (period == 0)
        {
            days.Add(first);
        }

        return days;
    }

    private void AddMonthDays(SortedSet<DateOnly> days, int year, int month, int defaultDay)
    {
        var length = DateTime.DaysInMonth(year, month);

        if (ByMonthDay.Count == 0 && ByDay.Count == 0)
        {
            if (defaultDay <= length)
            {
                days.Add(new DateOnly(year, month, defaultDay));
            }

            return;
        }

        for (var d = 1; d <= length; d++)
        {
            var candidate = new DateOnly(year, month, d);
            var monthDayOk = ByMonthDay.Count == 0 || ByMonthDay.Any(md => md > 0 ? md == d : length + md + 1 == d);
            var dayOk = ByDay.Count == 0 || ByDay.Any(b => b.Day == candidate.DayOfWeek && OrdinalMatches(b.Ordinal, d, length));

            if (monthDayOk && dayOk)
            {
                days.Add(candidate);
            }
        }
    }

    /// <summary>"2MO" is the second Monday (days 8–14); "-1FR" the last Friday; 0 is every one.</summary>
    private static bool OrdinalMatches(int ordinal, int day, int length) => ordinal switch
    {
        0 => true,
        > 0 => (day - 1) / 7 == ordinal - 1,
        _ => (length - day) / 7 == -ordinal - 1,
    };

    private bool MatchesMonth(DateOnly day) => ByMonth.Count == 0 || ByMonth.Contains(day.Month);

    private bool MatchesMonthDay(DateOnly day)
    {
        if (ByMonthDay.Count == 0)
        {
            return true;
        }

        var length = DateTime.DaysInMonth(day.Year, day.Month);
        return ByMonthDay.Any(md => md > 0 ? md == day.Day : length + md + 1 == day.Day);
    }

    private bool MatchesWeekday(DateOnly day) => ByDay.Count == 0 || ByDay.Any(b => b.Day == day.DayOfWeek);

    private static (int, DayOfWeek)? ParseDay(string token)
    {
        if (token.Length < 2)
        {
            return null;
        }

        var code = Array.IndexOf(DayCodes, token[^2..]);
        if (code < 0)
        {
            return null;
        }

        var prefix = token[..^2];
        if (prefix.Length == 0)
        {
            return (0, (DayOfWeek)code);
        }

        return int.TryParse(prefix, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n) && n is >= -5 and <= 5 and not 0
            ? (n, (DayOfWeek)code)
            : null;
    }

    private static bool ParseInts(string value, int min, int max, List<int> into)
    {
        foreach (var token in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!int.TryParse(token, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n) || n < min || n > max || n == 0)
            {
                return false;
            }

            into.Add(n);
        }

        return true;
    }
}
