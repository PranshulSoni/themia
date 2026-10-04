using System.Text.Json.Serialization;
using Themia.Core.Widgets;

namespace Themia.Core.Model;

/// <summary>
/// [S-1241] The closed set of things the power tile can do. An enum rather than a string at every
/// boundary: a typo in a hand-edited state file is a load error or a dropped entry
/// (<see cref="PowerConfig.Repair"/>), never a button that silently does nothing — or something else.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<PowerAction>))]
public enum PowerAction
{
    Shutdown = 0,
    Restart = 1,
    Sleep = 2,
    Hibernate = 3,
    Lock = 4,
    SignOut = 5,
    ScreenOff = 6,
}

/// <summary>
/// The power tile: shut down, restart, sleep, hibernate, lock, sign out and display off, with the
/// machine's power state (source, plan, battery saver, time left) as a strip where there is room.
/// </summary>
public sealed class PowerConfig : WidgetConfig
{
    [JsonIgnore]
    public override WidgetKind Kind => WidgetKind.Power;

    /// <summary>
    /// [S-1242] The buttons, in the order they are drawn. Order is the user's: the settings page
    /// reorders this list. Repaired on load to defined, distinct values.
    /// </summary>
    public List<PowerAction> Actions { get; set; } = [.. PowerActionRules.Defaults];

    /// <summary>[S-1244] Show each button's name under its icon (the widget's context menu flips it too).</summary>
    public bool ShowLabels { get; set; } = true;

    /// <summary>
    /// [S-1245] Session-ending actions (shut down, restart, sign out) need a second click. On by
    /// default; off runs every action on one click.
    /// </summary>
    public bool Confirm { get; set; } = true;

    /// <summary>Show the active power plan's friendly name in the status strip.</summary>
    public bool ShowPlan { get; set; } = true;

    /// <summary>Show whether battery saver is on, in the status strip.</summary>
    public bool ShowBatterySaver { get; set; } = true;

    /// <summary>Show Windows' estimate of time left. It only estimates while discharging.</summary>
    public bool ShowTimeRemaining { get; set; } = true;

    /// <summary>
    /// Seconds between reads of the power state, 5 to 300. Slower than a meter by default: a power
    /// plan changes when somebody changes it, and the charge moves by a percent every few minutes.
    /// </summary>
    public int IntervalSeconds { get; set; } = 15;

    /// <summary>Clamped on read, so a persisted zero cannot turn the shared ticker into a spin.</summary>
    [JsonIgnore]
    public TimeSpan TickInterval => TimeSpan.FromSeconds(Math.Clamp(IntervalSeconds, 5, 300));

    /// <summary>The buttons to draw: <see cref="Actions"/> filtered to defined, distinct values.</summary>
    [JsonIgnore]
    public IReadOnlyList<PowerAction> VisibleActions => PowerActionRules.Visible(Actions);

    public override WidgetConfig Clone() => new PowerConfig
    {
        Actions = [.. Actions],
        ShowLabels = ShowLabels,
        Confirm = Confirm,
        ShowPlan = ShowPlan,
        ShowBatterySaver = ShowBatterySaver,
        ShowTimeRemaining = ShowTimeRemaining,
        IntervalSeconds = IntervalSeconds,
    };

    /// <summary>
    /// [S-1241] The action list crosses a trust boundary on load: a number outside the enum (the
    /// converter accepts integers) or a duplicate is dropped, loudly. A null list is the defaults.
    /// </summary>
    public override IReadOnlyList<string> Repair()
    {
        if (Actions is null)
        {
            Actions = [.. PowerActionRules.Defaults];
            return ["power actions were missing; restored the defaults"];
        }

        var visible = PowerActionRules.Visible(Actions);

        if (visible.Count == Actions.Count)
        {
            return [];
        }

        var dropped = Actions.Count - visible.Count;
        Actions = [.. visible];
        return [$"dropped {dropped} unknown or repeated power action(s)"];
    }
}
