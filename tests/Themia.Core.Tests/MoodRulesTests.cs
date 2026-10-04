using Themia.Core.Localization;
using Themia.Core.Model;
using Themia.Core.Widgets;
using Xunit;

namespace Themia.Core.Tests;

/// <summary>
/// The heatmap's arithmetic: which bucket a day falls in, what a run of logged days is, and what a
/// hand-edited entry list is allowed to do to either.
/// <para>
/// The bucketing has one rule that carries the whole design: a day with something logged on it is
/// never bucket 0. If an awful day and an untouched day could share a square, the heatmap would be
/// saying two opposite things in the same colour.
/// </para>
/// </summary>
public class MoodRulesTests
{
    private static readonly TimeSpan Zone = TimeSpan.FromHours(2);

    private static readonly LocalDay Today = new(new DateOnly(2026, 6, 15));

    private static MoodEntry At(string day, int hour, int level, string note = "")
    {
        var date = DateOnly.ParseExact(day, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

        return new MoodEntry
        {
            Id = MoodEntry.NewId(),
            At = new DateTimeOffset(date.Year, date.Month, date.Day, hour, 0, 0, Zone),
            Level = level,
            Note = note,
        };
    }

    // -----------------------------------------------------------------------------------------
    // [S-321] bucketing
    // -----------------------------------------------------------------------------------------

    [Theory]
    [InlineData(1.0, 1)]
    [InlineData(1.4, 1)]
    [InlineData(1.5, 2)]    // away from zero, so the midpoint rounds up rather than to even
    [InlineData(2.5, 3)]
    [InlineData(3.0, 3)]
    [InlineData(4.49, 4)]
    [InlineData(4.5, 5)]
    [InlineData(5.0, 5)]
    public void S321_an_average_rounds_into_one_of_the_five_buckets(double average, int expected)
        => Assert.Equal(expected, MoodRules.Bucket(average));

    [Theory]
    [InlineData(-3.0)]
    [InlineData(0.0)]
    [InlineData(0.4)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void S321_a_bucket_is_never_below_one_however_odd_the_average(double average)
    {
        var bucket = MoodRules.Bucket(average);

        Assert.InRange(bucket, MoodRules.MinLevel, MoodRules.MaxLevel);
    }

    [Fact]
    public void S321_a_logged_day_is_never_bucket_zero_and_an_unlogged_one_always_is()
    {
        var byDay = MoodRules.ByDay([At("2026-06-15", 9, 1)]);

        Assert.Equal(1, MoodRules.Read(byDay, Today).Bucket);
        Assert.Equal(0, MoodRules.Read(byDay, Today.AddDays(-1)).Bucket);
        Assert.Equal(0, MoodRules.Read(byDay, Today.AddDays(-1)).Count);
    }

    [Fact]
    public void S321_several_entries_on_one_day_average_into_one_bucket()
    {
        var byDay = MoodRules.ByDay([At("2026-06-15", 8, 1), At("2026-06-15", 13, 4), At("2026-06-15", 20, 4)]);
        var day = MoodRules.Read(byDay, Today);

        Assert.Equal(3, day.Count);
        Assert.Equal(3.0, day.Average, 3);
        Assert.Equal(3, day.Bucket);
    }

    // -----------------------------------------------------------------------------------------
    // [S-322] the value is available without colour
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void S322_a_day_describes_itself_in_words()
    {
        var byDay = MoodRules.ByDay([At("2026-06-15", 9, 4)]);
        var described = MoodRules.Describe(Locale.En, MoodRules.Read(byDay, Today));

        Assert.Equal("Monday, June 15, 2026: Good (4 of 5)", described);
        Assert.Equal("Montag, 15. Juni 2026: Gut (4 von 5)", MoodRules.Describe(Locale.De, MoodRules.Read(byDay, Today)));
    }

    [Fact]
    public void S322_a_day_with_several_entries_says_how_many_and_the_mean()
    {
        var byDay = MoodRules.ByDay([At("2026-06-15", 9, 4), At("2026-06-15", 18, 3)]);
        var described = MoodRules.Describe(Locale.En, MoodRules.Read(byDay, Today));

        Assert.Equal("Monday, June 15, 2026: Good (4 of 5), average 3.5 of 2 entries", described);
        Assert.Equal("Montag, 15. Juni 2026: Gut (4 von 5), Durchschnitt 3,5 aus 2 Einträgen", MoodRules.Describe(Locale.De, MoodRules.Read(byDay, Today)));
    }

    [Fact]
    public void S322_an_empty_day_says_so_rather_than_saying_nothing()
        => Assert.Equal(
            "Monday, June 15, 2026: nothing logged",
            MoodRules.Describe(Locale.En, MoodRules.Read(new Dictionary<LocalDay, MoodDay>(), Today)));

    [Theory]
    [InlineData(1, "Awful")]
    [InlineData(3, "OK")]
    [InlineData(5, "Great")]
    [InlineData(0, "Not logged")]
    [InlineData(9, "Not logged")]
    public void S322_every_level_has_a_name(int level, string expected)
        => Assert.Equal(expected, MoodRules.LevelName(Locale.En, level));

    // -----------------------------------------------------------------------------------------
    // [S-323] the entry list is untrusted
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void S323_an_entry_outside_the_scale_is_dropped_and_its_siblings_kept()
    {
        var kept = MoodRules.Sanitise([At("2026-06-15", 9, 0), At("2026-06-15", 10, 3), At("2026-06-15", 11, 6)]);

        Assert.Single(kept);
        Assert.Equal(3, kept[0].Level);
    }

    [Fact]
    public void S323_a_null_entry_and_a_null_list_are_both_empty()
    {
        Assert.Empty(MoodRules.Sanitise(null));
        Assert.Empty(MoodRules.Sanitise([null, null]));
    }

    [Fact]
    public void S323_entries_come_back_newest_first_whatever_order_they_arrived_in()
    {
        var sorted = MoodRules.Sanitise([At("2026-06-13", 9, 2), At("2026-06-15", 9, 4), At("2026-06-14", 9, 3)]);

        Assert.Equal(new[] { 4, 3, 2 }, sorted.Select(entry => entry.Level));
    }

    [Fact]
    public void S323_pruning_keeps_the_newest_however_the_list_was_ordered()
    {
        var entries = new List<MoodEntry>
        {
            At("2026-06-13", 9, 2),
            At("2026-06-15", 9, 4),
            At("2026-06-14", 9, 3),
        };

        MoodRules.Prune(entries, 2);

        Assert.Equal(2, entries.Count);
        Assert.DoesNotContain(entries, entry => entry.Level == 2);
    }

    [Fact]
    public void S323_pruning_a_short_list_changes_nothing()
    {
        var entries = new List<MoodEntry> { At("2026-06-15", 9, 4) };

        MoodRules.Prune(entries, 10);

        Assert.Single(entries);
    }

    // -----------------------------------------------------------------------------------------
    // [S-324] the day an entry counts for, and the run of logged days
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void S324_a_late_evening_entry_counts_for_that_evening()
    {
        // Eleven at night at +02:00. In UTC it is already tomorrow.
        var byDay = MoodRules.ByDay([At("2026-06-15", 23, 5)]);

        Assert.Equal(1, MoodRules.Read(byDay, Today).Count);
        Assert.Equal(0, MoodRules.Read(byDay, Today.AddDays(1)).Count);
    }

    [Fact]
    public void S324_a_run_ending_yesterday_survives_an_unlogged_today()
    {
        var byDay = MoodRules.ByDay([At("2026-06-13", 9, 3), At("2026-06-14", 9, 3)]);

        Assert.Equal(2, MoodRules.Streak(byDay, Today));
    }

    [Fact]
    public void S324_a_gap_before_yesterday_ends_the_run()
    {
        var byDay = MoodRules.ByDay([At("2026-06-12", 9, 3), At("2026-06-13", 9, 3)]);

        Assert.Equal(0, MoodRules.Streak(byDay, Today));
    }

    [Fact]
    public void S324_a_run_spans_a_daylight_saving_change()
    {
        // 28 March at +01:00, then two days at +02:00 after the clocks went forward. Three
        // consecutive squares, not two and a bit.
        var entries = new List<MoodEntry>
        {
            new() { Id = "a", At = new DateTimeOffset(2026, 3, 28, 23, 30, 0, TimeSpan.FromHours(1)), Level = 3 },
            new() { Id = "b", At = new DateTimeOffset(2026, 3, 29, 23, 30, 0, TimeSpan.FromHours(2)), Level = 3 },
            new() { Id = "c", At = new DateTimeOffset(2026, 3, 30, 9, 0, 0, TimeSpan.FromHours(2)), Level = 3 },
        };

        var byDay = MoodRules.ByDay(entries);

        Assert.Equal(3, byDay.Count);
        Assert.Equal(3, MoodRules.Streak(byDay, new LocalDay(new DateOnly(2026, 3, 30))));
    }

    [Fact]
    public void S324_an_empty_log_has_no_run()
        => Assert.Equal(0, MoodRules.Streak(new Dictionary<LocalDay, MoodDay>(), Today));

    // -----------------------------------------------------------------------------------------
    // [S-325] the average over a window
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void S325_an_empty_window_has_no_average_rather_than_a_zero()
    {
        var average = MoodRules.Average([At("2026-01-01", 9, 5)], Today.AddDays(-6), Today);

        Assert.Null(average);
    }

    [Fact]
    public void S325_the_average_counts_every_entry_not_every_day()
    {
        // Two on one day and one on another: the mean is over the three entries.
        var entries = new List<MoodEntry> { At("2026-06-14", 9, 1), At("2026-06-14", 18, 2), At("2026-06-15", 9, 3) };
        var average = MoodRules.Average(entries, Today.AddDays(-6), Today);

        Assert.NotNull(average);
        Assert.Equal(2.0, average!.Value, 3);
    }

    [Fact]
    public void S325_entries_outside_the_window_are_ignored_not_folded_onto_an_edge_day()
    {
        var entries = new List<MoodEntry> { At("2026-06-01", 9, 5), At("2026-06-15", 9, 1) };
        var average = MoodRules.Average(entries, Today.AddDays(-2), Today);

        Assert.NotNull(average);
        Assert.Equal(1.0, average!.Value, 3);
    }

    // -----------------------------------------------------------------------------------------
    // [S-326] which entry the thought box edits
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void S326_the_thought_box_edits_todays_most_recent_entry()
    {
        var entries = MoodRules.Sanitise([At("2026-06-15", 8, 2, "morning"), At("2026-06-15", 20, 4, "evening")]);
        var latest = MoodRules.LatestOn(entries, Today);

        Assert.NotNull(latest);
        Assert.Equal("evening", latest!.Note);
    }

    [Fact]
    public void S326_with_nothing_logged_today_there_is_nothing_to_edit()
    {
        var entries = MoodRules.Sanitise([At("2026-06-14", 20, 4)]);

        Assert.Null(MoodRules.LatestOn(entries, Today));
    }
}
