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
    public static UsageLabelSize LabelSize(IReadOnlyList<UsageBar> bars, double width, UsageDensity density)
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
