using Themia.Core.Developer;
using Themia.Core.Model;
using Xunit;

namespace Themia.Core.Tests;

/// <summary>
/// [S-630]..[S-636] Claude usage, ported from the classic app's <c>claudeUsage.test.ts</c> and the
/// Rust <c>claude_usage_tests</c>.
/// </summary>
public sealed class ClaudeUsageTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 14, 12, 0, 0, TimeSpan.Zero);

    // Captured from a live GET /api/oauth/usage (Max 5x). Code-named buckets and extra_usage are
    // present so the test pins that they are ignored.
    private const string Sample = """
        {
          "five_hour": {"utilization": 42.5, "resets_at": "2026-06-14T15:00:00Z"},
          "seven_day": {"utilization": 8.0, "resets_at": null},
          "seven_day_oauth_apps": null,
          "seven_day_opus": null,
          "seven_day_sonnet": {"utilization": 3.0, "resets_at": null},
          "extra_usage": {"is_enabled": false, "monthly_limit": null, "used_credits": null}
        }
        """;

    // ---- [S-633] parsing -----------------------------------------------------------------------

    [Fact]
    public void S633_parses_windows_and_ignores_unknown_buckets()
    {
        var parsed = ClaudeUsageRules.Parse(Sample);

        Assert.Null(parsed.Error);
        var usage = parsed.Report!;
        Assert.Equal(42.5, usage.FiveHour.Utilization);
        Assert.Equal(new DateTimeOffset(2026, 6, 14, 15, 0, 0, TimeSpan.Zero), usage.FiveHour.ResetsAt);
        Assert.Equal(8.0, usage.SevenDay.Utilization);
        Assert.Null(usage.SevenDay.ResetsAt);
        Assert.Null(usage.Opus);
        Assert.Equal(3.0, usage.Sonnet!.Utilization);
    }

    [Fact]
    public void S633_an_empty_object_is_an_unused_subscription()
    {
        var usage = ClaudeUsageRules.Parse("{}").Report!;

        Assert.Equal(0, usage.FiveHour.Utilization);
        Assert.Null(usage.SevenDay.ResetsAt);
        Assert.Null(usage.Sonnet);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("[1,2]")]
    [InlineData("null")]
    [InlineData("")]
    public void S633_a_malformed_body_is_an_error_not_a_zero_default(string body)
    {
        var parsed = ClaudeUsageRules.Parse(body);

        Assert.Null(parsed.Report);
        Assert.False(string.IsNullOrEmpty(parsed.Error));
    }

    // ---- [S-634] errors ------------------------------------------------------------------------

    [Fact]
    public void S634_401_is_reconnect_not_permission()
    {
        var message = ClaudeUsageRules.MapError(401, "Invalid authentication credentials");

        Assert.Contains("reconnect", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("permission", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void S634_403_surfaces_anthropics_reason_and_does_not_say_reconnect()
    {
        var message = ClaudeUsageRules.MapError(403, "OAuth token does not meet scope requirement user:profile");

        Assert.Contains("user:profile", message, StringComparison.Ordinal);
        Assert.DoesNotContain("reconnect", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("permission", ClaudeUsageRules.MapError(403, null), StringComparison.Ordinal);
    }

    [Fact]
    public void S634_anything_else_names_the_status()
    {
        Assert.Equal("Anthropic answered HTTP 500: boom", ClaudeUsageRules.MapError(500, "boom"));
        Assert.Equal("Anthropic answered HTTP 502", ClaudeUsageRules.MapError(502, "  "));
    }

    // ---- [S-631] percentage and severity -------------------------------------------------------

    [Fact]
    public void S631_utilization_is_already_a_percentage()
    {
        Assert.Equal(42.5, ClaudeUsageRules.Percent(42.5));
        Assert.Equal(8, ClaudeUsageRules.Percent(8));

        // Classic U-01: sub-1% readings were read as a fraction and drew a full red bar.
        Assert.Equal(1, ClaudeUsageRules.Percent(1));
        Assert.Equal(0.62, ClaudeUsageRules.Percent(0.62), 6);
    }

    [Fact]
    public void S631_clamps_and_floors_junk()
    {
        Assert.Equal(100, ClaudeUsageRules.Percent(150));
        Assert.Equal(0, ClaudeUsageRules.Percent(-5));
        Assert.Equal(0, ClaudeUsageRules.Percent(double.NaN));
        Assert.Equal(0, ClaudeUsageRules.Percent(double.PositiveInfinity));
    }

    [Theory]
    [InlineData(0, UsageSeverity.Ok)]
    [InlineData(69.9, UsageSeverity.Ok)]
    [InlineData(70, UsageSeverity.Warn)]
    [InlineData(89.9, UsageSeverity.Warn)]
    [InlineData(90, UsageSeverity.Critical)]
    [InlineData(100, UsageSeverity.Critical)]
    public void S631_severity_buckets(double percent, UsageSeverity severity)
        => Assert.Equal(severity, ClaudeUsageRules.Severity(percent));

    // ---- [S-632] reset ---------------------------------------------------------------------------

    [Fact]
    public void S632_reset_labels()
    {
        Assert.Equal(string.Empty, ClaudeUsageRules.ResetLabel(null, Now));
        Assert.Equal(string.Empty, ClaudeUsageRules.ResetLabel(Now.AddHours(-1), Now));
        Assert.Equal("resets in 45m", ClaudeUsageRules.ResetLabel(Now.AddMinutes(45), Now));
        Assert.Equal("resets in 3h 20m", ClaudeUsageRules.ResetLabel(Now.AddHours(3).AddMinutes(20), Now));
        Assert.Equal("resets in 2d 2h", ClaudeUsageRules.ResetLabel(Now.AddDays(2).AddHours(2), Now));
        Assert.Equal("3h 20m", ClaudeUsageRules.ResetLabel(Now.AddHours(3).AddMinutes(20), Now, brief: true));
    }

    [Fact]
    public void S633_an_unreadable_reset_time_is_no_reset()
    {
        var usage = ClaudeUsageRules.Parse("""{"five_hour":{"utilization":1,"resets_at":"not-a-date"}}""").Report!;
        Assert.Null(usage.FiveHour.ResetsAt);
    }

    // ---- [S-636] cadence -----------------------------------------------------------------------

    [Fact]
    public void S636_refresh_clamps_and_defaults_to_fifteen()
    {
        Assert.Equal(5, ClaudeUsageRules.ClampRefreshMinutes(1));
        Assert.Equal(60, ClaudeUsageRules.ClampRefreshMinutes(999));
        Assert.Equal(15, ClaudeUsageRules.ClampRefreshMinutes(null));
        Assert.Equal(15, new ClaudeUsageConfig().RefreshMinutes);
        Assert.Equal(TimeSpan.FromMinutes(60), new ClaudeUsageConfig { RefreshMinutes = 600 }.RefreshInterval);
    }

    // ---- [S-630] rows --------------------------------------------------------------------------

    private static readonly ClaudeUsageReport Base = new(new UsageWindow(10, null), new UsageWindow(20, null), null, null);

    [Fact]
    public void S630_two_base_windows_always_and_models_when_toggled()
    {
        Assert.Equal(["five_hour", "seven_day"], ClaudeUsageRules.BuildBars(Base, false, false).Select(b => b.Key));

        var bars = ClaudeUsageRules.BuildBars(Base, true, true);
        Assert.Equal(["five_hour", "seven_day", "opus", "sonnet"], bars.Select(b => b.Key));
        Assert.Equal(UsageWindow.Unused, bars[2].Window);
        Assert.Equal(UsageWindow.Unused, bars[3].Window);
    }

    [Fact]
    public void S630_a_model_window_the_endpoint_sends_is_used()
    {
        var bars = ClaudeUsageRules.BuildBars(Base with { Opus = new UsageWindow(55, null) }, true, false);
        Assert.Equal(55, bars[2].Window.Utilization);
    }

    [Fact]
    public void S635_every_label_width_stays_distinct()
    {
        var bars = ClaudeUsageRules.BuildBars(Base, true, true);

        foreach (var size in Enum.GetValues<UsageLabelSize>())
        {
            var labels = bars.Select(b => ClaudeUsageRules.Label(b, size)).ToList();
            Assert.All(labels, l => Assert.NotEmpty(l));
            Assert.Equal(bars.Count, labels.Distinct().Count());
        }

        Assert.Equal(["5h", "7d", "Opus", "Son."], bars.Select(b => ClaudeUsageRules.Label(b, UsageLabelSize.Mini)));
    }

    // ---- [S-635] fitting -----------------------------------------------------------------------

    // The per-row heights the view draws at; a box of exactly this height must still hold it.
    private static double Need(UsageDensity density, int rows) => density switch
    {
        UsageDensity.Full => (rows * 50) + ((rows - 1) * 12),
        UsageDensity.Compact => (rows * 30) + ((rows - 1) * 8),
        _ => (rows * 16) + ((rows - 1) * 6),
    };

    [Fact]
    public void S635_picks_the_loosest_layout_the_box_holds()
    {
        Assert.Equal(UsageDensity.Full, ClaudeUsageRules.Density(Need(UsageDensity.Full, 2), 2));
        Assert.Equal(UsageDensity.Full, ClaudeUsageRules.Density(500, 4));
    }

    [Fact]
    public void S635_steps_down_exactly_at_the_budget()
    {
        Assert.Equal(UsageDensity.Full, ClaudeUsageRules.Density(Need(UsageDensity.Full, 3), 3));
        Assert.Equal(UsageDensity.Compact, ClaudeUsageRules.Density(Need(UsageDensity.Full, 3) - 1, 3));
        Assert.Equal(UsageDensity.Compact, ClaudeUsageRules.Density(Need(UsageDensity.Compact, 3), 3));
        Assert.Equal(UsageDensity.Tight, ClaudeUsageRules.Density(Need(UsageDensity.Compact, 3) - 1, 3));
        Assert.Equal(UsageDensity.Tight, ClaudeUsageRules.Density(Need(UsageDensity.Tight, 3), 3));
        Assert.Equal(UsageDensity.Micro, ClaudeUsageRules.Density(Need(UsageDensity.Tight, 3) - 1, 3));
    }

    [Fact]
    public void S635_more_rows_in_the_same_box_cost_a_layer_each()
    {
        const double box = 135;
        Assert.Equal(UsageDensity.Full, ClaudeUsageRules.Density(box, 2));
        Assert.Equal(UsageDensity.Compact, ClaudeUsageRules.Density(box, 3));
        Assert.Equal(UsageDensity.Tight, ClaudeUsageRules.Density(box, 4));
        Assert.Equal(UsageDensity.Tight, ClaudeUsageRules.Density(box, 6));
        Assert.Equal(UsageDensity.Micro, ClaudeUsageRules.Density(box, 7));
    }

    [Fact]
    public void S635_text_scale_counts_and_junk_scale_does_not_collapse_the_budget()
    {
        var box = Need(UsageDensity.Full, 3);
        Assert.Equal(UsageDensity.Full, ClaudeUsageRules.Density(box, 3, 1));
        Assert.Equal(UsageDensity.Compact, ClaudeUsageRules.Density(box, 3, 1.4));
        Assert.Equal(UsageDensity.Full, ClaudeUsageRules.Density(box, 3, 0));
        Assert.Equal(UsageDensity.Full, ClaudeUsageRules.Density(box, 3, double.NaN));
    }

    [Fact]
    public void S635_unmeasured_is_loosest_and_a_measured_zero_is_micro()
    {
        Assert.Equal(UsageDensity.Full, ClaudeUsageRules.Density(double.NaN, 2));
        Assert.Equal(UsageDensity.Full, ClaudeUsageRules.Density(40, 0));
        Assert.Equal(UsageDensity.Micro, ClaudeUsageRules.Density(0, 4));
        Assert.Equal(UsageDensity.Micro, ClaudeUsageRules.Density(-5, 4));
    }

    [Fact]
    public void S635_labels_shorten_with_the_width_and_inline_rows_need_more()
    {
        var bars = ClaudeUsageRules.BuildBars(Base, false, false);

        Assert.Equal(UsageLabelSize.Full, ClaudeUsageRules.LabelSize(bars, 300, UsageDensity.Full));
        Assert.Equal(UsageLabelSize.Short, ClaudeUsageRules.LabelSize(bars, 140, UsageDensity.Full));
        Assert.Equal(UsageLabelSize.Mini, ClaudeUsageRules.LabelSize(bars, 70, UsageDensity.Full));

        // Same width: stacked spells it out, a one-line row cannot.
        Assert.Equal(UsageLabelSize.Full, ClaudeUsageRules.LabelSize(bars, 220, UsageDensity.Compact));
        Assert.NotEqual(UsageLabelSize.Full, ClaudeUsageRules.LabelSize(bars, 220, UsageDensity.Tight));

        Assert.Equal(UsageLabelSize.Full, ClaudeUsageRules.LabelSize(bars, double.NaN, UsageDensity.Tight));
        Assert.Equal(UsageLabelSize.Mini, ClaudeUsageRules.LabelSize(bars, 0, UsageDensity.Full));
    }
}
