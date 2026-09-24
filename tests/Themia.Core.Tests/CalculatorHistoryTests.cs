using Themia.Core.Widgets;
using Xunit;

namespace Themia.Core.Tests;

public class CalculatorHistoryTests
{
    [Fact]
    public void S09_newest_entry_is_first()
    {
        var history = new CalculatorHistory();

        history.Add("1+1", "2");
        history.Add("2+2", "4");

        Assert.Equal("2+2", history.Entries[0].Expression);
        Assert.Equal("1+1", history.Entries[1].Expression);
    }

    [Fact]
    public void S09_history_is_capped_at_eight_and_drops_the_oldest()
    {
        var history = new CalculatorHistory();

        for (var i = 1; i <= 12; i++)
        {
            history.Add($"{i}+0", i.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        Assert.Equal(CalculatorHistory.Capacity, history.Count);
        Assert.Equal("12", history.Entries[0].Result);
        Assert.Equal("5", history.Entries[^1].Result);
    }

    [Theory]
    [InlineData("", "5")]
    [InlineData("   ", "5")]
    [InlineData("2+3", "")]
    public void S10_nothing_empty_gets_committed(string expression, string result)
    {
        var history = new CalculatorHistory();

        Assert.False(history.Add(expression, result));
        Assert.Empty(history.Entries);
    }

    [Fact]
    public void S10_a_committed_entry_reports_that_it_was_taken()
    {
        var history = new CalculatorHistory();

        Assert.True(history.Add("2+3", "5"));
        Assert.Single(history.Entries);
    }

    [Fact]
    public void S11_the_stored_result_is_what_clicking_the_row_types_back()
    {
        var history = new CalculatorHistory();
        var result = ExpressionEvaluator.Evaluate("10/3");

        history.Add("10/3", result.Format(4));

        // Round trip: the text a row hands back must itself be a valid expression.
        var again = ExpressionEvaluator.Evaluate(history.Entries[0].Result);

        Assert.Equal(ExpressionStatus.Value, again.Status);
        Assert.Equal(3.3333, again.Value, 4);
    }

    [Fact]
    public void S09_clearing_empties_the_list()
    {
        var history = new CalculatorHistory();
        history.Add("1+1", "2");

        history.Clear();

        Assert.Empty(history.Entries);
    }
}
