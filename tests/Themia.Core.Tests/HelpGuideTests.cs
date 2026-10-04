using Themia.Core.Model;
using Themia.Core.Widgets;
using Xunit;

namespace Themia.Core.Tests;

public sealed class HelpGuideTests
{
    [Fact]
    public void S482_strong_and_emphasis_split_into_runs()
    {
        var spans = RichText.Parse("Press *Ctrl+Alt+W*, then _Done_.");

        Assert.Equal(
            [
                new RichSpan(RichKind.Text, "Press "),
                new RichSpan(RichKind.Strong, "Ctrl+Alt+W"),
                new RichSpan(RichKind.Text, ", then "),
                new RichSpan(RichKind.Emphasis, "Done"),
                new RichSpan(RichKind.Text, "."),
            ],
            spans);
    }

    [Theory]
    [InlineData("a lone * star")]
    [InlineData("snake_case")]
    [InlineData("empty ** pair")]
    [InlineData("*across\nlines*")]
    public void S482_an_unpaired_marker_stays_literal(string text)
    {
        var spans = RichText.Parse(text);

        Assert.All(spans, s => Assert.Equal(RichKind.Text, s.Kind));
        Assert.Equal(text, string.Concat(spans.Select(s => s.Text)));
    }

    [Fact]
    public void S482_markers_do_not_nest()
    {
        var spans = RichText.Parse("_a *b_ c*");

        Assert.Equal(new RichSpan(RichKind.Emphasis, "a *b"), spans[0]);
        Assert.Equal(new RichSpan(RichKind.Text, " c*"), spans[1]);
    }

    [Fact]
    public void S481_the_guide_has_four_tabs_in_order()
    {
        Assert.Equal(
            [HelpTab.Basics, HelpTab.Widgets, HelpTab.Views, HelpTab.Customize],
            HelpGuide.Pages.Select(p => p.Tab));
    }

    [Fact]
    public void S482_every_page_has_prose_and_every_marker_in_it_is_paired()
    {
        foreach (var page in HelpGuide.Pages)
        {
            Assert.False(string.IsNullOrWhiteSpace(page.Intro));
            Assert.NotEmpty(page.Sections);

            var prose = new List<string> { page.Intro };
            prose.AddRange(page.Sections.SelectMany(s => s.Bullets));
            if (page.Tip is not null)
            {
                prose.Add(page.Tip);
            }

            foreach (var line in prose)
            {
                // A marker left over after parsing is one the author forgot to close.
                var plain = RichText.Parse(line).Where(s => s.Kind == RichKind.Text);
                Assert.DoesNotContain(plain, s => s.Text.Contains('*') || s.Text.Contains('_'));
            }
        }
    }

    [Fact]
    public void S483_the_basics_name_every_way_into_edit_mode()
    {
        var basics = string.Join(" ", HelpGuide.Page(HelpTab.Basics).Sections.SelectMany(s => s.Bullets));

        Assert.Contains("Ctrl+Alt+W", basics, StringComparison.Ordinal);
        Assert.Contains("double-click", basics, StringComparison.Ordinal);
        Assert.Contains("start Themia again", basics, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(WidgetKind.Themia)]
    [InlineData(WidgetKind.ViewSwitcher)]
    public void S485_chrome_widgets_start_without_a_header(WidgetKind kind)
        => Assert.Equal(HeaderPlacement.Hidden, WidgetInstance.Create(kind, new Geometry.FracRect(0, 0, 0.1, 0.1)).Header);

    [Fact]
    public void S480_help_starts_with_its_header()
        => Assert.Equal(HeaderPlacement.Top, WidgetInstance.Create(WidgetKind.Help, new Geometry.FracRect(0, 0, 0.1, 0.1)).Header);
}
