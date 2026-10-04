using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Themia.Core.Widgets;

namespace Themia.Core.Developer;

/// <summary>One rate-limit window as the usage endpoint sends it.</summary>
public sealed class UsageWindowWire
{
    [JsonPropertyName("utilization")]
    public double? Utilization { get; set; }

    [JsonPropertyName("resets_at")]
    public string? ResetsAt { get; set; }
}

/// <summary>
/// [S-633] The part of <c>GET /api/oauth/usage</c> the widget draws. Every other bucket the
/// endpoint sends (code-named experiments, extra usage) is ignored, so a server-side addition can
/// never break parsing.
/// </summary>
public sealed class ClaudeUsageWire
{
    [JsonPropertyName("five_hour")]
    public UsageWindowWire? FiveHour { get; set; }

    [JsonPropertyName("seven_day")]
    public UsageWindowWire? SevenDay { get; set; }

    [JsonPropertyName("seven_day_opus")]
    public UsageWindowWire? SevenDayOpus { get; set; }

    [JsonPropertyName("seven_day_sonnet")]
    public UsageWindowWire? SevenDaySonnet { get; set; }
}

[JsonSerializable(typeof(ClaudeUsageWire))]
public sealed partial class ClaudeUsageWireContext : JsonSerializerContext;

/// <param name="Utilization">How much of the window's limit is used, as a percentage (0..100), as sent.</param>
/// <param name="ResetsAt">When the window starts over; null until something has been used in it.</param>
public sealed record UsageWindow(double Utilization, DateTimeOffset? ResetsAt)
{
    public static UsageWindow Unused { get; } = new(0, null);
}

/// <summary>[S-630] A Claude subscription's usage: the session and weekly windows, and the per-model weeks when limited.</summary>
public sealed record ClaudeUsageReport(UsageWindow FiveHour, UsageWindow SevenDay, UsageWindow? Opus, UsageWindow? Sonnet);

/// <summary>A usage body, parsed or refused with the reason.</summary>
public sealed record UsageParse(ClaudeUsageReport? Report, string? Error);

public enum UsageSeverity
{
    Ok,
    Warn,
    Critical,
}

/// <summary>[S-635] How much of each row is drawn, loosest first.</summary>
public enum UsageDensity
{
    /// <summary>Label and percentage, the bar, and the "resets in" line.</summary>
    Full,

    /// <summary>Label and percentage over the bar; no reset line.</summary>
    Compact,

    /// <summary>Label · bar · percentage on one line.</summary>
    Tight,

    /// <summary>The same line, smaller, rows sharing whatever height is left.</summary>
    Micro,
}

/// <summary>[S-635] Which of a bar's three labels is shown.</summary>
public enum UsageLabelSize
{
    Full,
    Short,
    Mini,
}

/// <summary>[S-793] Usage against the share of its window that has gone by.</summary>
public enum UsagePace
{
    Under,
    On,
    Ahead,
}

/// <summary>[S-790] How a stacked layout spends the height its rows leave over.</summary>
/// <param name="Bar">Bar thickness.</param>
/// <param name="Value">The percentage's type step.</param>
/// <param name="Gap">Space between rows.</param>
/// <param name="Slack">Height still unspent, split above and below the rows.</param>
public sealed record UsageSpread(double Bar, TypeStep Value, double Gap, double Slack);

/// <summary>[S-794] Where the session gauge sits relative to the other bars.</summary>
public enum UsageGaugePlace
{
    None,
    Beside,
    Above,
}

/// <summary>[S-794] Points on the session gauge: a 270° arc open at the bottom.</summary>
public static class UsageGauge
{
    public const double StartDegrees = 135;
    public const double SweepDegrees = 270;

    /// <summary>The point at <paramref name="fraction"/> (0..1) of the arc, in screen coordinates (y down).</summary>
    public static (double X, double Y) Point(double cx, double cy, double radius, double fraction)
    {
        var f = double.IsFinite(fraction) ? Math.Clamp(fraction, 0, 1) : 0;
        var radians = (StartDegrees + (SweepDegrees * f)) * Math.PI / 180;
        return (cx + (radius * Math.Cos(radians)), cy + (radius * Math.Sin(radians)));
    }

    /// <summary>Whether the arc from the start to <paramref name="fraction"/> is the long way round.</summary>
    public static bool IsLargeArc(double fraction)
        => double.IsFinite(fraction) && Math.Clamp(fraction, 0, 1) * SweepDegrees > 180;
}

/// <summary>One row of the widget, with its label at three widths.</summary>
/// <param name="Short">Mid width: the qualifier only ("Session").</param>
/// <param name="Mini">Narrowest: a few characters, still distinct between rows ("5h").</param>
public sealed record UsageBar(string Key, string Label, string Short, string Mini, UsageWindow Window);

/// <summary>
/// [S-630]..[S-636] The Claude usage widget's rules, ported from the classic app's
/// <c>claudeUsage.ts</c> and the Rust <c>parse_claude_usage</c> / <c>map_usage_error</c>. Pure.
/// <para>
/// The endpoint is undocumented: it is the one the Claude CLI reads its own subscription usage
/// from, and it may change shape or auth without notice. So the parse is strict about what it
/// needs and blind to everything else.
/// </para>
/// </summary>
public static class ClaudeUsageRules
{
    public const int DefaultRefreshMinutes = 15;

    // ---- parsing -------------------------------------------------------------------------------

    /// <summary>
    /// [S-633] Parse a usage body. A body that is not a JSON object is an error, never an all-zero
    /// default: "the endpoint changed shape" has to be loud. An empty object is valid and is a
    /// fresh, unused subscription.
    /// </summary>
    public static UsageParse Parse(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return new UsageParse(null, "The usage endpoint sent an empty answer");
        }

        ClaudeUsageWire? wire;
        try
        {
            wire = JsonSerializer.Deserialize(body, ClaudeUsageWireContext.Default.ClaudeUsageWire);
        }
        catch (JsonException ex)
        {
            return new UsageParse(null, $"Unexpected usage response: {ex.Message}");
        }

        if (wire is null)
        {
            return new UsageParse(null, "Unexpected usage response: not an object");
        }

        return new UsageParse(
            new ClaudeUsageReport(
                Window(wire.FiveHour) ?? UsageWindow.Unused,
                Window(wire.SevenDay) ?? UsageWindow.Unused,
                Window(wire.SevenDayOpus),
                Window(wire.SevenDaySonnet)),
            null);
    }

    private static UsageWindow? Window(UsageWindowWire? wire)
    {
        if (wire is null)
        {
            return null;
        }

        DateTimeOffset? resets = DateTimeOffset.TryParse(wire.ResetsAt, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at)
            ? at
            : null;

        return new UsageWindow(wire.Utilization ?? 0, resets);
    }

    /// <summary>
    /// [S-634] A refused usage request in words that say what to do. A 401 the account service
    /// could not mend by renewing is a sign-in to do again; a 403 is a token without the scope the
    /// endpoint wants, which signing in the same way again will not fix, so Anthropic's own reason
    /// is shown; anything else is the status and whatever Anthropic said.
    /// </summary>
    public static string MapError(int status, string? providerMessage)
    {
        var said = string.IsNullOrWhiteSpace(providerMessage) ? null : providerMessage.Trim();

        return status switch
        {
            401 => "The Claude sign-in expired or was revoked. Reconnect the Claude account in Accounts",
            403 => said is null
                ? "The Claude sign-in lacks a permission the usage endpoint needs"
                : $"The Claude sign-in lacks a permission: {said}",
            _ => said is null ? $"Anthropic answered HTTP {status}" : $"Anthropic answered HTTP {status}: {said}",
        };
    }

    // ---- rows ----------------------------------------------------------------------------------

    /// <summary>
    /// [S-630] The rows to draw: the session and weekly windows always, a per-model week when its
    /// toggle is on. A per-model window the endpoint sends as null (no active limit) is a 0% bar,
    /// not a missing row, so a toggled-on model always shows.
    /// </summary>
    public static IReadOnlyList<UsageBar> BuildBars(ClaudeUsageReport usage, bool showOpus, bool showSonnet)
    {
        ArgumentNullException.ThrowIfNull(usage);

        var bars = new List<UsageBar>(4)
        {
            new("five_hour", "Session (5h)", "Session", "5h", usage.FiveHour),
            new("seven_day", "Weekly (all models)", "Weekly", "7d", usage.SevenDay),
        };

        if (showOpus)
        {
            bars.Add(new UsageBar("opus", "Weekly (Opus)", "Opus", "Opus", usage.Opus ?? UsageWindow.Unused));
        }

        if (showSonnet)
        {
            bars.Add(new UsageBar("sonnet", "Weekly (Sonnet)", "Sonnet", "Son.", usage.Sonnet ?? UsageWindow.Unused));
        }

        return bars;
    }

    /// <summary>
    /// [S-631] The endpoint's utilization as a percentage. It already is one (a live Max 5x payload
    /// says <c>42.5</c> for 42.5%), so it is clamped and never rescaled: the classic app once read
    /// anything at or under 1 as a fraction, and an almost unused account drew a full red bar.
    /// </summary>
    public static double Percent(double utilization)
        => !double.IsFinite(utilization) || utilization <= 0 ? 0 : Math.Min(100, utilization);

    /// <summary>[S-631] The bar's colour: accent under 70%, caution to 89%, critical from 90%.</summary>
    public static UsageSeverity Severity(double percent)
        => percent >= 90 ? UsageSeverity.Critical : percent >= 70 ? UsageSeverity.Warn : UsageSeverity.Ok;

    /// <summary>
    /// [S-632] "resets in 3h 20m" — or, short, "3h 20m". Empty when the window has no reset, it has
    /// passed, or the time is unreadable.
    /// </summary>
    public static string ResetLabel(DateTimeOffset? resetsAt, DateTimeOffset now, bool brief = false)
    {
        if (resetsAt is not { } at || at <= now)
        {
            return string.Empty;
        }

        var totalMinutes = (long)Math.Floor((at - now).TotalMinutes);
        var days = totalMinutes / (60 * 24);
        var hours = totalMinutes % (60 * 24) / 60;
        var minutes = totalMinutes % 60;
        var prefix = brief ? string.Empty : "resets in ";

        if (days > 0)
        {
            return $"{prefix}{days}d {hours}h";
        }

        return hours > 0 ? $"{prefix}{hours}h {minutes}m" : $"{prefix}{minutes}m";
    }

    /// <summary>[S-636] The cadence in minutes, clamped; fifteen by default — the endpoint's limits are strict.</summary>
    public static int ClampRefreshMinutes(int? minutes)
        => minutes is { } m ? Math.Clamp(m, 5, 60) : DefaultRefreshMinutes;

    // ---- fitting -------------------------------------------------------------------------------

    /// <summary>
    /// The height each density needs per row, at text scale 1: <c>Text</c> is the row's text lines,
    /// <c>Fixed</c> the bar and the gaps inside the row, <c>Gap</c> the space between rows. Must
    /// match how <c>ClaudeUsageView</c> builds a row.
    /// </summary>
    private static readonly (UsageDensity Density, double Text, double Fixed, double Gap)[] Budgets =
    [
        (UsageDensity.Full, 36, 14, 12),
        (UsageDensity.Compact, 20, 10, 8),
        (UsageDensity.Tight, 16, 0, 6),
    ];

    /// <summary>[S-794] The height <paramref name="rows"/> stacked rows need at a density, unspread.</summary>
    public static double StackHeight(int rows, UsageDensity density)
    {
        if (rows <= 0)
        {
            return 0;
        }

        var budget = Budgets.FirstOrDefault(b => b.Density == density);
        return (rows * (budget.Text + budget.Fixed)) + ((rows - 1) * RowGap(density));
    }

    /// <summary>The space between rows at a density.</summary>
    public static double RowGap(UsageDensity density) => density switch
    {
        UsageDensity.Full => 12,
        UsageDensity.Compact => 8,
        UsageDensity.Tight => 6,
        _ => 0,
    };

    /// <summary>
    /// [S-635] The loosest layout whose rows fit <paramref name="height"/> — the widget never
    /// scrolls. An unmeasured box (NaN) or no rows yet is <see cref="UsageDensity.Full"/>; a
    /// measured zero is the opposite case, a box already used up, and lands on
    /// <see cref="UsageDensity.Micro"/>, the one layout with no height floor. Bigger text needs
    /// more room, so it drops a layer sooner.
    /// </summary>
    public static UsageDensity Density(double height, int rows, double textScale = 1)
    {
        if (!double.IsFinite(height) || rows <= 0)
        {
            return UsageDensity.Full;
        }

        if (height <= 0)
        {
            return UsageDensity.Micro;
        }

        var scale = double.IsFinite(textScale) && textScale > 0 ? textScale : 1;

        foreach (var (density, text, fixedPart, gap) in Budgets)
        {
            if (height >= (rows * ((text * scale) + fixedPart)) + ((rows - 1) * gap))
            {
                return density;
            }
        }

        return UsageDensity.Micro;
    }

    /// <summary>
    /// [S-635] The longest label every row can show at <paramref name="width"/>. A one-line row
    /// (<see cref="UsageDensity.Tight"/>, <see cref="UsageDensity.Micro"/>) shares its line with
    /// the bar and the percentage, so it has much less to spend than a stacked one. Unmeasured
    /// spells the label out; a measured zero is the narrowest.
    /// </summary>
    public static UsageLabelSize LabelSize(IReadOnlyList<UsageBar> bars, double width, UsageDensity density, bool resetColumn = false)
    {
        ArgumentNullException.ThrowIfNull(bars);

        if (!double.IsFinite(width))
        {
            return UsageLabelSize.Full;
        }

        var stacked = density is UsageDensity.Full or UsageDensity.Compact;
        var step = stacked ? FluentType.Body : FluentType.Caption;

        // The percentage column ("100%") and its gap; inline rows also hold a bar worth seeing.
        var reserve = FluentType.EstimateWidth(step, 4) + 8 + (stacked ? 0 : 48 + 8);

        // [S-791] The reset column ("6d 23h") and its gap, on a strip that carries one.
        if (resetColumn && !stacked)
        {
            reserve += FluentType.EstimateWidth(FluentType.Caption, 6) + 8;
        }

        var room = width - reserve;

        foreach (var size in (UsageLabelSize[])[UsageLabelSize.Full, UsageLabelSize.Short])
        {
            if (bars.All(b => FluentType.EstimateWidth(step, Label(b, size).Length) <= room))
            {
                return size;
            }
        }

        return UsageLabelSize.Mini;
    }

    // ---- using the space (S-790..S-795) --------------------------------------------------------

    /// <summary>The thinnest and thickest bar; the spread grows it in 2px steps.</summary>
    public const double BarMin = 6;

    public const double BarMax = 12;

    /// <summary>The widest gap the spread opens between stacked rows; past it the rows are centred.</summary>
    public const double GapMax = 24;

    /// <summary>[S-791] A one-line row gets a reset column ("2h 9m") from this box width.</summary>
    public const double ResetColumnNeedsWidth = 360;

    /// <summary>[S-793] Within this many points of the elapsed share of a window, usage is on pace.</summary>
    public const double PaceSlack = 5;

    /// <summary>
    /// [S-790] Spend the height a stacked density leaves over, in this order: a thicker bar (6 → 12),
    /// then the percentage a step up (Body → Subtitle), then wider gaps up to <see cref="GapMax"/>.
    /// What is still left is <see cref="UsageSpread.Slack"/>, for the view to centre the rows in.
    /// One-line densities are returned unchanged: they are already the fallback for no room.
    /// </summary>
    public static UsageSpread Spread(double height, int rows, UsageDensity density)
    {
        var gap = RowGap(density);

        if (density is not (UsageDensity.Full or UsageDensity.Compact) || rows <= 0 || !double.IsFinite(height))
        {
            return new UsageSpread(BarMin, FluentType.Body, gap, 0);
        }

        var budget = Budgets.First(b => b.Density == density);
        var left = height - ((rows * (budget.Text + budget.Fixed)) + ((rows - 1) * gap));
        var bar = BarMin;

        while (bar < BarMax && left + 0.5 >= rows * 2)
        {
            bar += 2;
            left -= rows * 2;
        }

        var value = FluentType.Body;
        var grow = FluentType.Subtitle.LineHeight - FluentType.Body.LineHeight;

        if (left + 0.5 >= rows * grow)
        {
            value = FluentType.Subtitle;
            left -= rows * grow;
        }

        if (rows > 1 && left > 0)
        {
            var extra = Math.Min(GapMax - gap, left / (rows - 1));
            if (extra > 0)
            {
                gap += extra;
                left -= extra * (rows - 1);
            }
        }

        return new UsageSpread(bar, value, gap, Math.Max(0, left));
    }

    /// <summary>[S-791] Whether one-line rows carry a reset column: a wide strip has the width for it.</summary>
    public static bool ResetColumn(double width, UsageDensity density)
        => density is UsageDensity.Tight or UsageDensity.Micro && double.IsFinite(width) && width >= ResetColumnNeedsWidth;

    /// <summary>[S-793] A window's length: the session is five hours, every weekly window seven days.</summary>
    public static TimeSpan WindowLength(UsageBar bar)
    {
        ArgumentNullException.ThrowIfNull(bar);
        return bar.Key == "five_hour" ? TimeSpan.FromHours(5) : TimeSpan.FromDays(7);
    }

    /// <summary>
    /// [S-793] How much of a window has gone by (0..1), derived from its reset time and fixed length.
    /// Null when the window has no reset (nothing used yet), it has passed, or the length is unusable.
    /// </summary>
    public static double? Elapsed(TimeSpan length, DateTimeOffset? resetsAt, DateTimeOffset now)
    {
        if (resetsAt is not { } at || at <= now || length <= TimeSpan.Zero)
        {
            return null;
        }

        return Math.Clamp(1 - ((at - now) / length), 0, 1);
    }

    /// <summary>[S-793] Usage against the share of the window gone by.</summary>
    public static UsagePace Pace(double percent, double elapsed)
    {
        var expected = Math.Clamp(elapsed, 0, 1) * 100;
        return percent > expected + PaceSlack ? UsagePace.Ahead
            : percent < expected - PaceSlack ? UsagePace.Under
            : UsagePace.On;
    }

    public static string PaceText(UsagePace pace) => pace switch
    {
        UsagePace.Ahead => "ahead of pace",
        UsagePace.Under => "under pace",
        _ => "on pace",
    };

    /// <summary>
    /// [S-793] When a window resets, on the viewer's clock: "14:20" today, "Thu 14:20" another day.
    /// </summary>
    public static string ResetClock(DateTimeOffset at, DateTimeOffset now, TimeZoneInfo viewer)
    {
        ArgumentNullException.ThrowIfNull(viewer);

        var local = TimeZoneInfo.ConvertTime(at, viewer);
        var today = TimeZoneInfo.ConvertTime(now, viewer);

        return local.Date == today.Date
            ? local.ToString("HH:mm", CultureInfo.InvariantCulture)
            : local.ToString("ddd HH:mm", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// [S-792] An XS tile with at most two bars shows the session as one large reading instead of two
    /// micro rows: what a glance wants is "how much of my session is gone".
    /// </summary>
    public static bool SessionHero(WidgetArea area, int bars)
        => area.IsMeasured && area.WidthClass == SizeClass.Compact && bars is > 0 and <= 2;

    /// <summary>
    /// [S-794] Where the session gauge goes, if anywhere: beside the weekly bars on a Huge-wide panel,
    /// or on any Expanded panel when there are only one or two bars (stacked bars would leave most of
    /// it empty); above the week in a tall column with one or two bars. Otherwise the plain stack.
    /// </summary>
    public static UsageGaugePlace GaugePlace(WidgetArea area, int bars)
    {
        if (bars < 1 || area.HeightClass < SizeClass.Expanded)
        {
            return UsageGaugePlace.None;
        }

        if ((area.WidthClass == SizeClass.Huge && bars >= 2) || (area.WidthClass >= SizeClass.Expanded && bars <= 2))
        {
            return UsageGaugePlace.Beside;
        }

        return area.Shape == AreaShape.Tall && area.WidthClass >= SizeClass.Regular && bars <= 2
            ? UsageGaugePlace.Above
            : UsageGaugePlace.None;
    }

    /// <summary>[S-795] The "Updated 3m ago" footer, from Regular height up.</summary>
    public static bool Footer(WidgetArea area) => area.HeightClass >= SizeClass.Regular;

    public static string Label(UsageBar bar, UsageLabelSize size)
    {
        ArgumentNullException.ThrowIfNull(bar);
        return size switch
        {
            UsageLabelSize.Mini => bar.Mini,
            UsageLabelSize.Short => bar.Short,
            _ => bar.Label,
        };
    }
}
