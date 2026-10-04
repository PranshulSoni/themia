using Themia.Core.Model;
using Themia.Core.Widgets;
using Xunit;

namespace Themia.Core.Tests;

/// <summary>
/// The palette: what it shows, what it drops, and what the reorder buttons in settings do at the
/// ends of the list.
/// </summary>
public class SwatchPaletteTests
{
    private static List<Swatch> Read(params SwatchSeed[] seeds)
        => SwatchPalette.Read(seeds, ColorNotation.Hex);

    [Fact]
    public void The_bundled_palette_is_all_readable_colours()
    {
        Assert.NotEmpty(SwatchPalette.Default);

        foreach (var seed in SwatchPalette.Default)
        {
            Assert.True(ColorFormats.TryParse(seed.Value, out _), $"{seed.Name} is not a colour");
            Assert.False(string.IsNullOrWhiteSpace(seed.Name));
        }
    }

    [Fact]
    public void An_empty_palette_reads_as_an_empty_palette_rather_than_the_defaults()
    {
        Assert.Empty(SwatchPalette.Read([], ColorNotation.Hex));
        Assert.Empty(SwatchPalette.Read(null, ColorNotation.Hex));
        Assert.Empty(new ColorSwatchConfig { Swatches = [] }.EffectiveSwatches);
    }

    [Fact]
    public void An_entry_that_does_not_name_a_colour_is_dropped_not_painted_black()
    {
        var swatches = Read(
            new SwatchSeed("#0078D4", "Blue"),
            new SwatchSeed("not a colour", "Nonsense"),
            new SwatchSeed(string.Empty, "Blank"),
            new SwatchSeed("#10B981", "Emerald"));

        Assert.Equal(2, swatches.Count);
        Assert.Equal("Blue", swatches[0].Name);
        Assert.Equal("Emerald", swatches[1].Name);
    }

    [Fact]
    public void An_unnamed_swatch_is_labelled_with_the_value_that_would_be_copied()
    {
        Assert.Equal("#0078D4", Read(new SwatchSeed("#0078d4", "   ")).Single().Name);

        var asRgb = SwatchPalette.Read([new SwatchSeed("#0078d4", string.Empty)], ColorNotation.Rgb).Single();
        Assert.Equal("rgb(0, 120, 212)", asRgb.Name);
    }

    [Fact]
    public void A_name_keeps_its_own_spelling_and_loses_only_the_padding()
        => Assert.Equal("Brand blue", Read(new SwatchSeed("#0078D4", "  Brand blue  ")).Single().Name);

    [Fact]
    public void Reading_preserves_the_stored_order()
    {
        var swatches = Read(
            new SwatchSeed("#000000", "One"),
            new SwatchSeed("#111111", "Two"),
            new SwatchSeed("#222222", "Three"));

        Assert.Equal(["One", "Two", "Three"], swatches.Select(s => s.Name));
    }

    // -----------------------------------------------------------------------------------------
    // Reordering
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void Moving_swaps_with_the_neighbour_and_says_it_moved()
    {
        var items = new List<string> { "a", "b", "c" };

        Assert.True(DeskListOrder.Move(items, 0, 1));
        Assert.Equal(["b", "a", "c"], items);

        Assert.True(DeskListOrder.Move(items, 2, -1));
        Assert.Equal(["b", "c", "a"], items);
    }

    [Theory]
    [InlineData(0, -1)]   // up from the top
    [InlineData(2, 1)]    // down from the bottom
    [InlineData(1, 0)]    // nowhere
    [InlineData(-1, 1)]   // an index that is not in the list
    [InlineData(9, -1)]
    public void A_move_that_has_nowhere_to_go_changes_nothing_and_does_not_throw(int index, int delta)
    {
        var items = new List<string> { "a", "b", "c" };

        Assert.False(DeskListOrder.Move(items, index, delta));
        Assert.Equal(["a", "b", "c"], items);
    }

    [Fact]
    public void Moving_within_nothing_is_a_no_op()
        => Assert.False(DeskListOrder.Move<string>(null, 0, 1));

    [Fact]
    public void A_single_entry_list_has_nowhere_to_move_to()
    {
        var items = new List<string> { "only" };

        Assert.False(DeskListOrder.Move(items, 0, 1));
        Assert.False(DeskListOrder.Move(items, 0, -1));
        Assert.Equal(["only"], items);
    }
}
