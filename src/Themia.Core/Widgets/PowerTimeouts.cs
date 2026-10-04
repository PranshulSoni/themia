using System.Globalization;

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
    public static string Duration(uint seconds)
    {
        if (seconds == 0)
        {
            return "Never";
        }

        if (seconds < 60)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{seconds} s");
        }

        var minutes = seconds / 60;
        var hours = minutes / 60;
        var rest = minutes % 60;

        return hours == 0
            ? string.Create(CultureInfo.InvariantCulture, $"{minutes} min")
            : rest == 0
                ? string.Create(CultureInfo.InvariantCulture, $"{hours} h")
                : string.Create(CultureInfo.InvariantCulture, $"{hours} h {rest} min");
    }

    /// <summary>The section's rows, in Settings' order; a value that could not be read has no row.</summary>
    public static IReadOnlyList<PowerFact> Facts(PowerTimeouts timeouts)
    {
        var facts = new List<PowerFact>(3);
        Add(facts, PowerFactKind.ScreenOff, "Screen off after", timeouts.ScreenOffSeconds);
        Add(facts, PowerFactKind.Sleep, "Sleep after", timeouts.SleepSeconds);
        Add(facts, PowerFactKind.Hibernate, "Hibernate after", timeouts.HibernateSeconds);
        return facts;
    }

    /// <summary>"Up for 6d 11h": how long since Windows started, in the CPU meter's form ([S-1143]).</summary>
    public static PowerFact Uptime(TimeSpan up)
    {
        var value = CpuRules.Uptime(up);
        return new PowerFact(PowerFactKind.Uptime, "Up for", value, $"Up for {value}");
    }

    private static void Add(List<PowerFact> facts, PowerFactKind kind, string label, uint? seconds)
    {
        if (seconds is { } value)
        {
            var text = Duration(value);
            facts.Add(new PowerFact(kind, label, text, $"{label} {text}"));
        }
    }
}
