using Themia.Core.Localization;

namespace Themia.Core.Widgets;

/// <summary>
/// [S-1623] The active plan's "Screen, sleep and hibernate timeouts" for the power source in use,
/// in seconds as the power API stores them (0 = never). Null where a value could not be read: an
/// unreadable value is not "Never", and the row is left out rather than guessed.
/// </summary>
public readonly record struct PowerTimeouts(uint? ScreenOffSeconds, uint? SleepSeconds, uint? HibernateSeconds)
{
    /// <summary>Nothing read: before the first sample, or the plan could not be opened.</summary>
    public static PowerTimeouts Unknown { get; } = new(null, null, null);
}

/// <summary>
/// [S-1623] The largest power tile's "Screen and sleep" section, worded as Windows 11 Settings
/// words the same settings ("Turn my screen off after", "Make my device sleep after"), and the
/// machine's time since it started. Read-only, like the plan list: changing them is Settings' job.
/// </summary>
public static class PowerTimeoutRules
{
    /// <summary>A timeout as Settings shows it: "Never", "45 s", "5 min", "1 h", "1 h 30 min".</summary>
    public static string Duration(uint seconds) => Duration(Strings.Current, seconds);

    /// <summary><see cref="Duration(uint)"/> in an explicit language: "5 min", "5 Min.", "5 分".</summary>
    public static string Duration(Locale locale, uint seconds)
    {
        if (seconds == 0)
        {
            return Strings.Get(locale, Str.WidgetsC_Power_Timeout_Never);
        }

        if (seconds < 60)
        {
            return Strings.Format(locale, Str.WidgetsC_Power_Timeout_Seconds, ("value", (long)seconds));
        }

        var minutes = seconds / 60;
        var hours = minutes / 60;
        var rest = minutes % 60;

        return hours == 0
            ? Strings.Format(locale, Str.WidgetsC_Power_Timeout_Minutes, ("value", (long)minutes))
            : rest == 0
                ? Strings.Format(locale, Str.WidgetsC_Power_Timeout_Hours, ("value", (long)hours))
                : Strings.Format(locale, Str.WidgetsC_Power_Timeout_HoursMinutes, ("hours", (long)hours), ("minutes", (long)rest));
    }

    /// <summary>The section's rows, in Settings' order; a value that could not be read has no row.</summary>
    public static IReadOnlyList<PowerFact> Facts(PowerTimeouts timeouts)
    {
        var facts = new List<PowerFact>(3);
        Add(facts, PowerFactKind.ScreenOff, Str.WidgetsC_Power_Timeout_ScreenOff.Text(), timeouts.ScreenOffSeconds);
        Add(facts, PowerFactKind.Sleep, Str.WidgetsC_Power_Timeout_Sleep.Text(), timeouts.SleepSeconds);
        Add(facts, PowerFactKind.Hibernate, Str.WidgetsC_Power_Timeout_Hibernate.Text(), timeouts.HibernateSeconds);
        return facts;
    }

    /// <summary>"Up for 6d 11h": how long since Windows started, in the CPU meter's form ([S-1143]).</summary>
    public static PowerFact Uptime(TimeSpan up)
    {
        var value = CpuRules.Uptime(up);
        return new PowerFact(PowerFactKind.Uptime, Str.WidgetsC_Power_UpFor.Text(), value, Str.WidgetsC_Power_UpForLine.Format(("value", value)));
    }

    private static void Add(List<PowerFact> facts, PowerFactKind kind, string label, uint? seconds)
    {
        if (seconds is { } value)
        {
            var text = Duration(value);
            facts.Add(new PowerFact(kind, label, text, Str.WidgetsC_Power_Timeout_Line.Format(("label", label), ("value", text))));
        }
    }
}
