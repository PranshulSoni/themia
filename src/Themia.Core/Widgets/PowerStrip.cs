using Themia.Core.Localization;
namespace Themia.Core.Widgets;

/// <summary>Which fact a status line carries; picks its icon.</summary>
public enum PowerFactKind
{
    Source = 0,
    Plan = 1,
    Remaining = 2,
    Saver = 3,

    /// <summary>Windows would not report anything; the one line says so.</summary>
    Unreadable = 4,

    /// <summary>[S-1246] The last action failed; the tile leads with why.</summary>
    Error = 5,

    /// <summary>[S-1623] The largest tile's panel: time since Windows started.</summary>
    Uptime = 6,

    /// <summary>[S-1623] The largest tile's "Screen and sleep" rows.</summary>
    ScreenOff = 7,
    Sleep = 8,
    Hibernate = 9,
}

/// <summary>One status line: a label for the panel's rows, a value, and the one-line form for the strip.</summary>
public readonly record struct PowerFact(PowerFactKind Kind, string Label, string Value, string Line);

/// <summary>
/// [S-1250] The power tile's status strip: the facts the former status widget showed (source, plan,
/// time left, battery saver), in the order the tile drops them. The strip only ever takes room the
/// buttons do not need (<see cref="PowerTileLayout"/>), so these are secondary by construction.
/// </summary>
public static class PowerStrip
{
    /// <summary>
    /// The facts worth a line, most important first. The source leads (with the charge on a
    /// laptop: it is the one fact the battery meter does not already say), then the plan, the time
    /// left while discharging, and battery saver — which means nothing on a machine with no battery.
    /// </summary>
    public static IReadOnlyList<PowerFact> Facts(PowerSnapshot snapshot, bool showPlan, bool showTimeRemaining, bool showSaver)
    {
        if (!snapshot.Readable)
        {
            return [new PowerFact(PowerFactKind.Unreadable, Str.WidgetsC_Power_Fact_Power.Text(), Str.WidgetsC_Power_Unavailable.Text(), Str.WidgetsC_Power_Fact_UnreadableLine.Text())];
        }

        var facts = new List<PowerFact>(4);

        var source = SourceValue(snapshot);
        facts.Add(new PowerFact(PowerFactKind.Source, Str.WidgetsC_Power_Fact_Power.Text(), source, source));

        if (showPlan)
        {
            var plan = PowerRules.PlanLabel(snapshot);
            facts.Add(new PowerFact(PowerFactKind.Plan, Str.WidgetsC_Power_Fact_Plan.Text(), plan, snapshot.PlanName is null ? Str.WidgetsC_Power_Fact_PlanUnavailable.Text() : Str.WidgetsC_Power_Fact_PlanLine.Format(("plan", plan))));
        }

        if (showTimeRemaining && snapshot.Remaining is { } remaining && snapshot.Source == PowerSource.Battery)
        {
            var left = Str.WidgetsC_Power_Left.Format(("duration", Formatting.Duration(remaining)));
            facts.Add(new PowerFact(PowerFactKind.Remaining, Str.WidgetsC_Power_Fact_TimeLeft.Text(), Formatting.Duration(remaining), left));
        }

        if (showSaver && snapshot.HasBattery)
        {
            var saver = PowerRules.SaverLabel(snapshot);
            var line = (snapshot.BatterySaver ? Str.WidgetsC_Power_Fact_SaverOn : Str.WidgetsC_Power_Fact_SaverOff).Text();
            facts.Add(new PowerFact(PowerFactKind.Saver, Str.WidgetsC_Power_Fact_Saver.Text(), saver, line));
        }

        return facts;
    }

    /// <summary>
    /// The source, with the charge and its direction where there is a battery: "Plugged in",
    /// "On battery · 64%", "Charging · 80%", "Fully charged".
    /// </summary>
    public static string SourceValue(PowerSnapshot snapshot)
    {
        if (!snapshot.HasBattery)
        {
            return PowerRules.SourceLabel(snapshot);
        }

        if (snapshot.Source == PowerSource.Mains && snapshot.Percent >= 100)
        {
            return Str.WidgetsC_Power_FullyCharged.Text();
        }

        var state = (snapshot.Source switch
        {
            PowerSource.Mains => Str.WidgetsC_Power_Charging,
            PowerSource.Battery => Str.WidgetsC_Power_Source_Battery,
            _ => Str.WidgetsC_Power_Source_Unknown,
        }).Text();

        return snapshot.Percent is { } p
            ? Str.WidgetsC_Power_WithPercent.Format(("state", state), ("percent", LocaleFormat.Percent(Strings.Current, p)))
            : state;
    }
}
