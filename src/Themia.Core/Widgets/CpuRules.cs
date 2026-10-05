using Themia.Core.Localization;

namespace Themia.Core.Widgets;

/// <summary>
/// The wording of the machine's processor figures: uptime (the power tile's) and the process and
/// thread totals (the process list's). Pure, so the edge cases are pinned by tests.
/// </summary>
public static class CpuRules
{
    /// <summary>
    /// [S-1143] Time since boot in its two largest units, as the original showed it: <c>3d 4h</c>,
    /// <c>2h 5m</c>, <c>7m</c>.
    /// </summary>
    public static string Uptime(TimeSpan up) => Uptime(Strings.Current, up);

    /// <summary><see cref="Uptime(TimeSpan)"/> in <paramref name="locale"/>.</summary>
    public static string Uptime(Locale locale, TimeSpan up)
    {
        if (up < TimeSpan.Zero)
        {
            up = TimeSpan.Zero;
        }

        if (up.TotalDays >= 1)
        {
            return Strings.Format(locale, Str.WidgetsB_Units_DaysHours, ("days", (int)up.TotalDays), ("hours", up.Hours));
        }

        if (up.TotalHours >= 1)
        {
            return Strings.Format(locale, Str.WidgetsB_Units_HoursMinutes, ("hours", (int)up.TotalHours), ("minutes", up.Minutes));
        }

        return Strings.Format(locale, Str.WidgetsB_Units_Minutes, ("minutes", (int)up.TotalMinutes));
    }

    /// <summary>A count with thousands grouped: "4,210".</summary>
    public static string Count(long value) => LocaleFormat.Integer(value);

    /// <summary>[S-1144] "312 processes · 4,210 threads", the footer of a process list.</summary>
    public static string Totals(int processes, long threads) => Totals(Strings.Current, processes, threads);

    /// <summary><see cref="Totals(int, long)"/> in <paramref name="locale"/>.</summary>
    public static string Totals(Locale locale, int processes, long threads)
        => Strings.Format(locale, Str.WidgetsB_Cpu_Totals,
            ("processes", Strings.Plural(locale, Str.WidgetsB_Cpu_ProcessCount, processes)),
            ("threads", Strings.Plural(locale, Str.WidgetsB_Cpu_ThreadCount, threads)));
}
