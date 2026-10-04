using Themia.Core.Localization;
using Themia.Core.Widgets;
using Xunit;

namespace Themia.Core.Tests;

public sealed class ViewSwitcherRulesTests
{
    private static readonly ViewSummary[] Views =
    [
        new("a", "Work"),
        new("b", "Home office"),
        new("c", "Gaming"),
    ];

    [Theory]
    [InlineData("Work", "WO")]
    [InlineData("Home office", "HO")]
    [InlineData("  spaced   out  name ", "SO")]
    [InlineData("x", "X")]
    [InlineData("", "?")]
    [InlineData("   ", "?")]
    [InlineData(null, "?")]
    public void S489_initials_take_two_words_or_two_letters(string? name, string expected)
        => Assert.Equal(expected, ViewSwitcherRules.Initials(name));

    [Fact]
    public void S489_initials_do_not_split_a_character_built_from_two_code_points()
    {
        // "e" followed by a combining acute accent is one character on screen.
        var initials = ViewSwitcherRules.Initials("école");

        Assert.Equal("ÉC", initials);
    }

    [Fact]
    public void S488_an_empty_selection_offers_every_view_in_view_order()
    {
        var choices = ViewSwitcherRules.Choices(Views, [], "b");

        Assert.Equal(["a", "b", "c"], choices.Select(c => c.Id));
        Assert.Equal([false, true, false], choices.Select(c => c.Shown));
    }

    [Fact]
    public void S488_a_selection_keeps_view_order_not_selection_order()
    {
        var choices = ViewSwitcherRules.Choices(Views, ["c", "a"], null);

        Assert.Equal(["a", "c"], choices.Select(c => c.Id));
    }

    [Fact]
    public void S488_a_selection_naming_only_deleted_views_falls_back_to_all()
    {
        var choices = ViewSwitcherRules.Choices(Views, ["gone", "also-gone"], "a");

        Assert.Equal(3, choices.Count);
    }

    [Fact]
    public void S488_deleted_ids_in_a_selection_are_ignored()
    {
        var choices = ViewSwitcherRules.Choices(Views, ["gone", "b"], "a");

        Assert.Equal(["b"], choices.Select(c => c.Id));
    }

    [Fact]
    public void S490_unticking_a_view_leaves_the_rest_in_view_order()
    {
        var next = ViewSwitcherRules.Toggle(Views, [], "b", on: false);

        Assert.Equal(["a", "c"], next);
    }

    [Fact]
    public void S490_the_last_view_cannot_be_unticked()
    {
        var next = ViewSwitcherRules.Toggle(Views, ["a"], "a", on: false);

        Assert.Equal(["a"], next);
    }

    [Fact]
    public void S490_ticking_every_view_goes_back_to_all()
    {
        var next = ViewSwitcherRules.Toggle(Views, ["a", "b"], "c", on: true);

        Assert.Empty(next);
    }

    [Fact]
    public void S1657_choices_carry_each_views_widget_count()
    {
        var choices = ViewSwitcherRules.Choices([new("a", "Work", null, 4), new("b", "Home", null, -2)], [], "a");

        Assert.Equal([4, 0], choices.Select(c => c.WidgetCount));
    }

    [Theory]
    [InlineData(0, "No widgets")]
    [InlineData(-1, "No widgets")]
    [InlineData(1, "1 widget")]
    [InlineData(7, "7 widgets")]
    public void S1657_the_count_line_reads_naturally(int count, string expected)
        => Assert.Equal(expected, ViewSwitcherRules.CountText(Locale.En, count));

    [Theory]
    [InlineData(Locale.De, 0, "Keine Widgets")]
    [InlineData(Locale.De, 1, "1 Widget")]
    [InlineData(Locale.De, 7, "7 Widgets")]
    [InlineData(Locale.Pl, 3, "3 widżety")]
    [InlineData(Locale.Pl, 5, "5 widżetów")]
    public void S1657_the_count_line_follows_the_language(Locale locale, int count, string expected)
        => Assert.Equal(expected, ViewSwitcherRules.CountText(locale, count));
}
