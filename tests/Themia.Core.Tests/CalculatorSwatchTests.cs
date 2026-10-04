using Themia.Core.Geometry;
using Themia.Core.Localization;
using Themia.Core.Model;
using Themia.Core.Storage;
using Themia.Core.Widgets;
using Xunit;

namespace Themia.Core.Tests;

/// <summary>
/// Package PK: the calculator's keypad, recall and size ladder, and the colour swatch's modes,
/// gradient axis and palette ladder ([S-1500]..[S-1519]).
/// </summary>
public sealed class CalculatorSwatchTests : IDisposable
{
    private const double Body = 20;

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "themia-tests", Guid.NewGuid().ToString("n"));

    public CalculatorSwatchTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // ignore: temp cleanup is best effort and must not fail a test run
        }
    }

    // -----------------------------------------------------------------------------------------
    // Keypad
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void S1510_digits_and_operators_type_ascii_at_the_caret()
    {
        var edit = CalculatorKeypad.Apply(KeypadKey.Multiply, "12", 2, 0, null);
        Assert.Equal(new KeypadEdit("12*", 3, false), edit);

        edit = CalculatorKeypad.Apply(KeypadKey.D7, "12*", 1, 0, null);
        Assert.Equal(new KeypadEdit("172*", 2, false), edit);

        edit = CalculatorKeypad.Apply(KeypadKey.Divide, "8", 1, 0, null);
        Assert.Equal("8/", edit.Text);
    }

    [Fact]
    public void S1510_a_key_replaces_the_selection()
    {
        var edit = CalculatorKeypad.Apply(KeypadKey.D5, "1234", 1, 2, null);
        Assert.Equal(new KeypadEdit("154", 2, false), edit);
    }

    [Fact]
    public void S1510_backspace_deletes_before_the_caret_or_the_selection_and_stops_at_the_start()
    {
        Assert.Equal(new KeypadEdit("12", 2, false), CalculatorKeypad.Apply(KeypadKey.Backspace, "123", 3, 0, null));
        Assert.Equal(new KeypadEdit("13", 1, false), CalculatorKeypad.Apply(KeypadKey.Backspace, "123", 2, 0, null));
        Assert.Equal(new KeypadEdit("1", 1, false), CalculatorKeypad.Apply(KeypadKey.Backspace, "123", 1, 2, null));
        Assert.Equal(new KeypadEdit("123", 0, false), CalculatorKeypad.Apply(KeypadKey.Backspace, "123", 0, 0, null));
        Assert.Equal(new KeypadEdit(string.Empty, 0, false), CalculatorKeypad.Apply(KeypadKey.Backspace, string.Empty, 0, 0, null));
    }

    [Fact]
    public void S1510_clear_empties_and_equals_commits_without_changing_the_line()
    {
        Assert.Equal(new KeypadEdit(string.Empty, 0, false), CalculatorKeypad.Apply(KeypadKey.Clear, "1+2", 3, 0, "9"));
        Assert.Equal(new KeypadEdit("1+2", 3, true), CalculatorKeypad.Apply(KeypadKey.Equals, "1+2", 1, 2, "9"));
    }

    [Fact]
    public void S1510_an_out_of_range_caret_is_clamped_not_thrown()
    {
        Assert.Equal(new KeypadEdit("129", 3, false), CalculatorKeypad.Apply(KeypadKey.D9, "12", 99, 5, null));
        Assert.Equal(new KeypadEdit("912", 1, false), CalculatorKeypad.Apply(KeypadKey.D9, "12", -3, -1, null));
        Assert.Equal(new KeypadEdit("4", 1, false), CalculatorKeypad.Apply(KeypadKey.D4, null, 0, 0, null));
    }

    [Fact]
    public void S1511_an_operator_on_an_empty_line_continues_from_the_last_answer()
    {
        Assert.Equal("21*", CalculatorKeypad.Apply(KeypadKey.Multiply, string.Empty, 0, 0, "21").Text);
        Assert.Equal("21^2", CalculatorKeypad.Apply(KeypadKey.Square, "  ", 0, 2, "21").Text);

        // Not on a line that already has something, and not minus, which starts a negative number.
        Assert.Equal("3*", CalculatorKeypad.Apply(KeypadKey.Multiply, "3", 1, 0, "21").Text);
        Assert.Equal("-", CalculatorKeypad.Apply(KeypadKey.Subtract, string.Empty, 0, 0, "21").Text);

        // No answer yet: just the operator.
        Assert.Equal("+", CalculatorKeypad.Apply(KeypadKey.Add, string.Empty, 0, 0, null).Text);
    }

    [Fact]
    public void S1511_a_negative_answer_is_bracketed_so_powers_square_the_answer()
    {
        var edit = CalculatorKeypad.Apply(KeypadKey.Square, string.Empty, 0, 0, "-5");
        Assert.Equal("(-5)^2", edit.Text);
        Assert.Equal(25, ExpressionEvaluator.Evaluate(edit.Text).Value);

        Assert.Equal("2*(-5)", CalculatorKeypad.Apply(KeypadKey.Answer, "2*", 2, 0, "-5").Text);
    }

    [Fact]
    public void S1512_one_point_per_number_and_a_leading_point_gets_its_zero()
    {
        Assert.Equal("0.", CalculatorKeypad.Apply(KeypadKey.Point, string.Empty, 0, 0, null).Text);
        Assert.Equal("1+0.", CalculatorKeypad.Apply(KeypadKey.Point, "1+", 2, 0, null).Text);
        Assert.Equal("1.", CalculatorKeypad.Apply(KeypadKey.Point, "1", 1, 0, null).Text);

        // Already a point in this number, either side of the caret: nothing.
        Assert.Equal("1.5", CalculatorKeypad.Apply(KeypadKey.Point, "1.5", 3, 0, null).Text);
        Assert.Equal("15.2", CalculatorKeypad.Apply(KeypadKey.Point, "15.2", 1, 0, null).Text);

        // A point in another number does not count.
        Assert.Equal("1.5+2.", CalculatorKeypad.Apply(KeypadKey.Point, "1.5+2", 5, 0, null).Text);
    }

    [Fact]
    public void S1510_every_key_on_the_pad_has_a_label_and_a_name_and_the_rows_are_four_wide()
    {
        foreach (var row in CalculatorKeypad.Rows(extended: true))
        {
            Assert.Equal(CalculatorKeypad.Columns, row.Count);

            foreach (var key in row)
            {
                Assert.False(string.IsNullOrEmpty(CalculatorKeypad.Label(key)));
                Assert.False(string.IsNullOrEmpty(CalculatorKeypad.Name(key, Locale.En)));
            }
        }

        Assert.Equal(5, CalculatorKeypad.Rows(extended: false).Count);
        Assert.Equal(6, CalculatorKeypad.Rows(extended: true).Count);
        Assert.Equal(KeypadKey.Answer, CalculatorKeypad.Rows(extended: true)[0][0]);
    }

    [Fact]
    public void S1510_the_typed_keys_evaluate()
    {
        var text = string.Empty;

        foreach (var key in new[] { KeypadKey.OpenParen, KeypadKey.D2, KeypadKey.Add, KeypadKey.D3, KeypadKey.CloseParen, KeypadKey.Multiply, KeypadKey.D4, KeypadKey.Percent })
        {
            text = CalculatorKeypad.Apply(key, text, text.Length, 0, null).Text;
        }

        Assert.Equal("(2+3)*4%", text);
        Assert.Equal(0.2, ExpressionEvaluator.Evaluate(text).Value, 10);
    }

    // -----------------------------------------------------------------------------------------
    // Recall
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void S1513_up_walks_to_older_rows_and_stops_at_the_oldest_down_returns_to_the_line()
    {
        Assert.Equal(0, CalculatorRecall.Step(-1, older: true, count: 3));
        Assert.Equal(2, CalculatorRecall.Step(1, older: true, count: 3));
        Assert.Equal(2, CalculatorRecall.Step(2, older: true, count: 3));
        Assert.Equal(-1, CalculatorRecall.Step(0, older: false, count: 3));
        Assert.Equal(-1, CalculatorRecall.Step(-1, older: false, count: 3));
    }

    [Fact]
    public void S1513_recall_with_no_history_or_a_shrunk_one_stays_in_range()
    {
        Assert.Equal(-1, CalculatorRecall.Step(-1, older: true, count: 0));
        Assert.Equal(-1, CalculatorRecall.Step(5, older: true, count: 0));
        Assert.Equal(1, CalculatorRecall.Step(7, older: true, count: 2));
        Assert.Equal(0, CalculatorRecall.Step(7, older: false, count: 2));
    }

    // -----------------------------------------------------------------------------------------
    // Calculator ladder
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void S1514_XS_shows_only_the_line_and_gives_it_the_rest()
    {
        var plan = CalculatorLayout.Plan(new WidgetArea(120, 120), historyCount: 8, keypad: true, Body);

        Assert.Equal(CalculatorArrangement.Stacked, plan.Arrangement);
        Assert.Equal(0, plan.HistoryRows);
        Assert.False(plan.HistoryRegion);
        Assert.Equal(0, plan.KeypadRows);
        Assert.True(plan.ResultLine > Body);
    }

    [Fact]
    public void S1514_S_shows_the_newest_row_and_M_three()
    {
        Assert.Equal(1, CalculatorLayout.Plan(new WidgetArea(200, 150), 8, true, Body).HistoryRows);

        var medium = CalculatorLayout.Plan(new WidgetArea(320, 240), 8, true, Body);
        Assert.Equal(3, medium.HistoryRows);
        Assert.Equal(0, medium.KeypadRows);
    }

    [Fact]
    public void S1514_a_tall_column_gets_the_keypad_under_the_line_and_still_the_newest_row()
    {
        var plan = CalculatorLayout.Plan(new WidgetArea(200, 400), 8, true, Body);

        Assert.Equal(CalculatorArrangement.Stacked, plan.Arrangement);
        Assert.Equal(5, plan.KeypadRows);
        Assert.True(plan.HistoryRows >= 1);
        Assert.InRange(plan.KeyHeight, CalculatorLayout.KeyMin, CalculatorLayout.KeyMaxStacked);

        // Turned off, the same tile spends it on history instead.
        var off = CalculatorLayout.Plan(new WidgetArea(200, 400), 8, false, Body);
        Assert.Equal(0, off.KeypadRows);
        Assert.True(off.HistoryRows > plan.HistoryRows);
    }

    [Fact]
    public void S1514_a_wide_strip_puts_the_history_beside_the_line()
    {
        var plan = CalculatorLayout.Plan(new WidgetArea(480, 160), 8, true, Body);

        Assert.Equal(CalculatorArrangement.Strip, plan.Arrangement);
        Assert.Equal(3, plan.HistoryRows);
        Assert.Equal(240, plan.LeftWidth);
        Assert.Equal(0, plan.KeypadRows);
    }

    [Fact]
    public void S1514_large_tiles_put_a_full_height_keypad_beside_and_add_the_sixth_row()
    {
        var large = CalculatorLayout.Plan(new WidgetArea(560, 420), 8, true, Body);
        Assert.Equal(CalculatorArrangement.SideKeypad, large.Arrangement);
        Assert.Equal(6, large.KeypadRows);
        Assert.Equal(8, large.HistoryRows);
        Assert.Equal(280, large.LeftWidth);

        // The keys fill the height: six keys and five gaps within the insets.
        Assert.InRange(CalculatorLayout.KeypadHeight(6, large.KeyHeight), 420 - (2 * CalculatorLayout.Inset) - 6, 420 - (2 * CalculatorLayout.Inset));

        var huge = CalculatorLayout.Plan(new WidgetArea(800, 560), 8, true, Body);
        Assert.True(huge.KeyHeight > large.KeyHeight);
        Assert.True(huge.ResultLine > large.ResultLine);
    }

    [Fact]
    public void S1514_rows_never_exceed_the_history_and_an_empty_history_keeps_its_region()
    {
        var plan = CalculatorLayout.Plan(new WidgetArea(320, 400), historyCount: 2, keypad: false, Body);
        Assert.Equal(2, plan.HistoryRows);

        var empty = CalculatorLayout.Plan(new WidgetArea(320, 240), historyCount: 0, keypad: false, Body);
        Assert.Equal(0, empty.HistoryRows);
        Assert.True(empty.HistoryRegion);
        Assert.Equal(Body, empty.ResultLine);
    }

    [Fact]
    public void S1514_an_unmeasured_slot_is_a_plain_stack()
    {
        var plan = CalculatorLayout.Plan(default, 3, true, Body);
        Assert.Equal(CalculatorArrangement.Stacked, plan.Arrangement);
        Assert.Equal(0, plan.KeypadRows);
    }

    [Fact]
    public void S1515_a_new_calculator_shows_four_places_and_the_keypad()
    {
        var config = new CalculatorConfig();
        Assert.Equal(4, config.EffectiveDecimalPlaces);
        Assert.True(config.ShowKeypad);

        var copy = Assert.IsType<CalculatorConfig>(new CalculatorConfig { DecimalPlaces = 6, ShowKeypad = false }.Clone());
        Assert.Equal(6, copy.DecimalPlaces);
        Assert.False(copy.ShowKeypad);
    }

    // -----------------------------------------------------------------------------------------
    // Gradient axis
    // -----------------------------------------------------------------------------------------

    [Theory]
    [InlineData(0, 50, 100, 50, 0)]
    [InlineData(90, 0, 50, 100, 50)]
    [InlineData(180, 50, 0, 50, 100)]
    [InlineData(270, 100, 50, 0, 50)]
    [InlineData(135, 0, 0, 100, 100)]
    [InlineData(45, 0, 100, 100, 0)]
    public void S1503_the_axis_matches_css_on_a_square(double angle, double sx, double sy, double ex, double ey)
    {
        var axis = GradientAxis.For(angle, 100, 100);

        Assert.Equal(sx, axis.StartX, 6);
        Assert.Equal(sy, axis.StartY, 6);
        Assert.Equal(ex, axis.EndX, 6);
        Assert.Equal(ey, axis.EndY, 6);
    }

    [Theory]
    [InlineData(135)]
    [InlineData(30)]
    [InlineData(200)]
    [InlineData(315)]
    public void S1503_on_any_box_the_furthest_corners_land_exactly_on_the_two_stops(double angle)
    {
        const double w = 300;
        const double h = 80;
        var axis = GradientAxis.For(angle, w, h);
        var dx = axis.EndX - axis.StartX;
        var dy = axis.EndY - axis.StartY;
        var length2 = (dx * dx) + (dy * dy);

        // Where each corner projects along the axis, 0 = start stop, 1 = end stop.
        double[] along = [.. new[] { (0.0, 0.0), (w, 0.0), (0.0, h), (w, h) }
            .Select(c => (((c.Item1 - axis.StartX) * dx) + ((c.Item2 - axis.StartY) * dy)) / length2)];

        Assert.Equal(0, along.Min(), 6);
        Assert.Equal(1, along.Max(), 6);
    }

    [Theory]
    [InlineData(-45, 315)]
    [InlineData(360, 0)]
    [InlineData(725, 5)]
    [InlineData(double.NaN, GradientAxis.DefaultAngle)]
    [InlineData(double.PositiveInfinity, GradientAxis.DefaultAngle)]
    public void S1503_angles_are_brought_into_a_turn(double angle, double expected)
        => Assert.Equal(expected, GradientAxis.NormaliseAngle(angle), 9);

    [Fact]
    public void S1503_an_unmeasured_box_still_gives_a_valid_axis()
    {
        var axis = GradientAxis.For(90, 0, double.NaN);
        Assert.Equal(0, axis.StartX, 9);
        Assert.Equal(0.5, axis.StartY, 9);
        Assert.Equal(1, axis.EndX, 9);
        Assert.Equal(0.5, axis.EndY, 9);
    }

    // -----------------------------------------------------------------------------------------
    // Swatch config
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void S1500_a_new_swatch_is_the_original_block()
    {
        var config = new ColorSwatchConfig();

        Assert.Equal(SwatchMode.Solid, config.Mode);
        Assert.Equal(string.Empty, config.Color); // the accent
        Assert.Equal(1, config.Alpha);
        Assert.Equal(135, config.Angle);
        Assert.Equal(SwatchFill.DefaultEndColor, config.EndColor);
        Assert.NotEmpty(config.Swatches); // switching to Palette shows something
    }

    [Fact]
    public void S1502_empty_or_unreadable_colours_paint_the_fallback_and_alpha_is_clamped()
    {
        var accent = new RgbColor(1, 2, 3);

        Assert.Equal(new FillColor(accent, 1), SwatchFill.Resolve(string.Empty, 1, accent));
        Assert.Equal(new FillColor(accent, 0), SwatchFill.Resolve("nope", -2, accent));
        Assert.Equal(new FillColor(new RgbColor(0x34, 0xD3, 0x99), 0.5), SwatchFill.Resolve("#34d399", 0.5, accent));
        Assert.Equal((byte)255, new FillColor(accent, double.NaN).A);
        Assert.Equal((byte)128, new FillColor(accent, 0.5).A);
    }

    [Fact]
    public void S1502_repair_brings_a_hand_edited_swatch_back_inside_its_invariants()
    {
        var config = new ColorSwatchConfig
        {
            Mode = (SwatchMode)42,
            Color = "#abc",
            StartColor = "blue-ish",
            EndColor = "zzz",
            Alpha = 3,
            StartAlpha = double.NaN,
            EndAlpha = -1,
            Angle = -90,
        };

        var notes = config.Repair();

        Assert.Equal(SwatchMode.Solid, config.Mode);
        Assert.Equal("#AABBCC", config.Color);
        Assert.Equal(string.Empty, config.StartColor);
        Assert.Equal(SwatchFill.DefaultEndColor, config.EndColor);
        Assert.Equal(1, config.Alpha);
        Assert.Equal(1, config.StartAlpha);
        Assert.Equal(0, config.EndAlpha);
        Assert.Equal(270, config.Angle);
        Assert.Equal(7, notes.Count);

        Assert.Empty(new ColorSwatchConfig().Repair());
    }

    [Fact]
    public async Task S1501_mode_colours_and_angle_survive_the_state_file_and_duplicating()
    {
        var config = new ColorSwatchConfig
        {
            Mode = SwatchMode.Gradient,
            Color = "#112233",
            Alpha = 0.25,
            StartColor = "#445566",
            StartAlpha = 0.5,
            EndColor = "#778899",
            EndAlpha = 0.75,
            Angle = 200,
        };

        var path = Path.Combine(_dir, "state.json");
        var state = AppState.CreateDefault();
        var widget = WidgetInstance.Create(WidgetKind.ColorSwatch, new FracRect(0.1, 0.1, 0.2, 0.2));
        widget.Config = config;
        state.ActiveView().Widgets.Clear();
        state.ActiveView().Widgets.Add(widget);

        var store = new AppStateStore(path, TimeSpan.Zero);
        store.SaveSoon(state);
        await store.FlushAsync();

        var loaded = new AppStateStore(path, TimeSpan.Zero).Load();
        var reloaded = Assert.IsType<ColorSwatchConfig>(loaded.State.ActiveView().Widgets[0].Config);

        foreach (var copy in new[] { reloaded, Assert.IsType<ColorSwatchConfig>(config.Clone()) })
        {
            Assert.Equal(SwatchMode.Gradient, copy.Mode);
            Assert.Equal(("#112233", 0.25), (copy.Color, copy.Alpha));
            Assert.Equal(("#445566", 0.5), (copy.StartColor, copy.StartAlpha));
            Assert.Equal(("#778899", 0.75), (copy.EndColor, copy.EndAlpha));
            Assert.Equal(200, copy.Angle);
            Assert.Equal(SwatchPalette.Default.Count, copy.Swatches.Count);
        }
    }

    // -----------------------------------------------------------------------------------------
    // Palette ladder
    // -----------------------------------------------------------------------------------------

    private const double Caption = 44;

    [Fact]
    public void S1506_a_narrow_tile_is_a_grid_of_chips_showing_every_colour()
    {
        var plan = SwatchLayout.Plan(120, 120, 8, showValues: true, Caption);

        Assert.Equal(SwatchArrangement.Chips, plan.Arrangement);
        Assert.Equal(3, plan.Columns);
        Assert.True(plan.Cell >= 32);
        Assert.False(plan.ShowValue);
    }

    [Fact]
    public void S1506_too_many_chips_keep_the_minimum_size_and_scroll()
    {
        var (columns, side) = SwatchLayout.ChipGrid(120, 120, 64);

        Assert.Equal(SwatchLayout.MinChip, side);
        Assert.Equal(5, columns);
    }

    [Fact]
    public void S1506_medium_tiles_are_rows_with_values_and_a_tall_column_stretches_them()
    {
        var medium = SwatchLayout.Plan(320, 240, 8, true, Caption);
        Assert.Equal(new SwatchPlan(SwatchArrangement.Rows, 1, 40, true), medium);

        var tall = SwatchLayout.Plan(200, 400, 8, true, Caption);
        Assert.Equal(SwatchArrangement.Rows, tall.Arrangement);
        Assert.Equal(50, tall.Cell);

        var strip = SwatchLayout.Plan(480, 160, 8, true, Caption);
        Assert.Equal(2, strip.Columns);
        Assert.Equal(40, strip.Cell);
    }

    [Fact]
    public void S1506_the_value_shows_in_rows_unless_turned_off()
    {
        Assert.True(SwatchLayout.Plan(200, 400, 30, true, Caption).ShowValue);
        Assert.False(SwatchLayout.Plan(200, 400, 30, false, Caption).ShowValue);
    }

    [Fact]
    public void S1506_large_tiles_with_a_short_palette_become_big_tiles()
    {
        var large = SwatchLayout.Plan(560, 420, 8, true, Caption);
        Assert.Equal(SwatchArrangement.Tiles, large.Arrangement);
        Assert.Equal(4, large.Columns);

        var huge = SwatchLayout.Plan(800, 560, 8, true, Caption);
        Assert.Equal(SwatchArrangement.Tiles, huge.Arrangement);
        Assert.True(huge.Cell > large.Cell);

        // A long palette fills the same tile with rows instead.
        Assert.Equal(SwatchArrangement.Rows, SwatchLayout.Plan(560, 420, 40, true, Caption).Arrangement);
    }

    [Fact]
    public void S1506_an_empty_palette_or_unmeasured_slot_is_safe()
    {
        Assert.Equal(SwatchArrangement.Empty, SwatchLayout.Plan(560, 420, 0, true, Caption).Arrangement);
        Assert.Equal(SwatchArrangement.Rows, SwatchLayout.Plan(double.NaN, 0, 5, true, Caption).Arrangement);
    }
}
