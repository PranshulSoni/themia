using Themia.Core.Localization;
using Themia.Core.Widgets;
using Xunit;

namespace Themia.Core.Tests;

/// <summary>
/// The labelling and filing rules behind the calendar, tasks and contacts widgets. They used to live
/// inside the views, where a test could not reach them and "today" meant whatever day it happened to
/// be; every case here pins an explicit clock.
/// </summary>
public sealed class AgendaLabelTests
{
    private static readonly DateOnly Today = new(2026, 9, 24);

    // ---- day headings --------------------------------------------------------------------------

    [Fact]
    public void S105_TodayAndTomorrowAreNamed()
    {
        Assert.Equal("Today", Formatting.DayHeading(Locale.En, Today, Today));
        Assert.Equal("Tomorrow", Formatting.DayHeading(Locale.En, Today.AddDays(1), Today));
    }

    [Fact]
    public void S105_FurtherOutIsAWeekdayAndDate()
        => Assert.Equal("Sun, Sep 27", Formatting.DayHeading(Locale.En, Today.AddDays(3), Today));

    /// <summary>A heading is relative to the day passed in, never to the real clock.</summary>
    [Fact]
    public void S105_HeadingIsRelativeToTheGivenDay()
        => Assert.Equal("Today", Formatting.DayHeading(Locale.En, new DateOnly(2001, 1, 1), new DateOnly(2001, 1, 1)));

    [Fact]
    public void S105_APastDayIsJustItsDate()
        => Assert.Equal("Wed, Sep 23", Formatting.DayHeading(Locale.En, Today.AddDays(-1), Today));

    [Fact]
    public void S105_YearBoundaryStillReadsAsTomorrow()
    {
        var newYearsEve = new DateOnly(2026, 12, 31);
        Assert.Equal("Tomorrow", Formatting.DayHeading(Locale.En, new DateOnly(2027, 1, 1), newYearsEve));
    }

    // ---- due dates -----------------------------------------------------------------------------

    /// <summary>Overdue must be readable without colour, so it is said in words. [S-106]</summary>
    [Fact]
    public void S106_OverdueIsSaidInWords()
        => Assert.Equal("Overdue · Sep 22", Formatting.DueDate(Locale.En, new DateOnly(2026, 9, 22), Today, overdue: true));

    [Fact]
    public void S106_NotOverdueReadsLikeAHeading()
    {
        Assert.Equal("Today", Formatting.DueDate(Locale.En, Today, Today, overdue: false));
        Assert.Equal("Tomorrow", Formatting.DueDate(Locale.En, Today.AddDays(1), Today, overdue: false));
        Assert.Equal("Sun, Sep 27", Formatting.DueDate(Locale.En, Today.AddDays(3), Today, overdue: false));
    }

    /// <summary>
    /// Overdue wins over "Today": a task due today and already marked overdue is overdue, and
    /// hiding that behind the friendlier word is exactly the wrong way round.
    /// </summary>
    [Fact]
    public void S106_OverdueWinsOverToday()
        => Assert.Equal("Overdue · Sep 24", Formatting.DueDate(Locale.En, Today, Today, overdue: true));

    // ---- filing --------------------------------------------------------------------------------

    [Fact]
    public void S105_AnEventStillRunningIsFiledUnderToday()
        => Assert.Equal(Today, AgendaRules.FiledUnder(Today.AddDays(-2), Today));

    [Fact]
    public void S105_FutureAndCurrentDaysKeepTheirOwnHeading()
    {
        Assert.Equal(Today, AgendaRules.FiledUnder(Today, Today));
        Assert.Equal(Today.AddDays(5), AgendaRules.FiledUnder(Today.AddDays(5), Today));
    }

    // ---- in progress ---------------------------------------------------------------------------

    private static readonly DateTimeOffset Noon = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void S105_RunningEventIsInProgress()
        => Assert.True(AgendaRules.IsInProgress(false, Noon.AddMinutes(-30), Noon.AddMinutes(30), Noon));

    [Fact]
    public void S105_BoundariesAreHalfOpen()
    {
        // Starting exactly now has started; ending exactly now has ended.
        Assert.True(AgendaRules.IsInProgress(false, Noon, Noon.AddMinutes(30), Noon));
        Assert.False(AgendaRules.IsInProgress(false, Noon.AddMinutes(-30), Noon, Noon));
    }

    [Fact]
    public void S105_NotYetStartedIsNotInProgress()
        => Assert.False(AgendaRules.IsInProgress(false, Noon.AddMinutes(1), Noon.AddMinutes(30), Noon));

    /// <summary>An event with no stated end has not been stated to have finished.</summary>
    [Fact]
    public void S105_NoEndMeansStillRunning()
        => Assert.True(AgendaRules.IsInProgress(false, Noon.AddHours(-3), null, Noon));

    /// <summary>
    /// An all-day entry is a label on a date, not a span of the clock. Treating it as in progress
    /// would mark every one of them "Now" from midnight onwards.
    /// </summary>
    [Fact]
    public void S105_AllDayIsNeverInProgress()
        => Assert.False(AgendaRules.IsInProgress(true, Noon.AddHours(-12), Noon.AddHours(12), Noon));

    [Fact]
    public void S105_NoStartIsNotInProgress()
        => Assert.False(AgendaRules.IsInProgress(false, null, Noon.AddHours(1), Noon));

    // ---- monograms -----------------------------------------------------------------------------

    [Theory]
    [InlineData("Ada Lovelace", "AL")]
    [InlineData("ada lovelace", "AL")]
    [InlineData("Ada", "A")]
    [InlineData("Ada Byron King Lovelace", "AL")]
    [InlineData("  Ada   Lovelace  ", "AL")]
    public void S102_MonogramTakesFirstAndLastWord(string name, string expected)
        => Assert.Equal(expected, Formatting.Monogram(name));

    /// <summary>A contact with nothing to take an initial from gets a placeholder, not a blank circle.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void S102_NothingToInitialiseGetsAPlaceholder(string? name)
        => Assert.Equal("?", Formatting.Monogram(name));

    /// <summary>Non-Latin names must still produce something; upper-casing a script without case is a no-op.</summary>
    [Fact]
    public void S102_MonogramHandlesNonLatinNames()
        => Assert.Equal("千葉", Formatting.Monogram("千代 葉子"));

    // ---- clock ---------------------------------------------------------------------------------

    /// <summary>
    /// 24-hour, always: the app builds with InvariantGlobalization, so there is no culture to read a
    /// 12-hour preference from.
    /// </summary>
    [Fact]
    public void S107_ClockIsAlwaysTwentyFourHour()
    {
        var evening = new DateTimeOffset(2026, 9, 24, 19, 5, 0, TimeSpan.Zero).ToLocalTime();
        Assert.Equal(evening.ToString("HH:mm"), Formatting.Clock24(Locale.En, evening));
        Assert.DoesNotContain("PM", Formatting.Clock24(Locale.En, evening), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ShortDateFollowsTheLanguage()
    {
        Assert.Equal("Jan 1", Formatting.ShortDate(Locale.En, new DateOnly(2026, 1, 1)));
        Assert.Equal("1. Jan.", Formatting.ShortDate(Locale.De, new DateOnly(2026, 1, 1)));
    }

    /// <summary>The headings and due dates in German: its own words for today and overdue, day before month.</summary>
    [Fact]
    public void S105_S106_HeadingsSpeakTheLanguage()
    {
        Assert.Equal("Heute", Formatting.DayHeading(Locale.De, Today, Today));
        Assert.Equal("Morgen", Formatting.DayHeading(Locale.De, Today.AddDays(1), Today));
        Assert.Equal("Überfällig · 22. Sept.", Formatting.DueDate(Locale.De, new DateOnly(2026, 9, 22), Today, overdue: true));
    }
}
