using Themia.Core.Localization;
using Themia.Core.Widgets;
using Xunit;

namespace Themia.Core.Tests;

/// <summary>
/// [S-1622] The per-core strip's forms and the ladder's "first size that fits"; [S-1623] the
/// power tile's screen and sleep section.
/// </summary>
public class Polish2LayoutTests
{
    // -----------------------------------------------------------------------------------------
    // [S-1622] Disclosure.Fits / TakeFirst
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void S1622_take_first_takes_the_richest_size_that_fits_and_keeps_the_ladder_open()
    {
        var room = new Disclosure(100, 100, gap: 8);
        room.Reserve(40);                                   // 60 left

        Assert.Equal(1, room.TakeFirst([80, 40, 20]));      // 40 + gap 8 fits; 80 does not
        Assert.Equal(12, room.Remaining);
        Assert.True(room.Take(4));                          // still open after a smaller size was taken
    }

    [Fact]
    public void S1622_take_first_with_no_size_that_fits_is_a_miss_that_closes_the_ladder()
    {
        var room = new Disclosure(100, 50, gap: 8);
        room.Reserve(40);

        Assert.Equal(-1, room.TakeFirst([30, 20]));
        Assert.False(room.Take(1));
        Assert.Equal(-1, room.TakeFirst([1]));
        Assert.Equal(-1, new Disclosure(100, 100).TakeFirst([]));
    }

    [Fact]
    public void S1622_fits_asks_without_taking_or_closing()
    {
        var room = new Disclosure(100, 60, gap: 8);
        room.Reserve(20);                                   // 40 left

        Assert.False(room.Fits(40));                        // 40 + gap
        Assert.True(room.Fits(32));
        Assert.Equal(40, room.Remaining);
        Assert.True(room.Take(32));
    }

    // -----------------------------------------------------------------------------------------
    // [S-1622] the per-core strip's forms
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void S1622_the_forms_run_from_the_full_strip_to_bare_short_bars_each_shorter()
    {
        var forms = MeterCellForms.Ladder(24, 528, tall: true, labelWidth: 14);

        Assert.Equal(3, forms.Count);
        Assert.Equal(new CellGrid(24, 1, true), forms[0].Shape);
        Assert.Equal(MeterCellForms.TallBar, forms[0].BarHeight);
        Assert.True(forms[0].Labels && forms[0].Title);

        Assert.Equal(MeterCellForms.ShortBar, forms[1].BarHeight);
        Assert.False(forms[1].Labels);
        Assert.True(forms[1].Title);

        Assert.False(forms[2].Title);
        Assert.Equal(MeterCellForms.ShortBar, forms[2].Height);

        for (var i = 1; i < forms.Count; i++)
        {
            Assert.True(forms[i].Height < forms[i - 1].Height);
        }
    }

    [Fact]
    public void S1622_a_short_form_with_its_caption_is_as_tall_as_a_stat_tile_so_it_can_sit_beside_one()
    {
        var shortTitled = MeterCellForms.Ladder(24, 300, tall: true, labelWidth: 14)
            .First(f => f.Title && !f.Labels && f.BarHeight == MeterCellForms.ShortBar);

        // A stat tile is a Caption label over a BodyStrong value.
        Assert.True(shortTitled.Height <= FluentType.Caption.LineHeight + FluentType.BodyStrong.LineHeight + Disclosure.Tolerance);
    }

    [Fact]
    public void S1622_a_strip_already_in_short_bars_offers_only_its_bare_form_after_it()
    {
        // 64 bars on a 168-px column need several rows, which are short bars already.
        var forms = MeterCellForms.Ladder(64, 168, tall: true, labelWidth: 14);

        Assert.Equal(2, forms.Count);
        Assert.True(forms[0].Shape.Rows > 1);
        Assert.Equal(MeterCellForms.ShortBar, forms[0].BarHeight);
        Assert.True(forms[0].Title);
        Assert.False(forms[1].Title);
    }

    [Theory]
    [InlineData(0, 300)]
    [InlineData(8, 0)]
    [InlineData(8, double.NaN)]
    public void S1622_nothing_to_draw_offers_no_form(int count, double width)
        => Assert.Empty(MeterCellForms.Ladder(count, width, tall: false, labelWidth: 14));

    // -----------------------------------------------------------------------------------------
    // [S-1623] the power tile's screen and sleep section
    // -----------------------------------------------------------------------------------------

    [Theory]
    [InlineData(0u, "Never")]
    [InlineData(45u, "45 s")]
    [InlineData(60u, "1 min")]
    [InlineData(300u, "5 min")]
    [InlineData(3599u, "59 min")]
    [InlineData(3600u, "1 h")]
    [InlineData(5400u, "1 h 30 min")]
    [InlineData(18000u, "5 h")]
    public void S1623_a_timeout_reads_as_settings_shows_it(uint seconds, string expected)
        => Assert.Equal(expected, PowerTimeoutRules.Duration(seconds));

    [Fact]
    public void S1623_the_section_lists_screen_sleep_and_hibernate_in_settings_order()
    {
        var facts = PowerTimeoutRules.Facts(new PowerTimeouts(300, 900, 0));

        Assert.Equal([PowerFactKind.ScreenOff, PowerFactKind.Sleep, PowerFactKind.Hibernate], facts.Select(f => f.Kind));
        Assert.Equal(["5 min", "15 min", "Never"], facts.Select(f => f.Value));
        Assert.Equal("Screen off after 5 min", facts[0].Line);
    }

    [Theory]
    [InlineData(Locale.En, 5400u, "1 h 30 min")]
    [InlineData(Locale.De, 5400u, "1 Std. 30 Min.")]
    [InlineData(Locale.De, 0u, "Nie")]
    [InlineData(Locale.Ja, 300u, "5 分")]
    public void S1623_timeouts_are_worded_per_language(Locale locale, uint seconds, string expected)
        => Assert.Equal(expected, PowerTimeoutRules.Duration(locale, seconds));

    [Fact]
    public void S1623_an_unreadable_timeout_has_no_row_rather_than_reading_never()
    {
        Assert.Empty(PowerTimeoutRules.Facts(PowerTimeouts.Unknown));

        var facts = PowerTimeoutRules.Facts(new PowerTimeouts(null, 600, null));
        Assert.Equal(PowerFactKind.Sleep, Assert.Single(facts).Kind);
    }

    [Fact]
    public void S1623_uptime_uses_the_cpu_meters_two_largest_units()
    {
        var up = PowerTimeoutRules.Uptime(new TimeSpan(6, 11, 5, 0));

        Assert.Equal(PowerFactKind.Uptime, up.Kind);
        Assert.Equal("6d 11h", up.Value);
        Assert.Equal("Up for 6d 11h", up.Line);
    }

    // -----------------------------------------------------------------------------------------
    // [S-1624] the extra-large mixer's list fills its column
    // -----------------------------------------------------------------------------------------

    [Theory]
    [InlineData(512, 7, 40, 64)]     // seven apps on XL: capped at the tallest row
    [InlineData(512, 9, 40, 56)]     // nine share the height
    [InlineData(512, 12, 40, 42)]
    [InlineData(512, 13, 40, 40)]    // thirteen do not all fit: base height, they scroll
    [InlineData(512, 0, 40, 40)]
    [InlineData(0, 3, 40, 40)]       // unmeasured
    public void S1624_rows_that_all_fit_share_the_height_up_to_a_cap(double height, int count, double baseHeight, double expected)
        => Assert.Equal(expected, MixerFill.RowHeight(height, count, baseHeight));

    [Theory]
    [InlineData(false, false, true, false, 1, "Playing")]
    [InlineData(false, false, true, false, 3, "Playing")]
    [InlineData(false, false, false, false, 1, "Silent")]
    [InlineData(true, false, true, false, 2, "Muted")]
    [InlineData(false, true, true, false, 1, "Windows sounds")]
    [InlineData(false, false, true, true, 0, "All apps on this device")]
    public void S1624_the_caption_says_what_the_app_is_doing(bool muted, bool system, bool active, bool master, int sessions, string expected)
    {
        var row = new MixerRow(master ? MixerRules.MasterKey : "app", "App", null, 0.5, muted, active, system, sessions);

        Assert.Equal(expected, MixerFill.Detail(row));
    }
}
