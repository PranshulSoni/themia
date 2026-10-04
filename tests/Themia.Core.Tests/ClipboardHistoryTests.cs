using Themia.Core.Localization;
using Themia.Core.Widgets;
using Xunit;

namespace Themia.Core.Tests;

/// <summary>
/// The clipboard history's rules. The bounds are the security property here, not a nicety: this
/// list holds whatever was copied, which regularly includes a password, so "how much is kept" and
/// "what is refused outright" are the two things worth pinning.
/// </summary>
public class ClipboardHistoryTests
{
    private static readonly DateTimeOffset At = new(2026, 9, 24, 10, 0, 0, TimeSpan.Zero);

    private static ClipboardHistory New(int capacity = ClipboardHistory.DefaultCapacity)
        => new() { Capacity = capacity };

    [Fact]
    public void The_most_recent_entry_is_first()
    {
        var history = New();

        history.Add("one", At);
        history.Add("two", At.AddSeconds(1));
        history.Add("three", At.AddSeconds(2));

        Assert.Equal(["three", "two", "one"], history.Entries.Select(e => e.Text));
        Assert.Equal("three", history.Latest);
    }

    [Fact]
    public void Re_copying_something_moves_it_to_the_top_rather_than_adding_a_second_row()
    {
        var history = New();

        history.Add("one", At);
        history.Add("two", At.AddSeconds(1));
        history.Add("one", At.AddSeconds(2));

        Assert.Equal(["one", "two"], history.Entries.Select(e => e.Text));
        Assert.Equal(At.AddSeconds(2), history.Entries[0].CapturedAt);
    }

    [Fact]
    public void Being_told_again_about_what_is_already_on_top_changes_nothing()
    {
        // Windows raises the clipboard change several times for a single copy in some apps.
        var history = New();

        Assert.True(history.Add("one", At));
        Assert.False(history.Add("one", At.AddSeconds(1)));
        Assert.Equal(At, history.Entries[0].CapturedAt);
    }

    [Fact]
    public void Two_entries_that_differ_only_by_case_are_two_entries()
    {
        var history = New();

        history.Add("Secret", At);
        history.Add("secret", At.AddSeconds(1));

        Assert.Equal(2, history.Count);
    }

    [Fact]
    public void The_oldest_entries_fall_off_the_end_at_the_capacity()
    {
        var history = New(capacity: ClipboardHistory.MinCapacity);

        for (var i = 0; i < ClipboardHistory.MinCapacity + 3; i++)
        {
            history.Add($"entry {i}", At.AddSeconds(i));
        }

        Assert.Equal(ClipboardHistory.MinCapacity, history.Count);
        Assert.Equal($"entry {ClipboardHistory.MinCapacity + 2}", history.Latest);
        Assert.DoesNotContain(history.Entries, e => e.Text == "entry 0");
    }

    [Fact]
    public void Lowering_the_capacity_forgets_the_oldest_straight_away()
    {
        // The one setting that genuinely reduces exposure has to take effect when it is asked for,
        // not at the next copy.
        var history = New(capacity: 20);

        for (var i = 0; i < 20; i++)
        {
            history.Add($"entry {i}", At.AddSeconds(i));
        }

        history.Capacity = ClipboardHistory.MinCapacity;

        Assert.Equal(ClipboardHistory.MinCapacity, history.Count);
        Assert.Equal("entry 19", history.Latest);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(int.MaxValue)]
    [InlineData(int.MinValue)]
    public void A_capacity_from_a_hand_edited_config_is_clamped(int configured)
    {
        var history = New();

        history.Capacity = configured;

        Assert.InRange(history.Capacity, ClipboardHistory.MinCapacity, ClipboardHistory.MaxCapacity);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    public void Whitespace_is_never_worth_a_row(string? text)
    {
        var history = New();

        Assert.False(history.Add(text, At));
        Assert.Equal(0, history.Count);
    }

    [Fact]
    public void An_entry_longer_than_the_ceiling_is_dropped_rather_than_truncated()
    {
        // Truncating would mean a row whose click pastes back something other than what it shows.
        var history = New();

        Assert.False(history.Add(new string('a', ClipboardHistory.MaxEntryLength + 1), At));
        Assert.Equal(0, history.Count);

        Assert.True(history.Add(new string('a', ClipboardHistory.MaxEntryLength), At));
        Assert.Equal(1, history.Count);
    }

    [Fact]
    public void The_total_size_is_bounded_even_when_the_entry_count_is_not()
    {
        // A capacity of 25 sounds bounded until somebody copies a very large file twice.
        var history = New(capacity: ClipboardHistory.MaxCapacity);

        var big = ClipboardHistory.MaxEntryLength;
        var needed = (ClipboardHistory.MaxTotalCharacters / big) + 4;

        for (var i = 0; i < needed; i++)
        {
            history.Add(i.ToString() + new string('a', big - 8), At.AddSeconds(i));
        }

        Assert.True(history.Count < needed);
        Assert.True(history.Entries.Sum(e => (long)e.Text.Length) <= ClipboardHistory.MaxTotalCharacters);
    }

    [Fact]
    public void An_entry_is_stored_exactly_as_it_was_copied()
    {
        // Whatever is displayed, copying a row back must give the same string.
        const string Text = "  line one\r\n\tline two   ";

        var history = New();
        history.Add(Text, At);

        Assert.Equal(Text, history.Entries[0].Text);
    }

    [Fact]
    public void Clearing_forgets_everything_and_says_whether_there_was_anything()
    {
        var history = New();

        Assert.False(history.Clear());

        history.Add("one", At);

        Assert.True(history.Clear());
        Assert.Equal(0, history.Count);
        Assert.Null(history.Latest);
    }

    [Fact]
    public void Removing_one_entry_leaves_the_rest_in_order()
    {
        var history = New();

        history.Add("one", At);
        history.Add("two", At.AddSeconds(1));
        history.Add("three", At.AddSeconds(2));

        Assert.True(history.Remove("two"));
        Assert.False(history.Remove("two"));
        Assert.Equal(["three", "one"], history.Entries.Select(e => e.Text));
    }

    [Fact]
    public void Clearing_after_a_remove_still_leaves_room_for_new_entries()
    {
        // The total-size counter is easy to get out of step with the list; if it did, the history
        // would silently stop keeping anything.
        var history = New();

        history.Add("one", At);
        history.Remove("one");
        history.Add("two", At.AddSeconds(1));

        Assert.Equal(1, history.Count);
        Assert.Equal("two", history.Latest);
    }
}

/// <summary>How an entry reads in a row, which is separate from how it is stored.</summary>
public class ClipboardFormattingTests
{
    [Theory]
    [InlineData("hello", "hello")]
    [InlineData("  hello  ", "hello")]
    [InlineData("line one\nline two", "line one line two")]
    [InlineData("a\r\n\t \tb", "a b")]
    [InlineData("\n\n\n", "")]
    public void A_preview_is_one_line_with_runs_of_whitespace_collapsed(string text, string expected)
    {
        Assert.Equal(expected, ClipboardFormatting.Preview(text, 120));
    }

    [Fact]
    public void A_preview_longer_than_the_room_is_cut_with_an_ellipsis()
    {
        var preview = ClipboardFormatting.Preview(new string('a', 500), 10);

        Assert.Equal(new string('a', 10) + '…', preview);
    }

    [Fact]
    public void A_preview_of_a_very_long_entry_never_exceeds_the_room_it_was_given()
    {
        var text = string.Join(' ', Enumerable.Repeat("word", 5000));

        var preview = ClipboardFormatting.Preview(text, 32);

        Assert.True(preview.Length <= 33, preview.Length.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Nothing_previews_as_nothing(string? text)
    {
        Assert.Equal(string.Empty, ClipboardFormatting.Preview(text, 120));
    }

    [Fact]
    public void A_zero_width_row_previews_as_nothing_rather_than_throwing()
    {
        Assert.Equal(string.Empty, ClipboardFormatting.Preview("hello", 0));
        Assert.Equal(string.Empty, ClipboardFormatting.Preview("hello", -4));
    }

    [Theory]
    [InlineData("a", "1 character")]
    [InlineData("hello", "5 characters")]
    [InlineData("one\ntwo", "2 lines")]
    [InlineData("one\ntwo\nthree", "3 lines")]
    public void A_description_says_how_much_there_is(string text, string expected)
    {
        Assert.Equal(expected, ClipboardFormatting.Describe(text, Locale.En));
    }
}
