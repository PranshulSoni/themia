using System.Text.Json.Serialization;
using Themia.Core.Widgets;

namespace Themia.Core.Model;

/// <summary>[S-451] What a screen-time widget covers: one day, or a rolling window ending today.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ScreenTimePeriod>))]
public enum ScreenTimePeriod
{
    Day = 0,

    /// <summary>The last 7 days, never the calendar week.</summary>
    Week = 1,

    /// <summary>The last 30 days, never the calendar month.</summary>
    Month = 2,
}

/// <summary>[S-451] How the period is drawn. Which styles a period can use is <see cref="ScreenTimeRules.StylesFor"/>.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ScreenTimeStyle>))]
public enum ScreenTimeStyle
{
    /// <summary>A ring of the day's apps.</summary>
    Doughnut = 0,

    /// <summary>One bar split by app for a day; one stacked bar per day for a window.</summary>
    Bars = 1,

    /// <summary>A calendar of the window's days, shaded by use.</summary>
    Heatmap = 2,

    /// <summary>No drawing: the figure and the app list only.</summary>
    None = 3,
}

/// <summary>[S-457] A daily limit on one app, by its bucket key (the lower-case executable name).</summary>
public sealed class ScreenTimeAppLimit
{
    public string App { get; set; } = string.Empty;

    /// <summary>Seconds per day. Zero or less means no limit.</summary>
    public int Seconds { get; set; }

    public ScreenTimeAppLimit Copy() => new() { App = App, Seconds = Seconds };
}

/// <summary>
/// [S-450] Settings for the screen-time widget.
/// <para>
/// Holds no usage at all — only how to draw it and which app keys to leave out. The history lives
/// in its own file on this machine ([S-459]), so copying a widget, exporting a layout or pasting it
/// onto another display never carries what the user did with their day.
/// </para>
/// </summary>
public sealed class ScreenTimeConfig : WidgetConfig
{
    [JsonIgnore]
    public override WidgetKind Kind => WidgetKind.ScreenTime;

    public ScreenTimePeriod Period { get; set; } = ScreenTimePeriod.Day;

    public ScreenTimeStyle Style { get; set; } = ScreenTimeStyle.Doughnut;

    /// <summary>
    /// [S-1211] The most apps the legend lists, 1 to 50, or 0 (the default) for as many as the tile
    /// has room for. A ceiling over what fits, never a fixed length: a bigger tile lists more.
    /// </summary>
    public int MaxApps { get; set; }

    /// <summary>[S-1211] Whether the apps are listed beside or under the drawing at all.</summary>
    public bool ShowList { get; set; } = true;

    /// <summary>[S-453] In a window, the average per day beside the total and on each row.</summary>
    public bool ShowAverage { get; set; } = true;

    /// <summary>[S-453] How often the foreground app changed.</summary>
    public bool ShowSwitches { get; set; }

    /// <summary>[S-453] Each app's share of the shown total.</summary>
    public bool ShowPercent { get; set; }

    /// <summary>[S-455] App keys left out of every view. Keys only, never durations.</summary>
    public List<string> HiddenApps { get; set; } = [];

    /// <summary>[S-457] A daily limit on the whole screen time, in seconds. Zero is no limit.</summary>
    public int GoalSeconds { get; set; }

    /// <summary>[S-457] Daily limits on single apps.</summary>
    public List<ScreenTimeAppLimit> AppLimits { get; set; } = [];

    public override WidgetConfig Clone() => new ScreenTimeConfig
    {
        Period = Period,
        Style = Style,
        MaxApps = MaxApps,
        ShowList = ShowList,
        ShowAverage = ShowAverage,
        ShowSwitches = ShowSwitches,
        ShowPercent = ShowPercent,
        HiddenApps = [.. HiddenApps ?? []],
        GoalSeconds = GoalSeconds,
        AppLimits = [.. (AppLimits ?? []).Select(limit => limit.Copy())],
    };
}
