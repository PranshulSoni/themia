using System.Globalization;
using Themia.Core.Widgets;
using Xunit;

namespace Themia.Core.Tests;

/// <summary>
/// The calculator grammar, ported from the original widget's <c>evalExpr.test.ts</c> plus the cases
/// a C# port adds (invariant culture, a literal too big for a double, a nesting depth that would
/// otherwise take the process down).
/// </summary>
public class ExpressionEvaluatorTests
{
    private static double Number(string input)
    {
        var result = ExpressionEvaluator.Evaluate(input);
        Assert.Equal(ExpressionStatus.Value, result.Status);
        return result.Value;
    }

    private static void AssertError(string input)
        => Assert.Equal(ExpressionStatus.Error, ExpressionEvaluator.Evaluate(input).Status);

    private static void AssertEmpty(string? input)
        => Assert.Equal(ExpressionStatus.Empty, ExpressionEvaluator.Evaluate(input).Status);

    [Theory]
    [InlineData("2+3", 5)]
    [InlineData("10-4", 6)]
    [InlineData("10*4", 40)]
    [InlineData("20/5", 4)]
    public void S08_the_four_operators(string input, double expected) => Assert.Equal(expected, Number(input), 9);

    [Theory]
    [InlineData("2+3*4", 14)]
    [InlineData("(2+3)*4", 20)]
    [InlineData("2*3+4*5", 26)]
    [InlineData("2-3-4", -5)]
    [InlineData("100/5/2", 10)]
    public void S08_precedence_and_parentheses(string input, double expected)
        => Assert.Equal(expected, Number(input), 9);

    [Theory]
    [InlineData("-5", -5)]
    [InlineData("+7", 7)]
    [InlineData("3*-2", -6)]
    [InlineData("-(2+3)", -5)]
    [InlineData("--5", 5)]
    [InlineData("---5", -5)]
    [InlineData("2--3", 5)]
    public void S07_unary_sign_chains(string input, double expected) => Assert.Equal(expected, Number(input), 9);

    [Theory]
    [InlineData("2^10", 1024)]
    [InlineData("2^3^2", 512)]      // 2^(3^2), not (2^3)^2 = 64
    [InlineData("2^-3", 0.125)]
    [InlineData("-2^2", -4)]        // unary binds looser than ^, so this is -(2^2)
    [InlineData("(-2)^2", 4)]
    public void S07_power_is_right_associative(string input, double expected)
        => Assert.Equal(expected, Number(input), 9);

    [Theory]
    [InlineData("50%", 0.5)]
    [InlineData("200%", 2)]
    [InlineData("50%*4", 2)]
    [InlineData("1+10%", 1.1)]
    public void S07_percent_maps_to_divide_by_a_hundred(string input, double expected)
        => Assert.Equal(expected, Number(input), 9);

    [Theory]
    [InlineData("1.5+2.5", 4)]
    [InlineData(".5*2", 1)]
    [InlineData("5.", 5)]
    [InlineData("0.1+0.2", 0.30000000000000004)]
    public void S08_decimals(string input, double expected) => Assert.Equal(expected, Number(input), 15);

    [Theory]
    [InlineData("1,234.5", 1234.5)]
    [InlineData("1,000,000", 1000000)]
    public void S07_thousands_commas_are_dropped(string input, double expected)
        => Assert.Equal(expected, Number(input), 9);

    /// <summary>
    /// The app builds with InvariantGlobalization, and a comma is a thousands separator here. A
    /// decimal comma must therefore NOT quietly parse as a decimal point: "1,5" is fifteen.
    /// </summary>
    [Fact]
    public void S07_a_decimal_comma_is_never_a_decimal_point()
    {
        Assert.Equal(15, Number("1,5"), 9);
        Assert.Equal(15, Number("1.5") * 10, 9);
    }

    [Fact]
    public void S07_the_result_is_written_with_a_decimal_point_whatever_the_thread_culture()
    {
        var previous = CultureInfo.CurrentCulture;

        try
        {
            // A culture that writes 1,5 for one and a half. Under InvariantGlobalization this is a
            // best-effort alias, but the assertion holds either way: the output must be typeable
            // back into the box it came from.
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            Assert.Equal("1.5", ExpressionEvaluator.Evaluate("3/2").Format(4));
        }
        catch (CultureNotFoundException)
        {
            Assert.Equal("1.5", ExpressionEvaluator.Evaluate("3/2").Format(4));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData(",")]
    [InlineData("abc")]
    [InlineData("€ ")]
    public void S04_nothing_to_evaluate_is_empty_not_an_error(string? input) => AssertEmpty(input);

    [Fact]
    public void S07_unknown_characters_are_dropped_rather_than_failing()
        => Assert.Equal(15, Number("€12 + 3"), 9);

    [Theory]
    [InlineData("1/0")]
    [InlineData("0/0")]
    [InlineData("-1/0")]
    public void S08_division_by_zero_is_an_error_not_an_infinity(string input) => AssertError(input);

    [Theory]
    [InlineData("2^10000")]           // overflows to +infinity
    [InlineData("9^9^9")]
    public void S08_an_overflow_surfaces_as_an_error(string input) => AssertError(input);

    [Fact]
    public void S08_a_literal_too_big_for_a_double_is_an_error()
        => AssertError(new string('9', 400));

    [Theory]
    [InlineData("2+")]
    [InlineData("*3")]
    [InlineData("(2+3")]
    [InlineData("2+3)")]
    [InlineData("2 3")]
    [InlineData("()")]
    [InlineData("2**3")]
    [InlineData(".")]
    [InlineData("1.2.3")]
    [InlineData("2^")]
    public void S08_malformed_expressions_are_errors_and_never_throw(string input) => AssertError(input);

    /// <summary>
    /// Parentheses and unary signs recurse, so a pasted wall of them would exhaust the stack — and a
    /// StackOverflowException cannot be caught, it kills the process. The depth cap turns that into
    /// an ordinary Error.
    /// </summary>
    [Fact]
    public void S07_a_deeply_nested_expression_is_an_error_rather_than_a_stack_overflow()
    {
        var deep = new string('(', 5000) + "1" + new string(')', 5000);
        AssertError(deep);

        AssertError(new string('-', 5000) + "1");
    }

    [Fact]
    public void S07_nesting_up_to_the_cap_still_evaluates()
    {
        // Comfortably inside MaxDepth, and far past anything a person types by hand.
        var nested = new string('(', 8) + "2+3" + new string(')', 8);
        Assert.Equal(5, Number(nested), 9);
    }

    [Fact]
    public void S08_whitespace_inside_an_expression_is_ignored()
        => Assert.Equal(14, Number("  2 +  3 * 4  "), 9);

    [Theory]
    [InlineData(0, "3")]
    [InlineData(2, "3.33")]
    [InlineData(4, "3.3333")]
    [InlineData(8, "3.33333333")]
    public void S05_decimal_places_are_applied_to_the_displayed_result(int places, string expected)
        => Assert.Equal(expected, ExpressionEvaluator.Evaluate("10/3").Format(places));

    [Fact]
    public void S05_trailing_zeros_are_dropped_the_way_the_original_did()
    {
        Assert.Equal("4", ExpressionEvaluator.Evaluate("2+2").Format(4));
        Assert.Equal("1.5", ExpressionEvaluator.Evaluate("3/2").Format(4));
    }

    [Fact]
    public void S05_an_out_of_range_decimal_place_count_is_clamped_not_thrown()
    {
        Assert.Equal("3.33333333", ExpressionEvaluator.Evaluate("10/3").Format(99));
        Assert.Equal("3", ExpressionEvaluator.Evaluate("10/3").Format(-4));
    }

    [Fact]
    public void S05_a_result_that_rounds_to_zero_is_not_written_as_negative_zero()
        => Assert.Equal("0", ExpressionEvaluator.Evaluate("0-0.001").Format(2));

    [Fact]
    public void S03_formatting_says_what_the_widget_shows_for_each_status()
    {
        Assert.Equal(string.Empty, ExpressionEvaluator.Evaluate("").Format(2));
        Assert.Equal("Error", ExpressionEvaluator.Evaluate("1/0").Format(2));
        Assert.Equal("5", ExpressionEvaluator.Evaluate("2+3").Format(2));
    }

    [Fact]
    public void S08_an_error_result_never_pretends_to_carry_a_number()
    {
        var result = ExpressionEvaluator.Evaluate("2+");

        Assert.False(result.IsNumber);
        Assert.False(double.IsFinite(result.Value));
    }

    /// <summary>
    /// The render path calls this on every keystroke, so the contract is: whatever is in the box,
    /// it returns. Fuzzed over the characters the sanitiser keeps plus a few it does not.
    /// </summary>
    [Fact]
    public void S08_evaluation_is_total_over_arbitrary_input()
    {
        const string Alphabet = "0123456789+-*/().,%^ e#";
        var random = new Random(20260924);

        for (var i = 0; i < 4000; i++)
        {
            var length = random.Next(0, 24);
            var chars = new char[length];

            for (var c = 0; c < length; c++)
            {
                chars[c] = Alphabet[random.Next(Alphabet.Length)];
            }

            var input = new string(chars);
            var result = ExpressionEvaluator.Evaluate(input);

            if (result.IsNumber)
            {
                Assert.True(double.IsFinite(result.Value), $"'{input}' returned a non-finite value");
            }

            // Formatting must be total too: it runs in the same frame as the evaluation.
            Assert.NotNull(result.Format(4));
        }
    }
}
