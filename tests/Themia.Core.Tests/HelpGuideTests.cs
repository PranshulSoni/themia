using Themia.Core.Localization;
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
            HelpGuide.PagesIn(Locale.En).Select(p => p.Tab));
    }

    public static TheoryData<Locale> Locales()
    {
        var data = new TheoryData<Locale>();

        foreach (var locale in LanguageResolver.All)
        {
            data.Add(locale);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Locales))]
    public void S482_every_page_has_prose_and_every_marker_in_it_is_paired(Locale locale)
    {
        foreach (var page in HelpGuide.PagesIn(locale))
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
        var basics = string.Join(" ", HelpGuide.Page(HelpTab.Basics, Locale.En).Sections.SelectMany(s => s.Bullets));

        Assert.Contains("Ctrl+Alt+W", basics, StringComparison.Ordinal);
        Assert.Contains("double-click", basics, StringComparison.Ordinal);
        Assert.Contains("start Themia again", basics, StringComparison.Ordinal);
    }

    [Fact]
    public void S482_every_guide_string_is_written_in_all_nine_languages()
    {
        foreach (var id in HelpGuide.TextIds)
        {
            foreach (var locale in LanguageResolver.All)
            {
                Assert.False(string.IsNullOrWhiteSpace(StringCatalog.Raw(locale, id)), $"{id.Key()} has no {locale} text");
            }

            Assert.False(StringCatalog.IsEnglishOnly(id), $"{id.Key()} is English only");
        }
    }

    /// <summary>
    /// Every language marks as many terms and as many things on screen as English does, so a
    /// translation cannot drop a key name's bold or the italic on a button it names.
    /// </summary>
    [Theory]
    [MemberData(nameof(Locales))]
    public void S482_every_language_keeps_the_markers_of_the_english(Locale locale)
    {
        foreach (var id in HelpGuide.TextIds)
        {
            var english = RichText.Parse(Strings.Get(Locale.En, id));
            var own = RichText.Parse(Strings.Get(locale, id));

            Assert.True(
                english.Count(s => s.Kind == RichKind.Strong) == own.Count(s => s.Kind == RichKind.Strong)
                && english.Count(s => s.Kind == RichKind.Emphasis) == own.Count(s => s.Kind == RichKind.Emphasis),
                $"{id.Key()} in {locale} marks different runs than English");
            Assert.DoesNotContain(own, s => s.Kind == RichKind.Text && (s.Text.Contains('*') || s.Text.Contains('_')));
        }
    }

    [Fact]
    public void S482_the_guide_is_read_in_the_language_asked_for()
    {
        var german = HelpGuide.Page(HelpTab.Basics, Locale.De);

        Assert.Equal("Grundlagen", german.Label);
        Assert.Contains("*Strg+Alt+W*", german.Sections[0].Bullets[0], StringComparison.Ordinal);
        Assert.Contains("_Layout bearbeiten_", german.Sections[0].Bullets[0], StringComparison.Ordinal);
        Assert.Equal("Strg+Alt+W", HelpGuide.KeysIn(Locale.De)[0].Keys);
        Assert.Equal("Entf", HelpGuide.KeysIn(Locale.De).Single(k => k.Meaning == "Auswahl löschen").Keys);
        Assert.Equal("Tasten", HelpGuide.KeysTitle(Locale.De));
    }

    [Fact]
    public void S482_every_language_has_the_same_shape()
    {
        var english = HelpGuide.PagesIn(Locale.En);

        foreach (var locale in LanguageResolver.All)
        {
            var pages = HelpGuide.PagesIn(locale);

            Assert.Equal(english.Select(p => p.Sections.Count), pages.Select(p => p.Sections.Count));
            Assert.Equal(english.SelectMany(p => p.Sections).Select(s => s.Bullets.Count), pages.SelectMany(p => p.Sections).Select(s => s.Bullets.Count));
            Assert.Equal(english.Select(p => p.Tip is null), pages.Select(p => p.Tip is null));
            Assert.Equal(HelpGuide.KeysIn(Locale.En).Count, HelpGuide.KeysIn(locale).Count);
        }
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
