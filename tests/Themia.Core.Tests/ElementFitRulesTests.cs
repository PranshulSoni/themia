using Themia.Core.Model;
using Themia.Core.Widgets;
using Xunit;

namespace Themia.Core.Tests;

/// <summary>[S-1316]/[S-1317]/[S-1318] How a pinned element is drawn and captured at each size.</summary>
public class ElementFitRulesTests
{
    [Fact]
    public void S1316_a_large_element_that_would_be_a_smudge_is_shown_top_left_at_its_own_size()
    {
        // A 300×80 card in an XS slot: fitting it whole would draw it at 35%.
        var placement = ElementFitRules.Place(104, 68, 300, 80, ElementFit.Whole);

        Assert.Equal(1, placement.Scale);
        Assert.Equal(104, placement.Width);
        Assert.Equal(68, placement.Height);
        Assert.True(placement.FadeRight);
        Assert.True(placement.FadeBottom);
        Assert.True(placement.Cropped);
    }

    [Fact]
    public void S1316_an_element_that_fits_legibly_is_shown_whole_and_not_cropped()
    {
        // The same card at S: 61% is still readable.
        var placement = ElementFitRules.Place(184, 98, 300, 80, ElementFit.Whole);

        Assert.Equal(184.0 / 300, placement.Scale, 6);
        Assert.False(placement.Cropped);
        Assert.Equal(184, placement.Width, 6);
    }

    [Fact]
    public void S1316_a_small_element_is_blown_up_to_fill_but_never_past_three_times()
    {
        var placement = ElementFitRules.Place(544, 360, 80, 24, ElementFit.Whole);

        Assert.Equal(ElementFitRules.MaxScale, placement.Scale);
        Assert.Equal(240, placement.Width);
        Assert.False(placement.Cropped);

        var fits = ElementFitRules.Place(544, 360, 300, 80, ElementFit.Whole);
        Assert.Equal(544.0 / 300, fits.Scale, 6);
    }

    [Fact]
    public void S1316_fill_width_takes_the_width_and_crops_the_bottom()
    {
        var placement = ElementFitRules.Place(184, 340, 400, 1200, ElementFit.FillWidth);

        Assert.Equal(184.0 / 400, placement.Scale, 6);
        Assert.Equal(184, placement.Width, 6);
        Assert.Equal(340, placement.Height);
        Assert.False(placement.FadeRight);
        Assert.True(placement.FadeBottom);
    }

    [Fact]
    public void S1316_fill_width_below_legibility_also_falls_back_to_one_to_one()
    {
        var placement = ElementFitRules.Place(104, 68, 1200, 400, ElementFit.FillWidth);

        Assert.Equal(1, placement.Scale);
        Assert.True(placement.FadeRight);
    }

    [Theory]
    [InlineData(0, 100, 100, 100)]
    [InlineData(100, 100, 0, 100)]
    [InlineData(double.NaN, 100, 100, 100)]
    [InlineData(100, 100, 100, double.PositiveInfinity)]
    public void S1316_an_unmeasured_box_or_element_places_nothing(double bw, double bh, double ew, double eh)
    {
        var placement = ElementFitRules.Place(bw, bh, ew, eh, ElementFit.Whole);
        Assert.Equal(0, placement.Width);
        Assert.False(placement.Cropped);
    }

    [Fact]
    public void S1316_fit_defaults_to_whole_and_survives_a_clone()
    {
        Assert.Equal(ElementFit.Whole, new WebElementConfig().Fit);
        Assert.Equal(ElementFit.FillWidth, ((WebElementConfig)new WebElementConfig { Fit = ElementFit.FillWidth }.Clone()).Fit);
        Assert.Equal(ElementFit.Whole, new WebElementConfig { Fit = (ElementFit)7 }.EffectiveFit);
    }

    [Theory]
    [InlineData(0.6, 1)]
    [InlineData(1.8, 1.8)]
    [InlineData(5, 3)]
    [InlineData(double.NaN, 1)]
    public void S1317_the_capture_zoom_is_the_drawn_scale_between_one_and_three(double scale, double expected)
        => Assert.Equal(expected, ElementFitRules.CaptureZoom(scale, 300, 80, 1.5), 6);

    [Fact]
    public void S1317_the_capture_never_exceeds_the_largest_texture()
    {
        // 4000 CSS px at a 2× display is already 8000 px; a 3× zoom would be 24000.
        var zoom = ElementFitRules.CaptureZoom(3, 4000, 200, 2);
        Assert.Equal(ElementFitRules.MaxCapturePixels / 8000, zoom, 6);
    }

    [Theory]
    [InlineData(1.0, 1.0, false)]
    [InlineData(1.2, 1.0, false)]
    [InlineData(1.3, 1.0, true)]
    [InlineData(0.5, 2.0, false)]
    [InlineData(9, 3, false)]
    [InlineData(9, 2, true)]
    public void S1317_a_capture_is_retaken_only_when_the_widget_outgrew_it(double scale, double captured, bool expected)
        => Assert.Equal(expected, ElementFitRules.NeedsRecapture(scale, captured));

    [Fact]
    public void S1318_the_footer_shows_only_at_the_largest_size()
    {
        Assert.True(ElementFitRules.ShowsFooter(new WidgetArea(800, 516)));
        Assert.False(ElementFitRules.ShowsFooter(new WidgetArea(560, 376)));
        Assert.False(ElementFitRules.ShowsFooter(new WidgetArea(200, 356)));
    }
}
