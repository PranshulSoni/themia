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
    public void S491_a_wide_strip_is_one_row_with_names()
    {
        var plan = ViewSwitcherRules.Plan(480, 160, 3, iconOnly: false);

        Assert.False(plan.Vertical);
        Assert.True(plan.ShowNames);
    }

    [Fact]
    public void S491_a_narrow_strip_too_short_to_stack_drops_to_initials()
    {
        var plan = ViewSwitcherRules.Plan(240, 60, 3, iconOnly: false);

        Assert.False(plan.Vertical);
        Assert.False(plan.ShowNames);
    }

    [Theory]
    [InlineData(320, 240)]
    [InlineData(560, 420)]
    [InlineData(200, 150)]
    [InlineData(120, 120)]
    public void S491_a_tile_that_is_not_a_strip_stacks_with_names(double width, double height)
    {
        var plan = ViewSwitcherRules.Plan(width, height, 3, iconOnly: false);

        Assert.True(plan.Vertical);
        Assert.True(plan.ShowNames);
    }

    [Fact]
    public void S491_the_default_strip_is_one_row_with_names()
    {
        var plan = ViewSwitcherRules.Plan(320, 56, 3, iconOnly: false);

        Assert.False(plan.Vertical);
        Assert.True(plan.ShowNames);
    }

    [Fact]
    public void S491_a_tall_column_stacks_the_buttons_and_keeps_names()
    {
        var plan = ViewSwitcherRules.Plan(200, 400, 3, iconOnly: false);

        Assert.True(plan.Vertical);
        Assert.True(plan.ShowNames);
    }

    [Fact]
    public void S491_a_column_too_short_for_every_button_stays_a_row()
    {
        var plan = ViewSwitcherRules.Plan(60, 100, 5, iconOnly: false);

        Assert.False(plan.Vertical);
    }

    [Fact]
    public void S491_icon_only_never_shows_names()
    {
        Assert.False(ViewSwitcherRules.Plan(560, 420, 2, iconOnly: true).ShowNames);
        Assert.False(ViewSwitcherRules.Plan(200, 400, 2, iconOnly: true).ShowNames);
    }
}
