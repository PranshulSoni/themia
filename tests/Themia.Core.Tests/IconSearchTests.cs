using Themia.Core.Icons;
using Themia.Core.Localization;
using Xunit;

namespace Themia.Core.Tests;

/// <summary>[S-1800]..[S-1802] The icon picker's glyph set and its search.</summary>
public sealed class IconSearchTests
{
    private static readonly FluentIcon[] Sample =
    [
        new(0xE700, "GlobalNavButton", ["menu", "hamburger"]),
        new(0xE715, "Mail", ["envelope", "email"]),
        new(0xE787, "Calendar", ["date", "schedule"]),
        new(0xE80F, "Home", ["house", "safety"]),
        new(0xE81D, "Location", ["map", "place"]),
        new(0xE9D2, "AreaChart", ["graph", "statistics"]),
        new(0xEB05, "PieSingle", ["chart", "graph"]),
        new(0xE701, "Wifi", ["wireless fidelity", "network"]),
        new(0xE722, "Camera", ["photo", "picture"]),
    ];

    private static List<string> Names(IEnumerable<FluentIcon> icons) => [.. icons.Select(i => i.Name)];

    // ---- the set ------------------------------------------------------------------------------

    [Fact]
    public void S1800_the_set_is_the_whole_gallery_list_each_glyph_once_and_storable()
    {
        Assert.True(FluentIcons.All.Count > 1400);
        Assert.Equal(FluentIcons.All.Count, FluentIcons.All.Select(i => i.Code).Distinct().Count());
        Assert.All(FluentIcons.All, icon =>
        {
            Assert.InRange(icon.Code, 0xE000, 0xF8FF);
            Assert.False(string.IsNullOrWhiteSpace(icon.Name));
            Assert.Single(icon.Glyph);

            // Stored as the glyph itself, and the load check accepts it ([S-703]).
            Assert.Same(icon, FluentIcons.ByGlyph(icon.Glyph));
            Assert.All(icon.Tags, tag => Assert.Equal(tag.Trim().ToLowerInvariant(), tag));
        });
    }

    [Fact]
    public void S1800_an_unknown_or_empty_glyph_is_no_icon()
    {
        Assert.Null(FluentIcons.ByGlyph(null));
        Assert.Null(FluentIcons.ByGlyph(string.Empty));
        Assert.Null(FluentIcons.ByGlyph("A"));
    }

    [Fact]
    public void S1800_names_are_spaced_for_people()
    {
        Assert.Equal("Global Nav Button", FluentIcons.SpaceWords("GlobalNavButton"));
        Assert.Equal("VPN", FluentIcons.SpaceWords("VPN"));
        Assert.Equal("OEM Key", FluentIcons.SpaceWords("OEMKey"));
        Assert.Equal("Punc Key 2", FluentIcons.SpaceWords("PuncKey2"));
    }

    [Fact]
    public void S1801_every_suggested_icon_is_in_the_set()
    {
        Assert.Equal(32, FluentIcons.Suggested.Count);
        Assert.All(FluentIcons.Suggested, s => Assert.Contains(FluentIcons.All, icon => icon.Code == s.Code));
        Assert.Equal(32, FluentIcons.Suggested.Select(s => s.Code).Distinct().Count());
    }

    // ---- search -------------------------------------------------------------------------------

    [Fact]
    public void S1802_an_empty_query_is_every_icon_suggested_first_then_in_order()
    {
        var search = new IconSearch(Sample, leading: [0xE80F, 0xE715]);

        foreach (var query in new string?[] { null, string.Empty, "   " })
        {
            var all = search.Find(query);
            Assert.Equal(Sample.Length, all.Count);
            Assert.Equal(["Home", "Mail", "GlobalNavButton", "Calendar", "Location"], Names(all.Take(5)));
        }
    }

    [Fact]
    public void S1802_matching_ignores_case()
    {
        var search = new IconSearch(Sample);

        Assert.Equal("Home", search.Find("HOME")[0].Name);
        Assert.Equal("Home", search.Find("hOuSe")[0].Name);
    }

    [Fact]
    public void S1802_a_name_prefix_beats_a_substring()
    {
        var search = new IconSearch(Sample);

        // "ca": Calendar and Camera start with it; Location only contains it.
        var hits = Names(search.Find("ca"));
        Assert.Equal(3, hits.Count);
        Assert.Equal("Location", hits[^1]);
        Assert.Equal(["Camera", "Calendar"], hits.Take(2));
    }

    [Fact]
    public void S1802_tags_match_and_rank_below_a_name_hit()
    {
        var search = new IconSearch(Sample);

        Assert.Equal(["Mail"], Names(search.Find("envelope")));
        Assert.Equal(["Wifi"], Names(search.Find("fidelity")));

        // "chart": AreaChart has it as a name word, PieSingle only as a tag.
        Assert.Equal(["AreaChart", "PieSingle"], Names(search.Find("chart")));
    }

    [Fact]
    public void S1802_a_tag_prefix_beats_a_tag_substring()
    {
        var search = new IconSearch(
        [
            new(0xE001, "Alpha", ["outhouse"]),
            new(0xE002, "Beta", ["house"]),
        ]);

        Assert.Equal(["Beta", "Alpha"], Names(search.Find("hous")));
    }

    [Fact]
    public void S1802_every_word_of_the_query_must_match()
    {
        var search = new IconSearch(Sample);

        Assert.Equal(["PieSingle"], Names(search.Find("pie graph")));
        Assert.Empty(search.Find("pie envelope"));
        Assert.Empty(search.Find("zzz"));
    }

    [Fact]
    public void S1802_a_code_point_finds_its_glyph()
    {
        var search = new IconSearch(Sample);
        Assert.Equal("Home", search.Find("E80F")[0].Name);
        Assert.Equal("Home", search.Find(" e80f ")[0].Name);
    }

    [Fact]
    public void S1802_extra_terms_are_searched_like_tags()
    {
        var search = new IconSearch(Sample, new Dictionary<int, IReadOnlyList<string>> { [0xE80F] = ["Zuhause"] });
        Assert.Equal(["Home"], Names(search.Find("zuhause")));
    }

    [Fact]
    public void S1802_a_suggested_icon_wins_a_tie()
    {
        var search = new IconSearch(Sample, leading: [0xEB05]);

        // Both have "graph" as a tag; the suggested one goes first.
        Assert.Equal(["PieSingle", "AreaChart"], Names(search.Find("graph")));
    }

    [Fact]
    public void S1801_a_suggested_icon_is_found_and_named_in_the_users_language()
    {
        var search = FluentIcons.SearchFor(Locale.De);
        var home = FluentIcons.All.Single(i => i.Code == 0xE80F);

        Assert.Equal("Home", search.Find("zuhause")[0].Name);
        Assert.Equal("Zuhause", FluentIcons.NameFor(home, Locale.De));
        Assert.Equal("Global Nav Button", FluentIcons.NameFor(FluentIcons.All[0], Locale.De));
    }

    [Fact]
    public void S1802_punctuation_alone_still_searches_the_symbol_tags()
    {
        var search = new IconSearch(FluentIcons.All);
        Assert.Contains(search.Find("$"), icon => icon.Name == "PuncKey7");
    }

    [Fact]
    public void S1802_the_real_set_answers_everyday_words()
    {
        var search = FluentIcons.SearchFor(Locale.En);

        Assert.Equal("Home", search.Find(string.Empty)[0].Name);
        Assert.Equal("Home", search.Find("home")[0].Name);
        Assert.Equal("Mail", search.Find("mail")[0].Name);
        Assert.Equal(["AreaChart", "PieSingle"], Names(search.Find("chart").Take(2)));
        Assert.True(search.Find("graph").Count >= 3);
        Assert.Equal(FluentIcons.All.Count, search.Find(string.Empty).Count);
    }
}
