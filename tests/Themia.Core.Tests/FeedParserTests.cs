using Themia.Core.Widgets;
using Xunit;

namespace Themia.Core.Tests;

/// <summary>
/// The feed parser, which is the one place in this app that reads a document written entirely by
/// somebody else, fetched over the network, and re-read every few minutes forever.
/// <para>
/// So the cases that matter most here are not the well-formed ones. They are the DTD, the entity
/// expansion, the <c>javascript:</c> link and the HTML page served with a 200 — each of which is
/// something a feed can genuinely contain and none of which may reach the screen or the shell.
/// </para>
/// </summary>
public class FeedParserTests
{
    private const string Rss = """
        <?xml version="1.0" encoding="UTF-8"?>
        <rss version="2.0">
          <channel>
            <title>Tech News</title>
            <link>https://example.com</link>
            <item>
              <title>First story</title>
              <link>https://example.com/one</link>
              <pubDate>Wed, 23 Sep 2026 10:00:00 GMT</pubDate>
              <description>&lt;p&gt;A &lt;b&gt;bold&lt;/b&gt; claim.&lt;/p&gt;</description>
            </item>
            <item>
              <title>Second story</title>
              <link>https://example.com/two</link>
              <pubDate>Tue, 22 Sep 2026 09:30:00 +0200</pubDate>
              <description>Plain words.</description>
            </item>
          </channel>
        </rss>
        """;

    private const string Atom = """
        <?xml version="1.0" encoding="utf-8"?>
        <feed xmlns="http://www.w3.org/2005/Atom">
          <title>Atom Journal</title>
          <entry>
            <title>An entry</title>
            <link rel="edit" href="https://example.com/edit/1"/>
            <link rel="alternate" href="https://example.com/entry/1"/>
            <updated>2026-09-23T10:00:00Z</updated>
            <summary>A summary.</summary>
          </entry>
          <entry>
            <title>Content only</title>
            <link href="https://example.com/entry/2"/>
            <published>2026-09-22T08:00:00+02:00</published>
            <content>Body text instead of a summary.</content>
          </entry>
        </feed>
        """;

    // ---- the security cases ----------------------------------------------------------------

    [Fact]
    public void A_document_with_a_doctype_is_refused_outright()
    {
        // The classic XXE shape. Prohibited rather than ignored: there is no reason to go on
        // parsing the rest of a document that opened by trying this.
        const string Xxe = """
            <?xml version="1.0"?>
            <!DOCTYPE rss [<!ENTITY xxe SYSTEM "file:///C:/Windows/win.ini">]>
            <rss version="2.0"><channel><title>&xxe;</title>
            <item><title>&xxe;</title><link>https://example.com/a</link></item>
            </channel></rss>
            """;

        var result = FeedParser.Parse(Xxe, 20);

        Assert.Equal(FeedParseStatus.Malformed, result.Status);
        Assert.Empty(result.Document.Entries);
    }

    [Fact]
    public void A_billion_laughs_expansion_is_refused_rather_than_expanded()
    {
        const string Bomb = """
            <?xml version="1.0"?>
            <!DOCTYPE lolz [
              <!ENTITY lol "lol">
              <!ENTITY lol2 "&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;">
              <!ENTITY lol3 "&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;">
            ]>
            <rss version="2.0"><channel><title>&lol3;</title></channel></rss>
            """;

        // It never expands, because the DTD that declares the entities is refused first.
        Assert.Equal(FeedParseStatus.Malformed, FeedParser.Parse(Bomb, 20).Status);
    }

    [Theory]
    [InlineData("javascript:alert(document.cookie)")]
    [InlineData("file:///C:/Windows/System32/cmd.exe")]
    [InlineData(@"\\attacker\share\payload.exe")]
    [InlineData("ftp://example.com/x")]
    public void A_link_that_is_not_http_is_dropped(string link)
    {
        // The link ends up at the shell, so this is the whole attack surface of the widget.
        var xml = $"""
            <rss version="2.0"><channel><title>T</title>
            <item><title>Click me</title><link>{link}</link></item>
            </channel></rss>
            """;

        var result = FeedParser.Parse(xml, 20);

        Assert.Equal(FeedParseStatus.Ok, result.Status);
        Assert.Equal("Click me", result.Document.Entries[0].Title);
        Assert.Equal(string.Empty, result.Document.Entries[0].Link);
    }

    [Fact]
    public void Markup_in_a_title_is_stripped_rather_than_carried()
    {
        var xml = """
            <rss version="2.0"><channel><title>T</title>
            <item><title>&lt;script&gt;alert(1)&lt;/script&gt;Breaking</title>
            <link>https://example.com/a</link></item>
            </channel></rss>
            """;

        var title = FeedParser.Parse(xml, 20).Document.Entries[0].Title;

        Assert.DoesNotContain("<", title, StringComparison.Ordinal);
        Assert.DoesNotContain(">", title, StringComparison.Ordinal);
        Assert.Contains("Breaking", title, StringComparison.Ordinal);
    }

    // ---- RSS -------------------------------------------------------------------------------

    [Fact]
    public void An_rss_channel_yields_its_title_and_its_items()
    {
        var result = FeedParser.Parse(Rss, 20);

        Assert.Equal(FeedParseStatus.Ok, result.Status);
        Assert.Equal("Tech News", result.Document.Title);
        Assert.Equal(2, result.Document.Entries.Count);
        Assert.Equal("First story", result.Document.Entries[0].Title);
        Assert.Equal("https://example.com/one", result.Document.Entries[0].Link);
    }

    [Fact]
    public void An_escaped_html_description_becomes_one_line_of_plain_text()
    {
        var summary = FeedParser.Parse(Rss, 20).Document.Entries[0].Summary;

        Assert.Equal("A bold claim.", summary);
    }

    [Fact]
    public void The_item_cap_is_honoured()
        => Assert.Single(FeedParser.Parse(Rss, 1).Document.Entries);

    [Fact]
    public void An_rfc_822_date_with_a_zone_name_parses()
    {
        var published = FeedParser.Parse(Rss, 20).Document.Entries[0].Published;

        Assert.NotNull(published);
        Assert.Equal(new DateTimeOffset(2026, 9, 23, 10, 0, 0, TimeSpan.Zero), published!.Value.ToUniversalTime());
    }

    [Fact]
    public void An_rfc_822_date_with_a_numeric_offset_parses()
    {
        var published = FeedParser.Parse(Rss, 20).Document.Entries[1].Published;

        Assert.NotNull(published);
        Assert.Equal(new DateTimeOffset(2026, 9, 22, 7, 30, 0, TimeSpan.Zero), published!.Value.ToUniversalTime());
    }

    // ---- Atom ------------------------------------------------------------------------------

    [Fact]
    public void An_atom_feed_yields_its_title_and_its_entries()
    {
        var result = FeedParser.Parse(Atom, 20);

        Assert.Equal(FeedParseStatus.Ok, result.Status);
        Assert.Equal("Atom Journal", result.Document.Title);
        Assert.Equal(2, result.Document.Entries.Count);
    }

    [Fact]
    public void The_alternate_link_is_preferred_over_the_edit_link()
    {
        // An entry usually carries several: the article, a comments feed, an edit endpoint. Only
        // one of them is the thing a reader wants to open.
        Assert.Equal("https://example.com/entry/1", FeedParser.Parse(Atom, 20).Document.Entries[0].Link);
    }

    [Fact]
    public void A_link_with_no_rel_counts_as_the_alternate_one()
        => Assert.Equal("https://example.com/entry/2", FeedParser.Parse(Atom, 20).Document.Entries[1].Link);

    [Fact]
    public void Content_stands_in_for_a_missing_summary()
        => Assert.Equal("Body text instead of a summary.", FeedParser.Parse(Atom, 20).Document.Entries[1].Summary);

    [Fact]
    public void Published_stands_in_for_a_missing_updated()
        => Assert.NotNull(FeedParser.Parse(Atom, 20).Document.Entries[1].Published);

    // ---- the failure shapes ----------------------------------------------------------------

    [Fact]
    public void An_html_page_served_in_place_of_a_feed_is_told_apart_from_a_broken_one()
    {
        // A consent wall, a login page or a "subscribe here" landing page, served with a 200. It
        // is well-formed and it is not a feed, and the reader needs to be told which.
        const string Page = "<html><head><title>Subscribe</title></head><body><p>Sign in</p></body></html>";

        Assert.Equal(FeedParseStatus.NotFeed, FeedParser.Parse(Page, 20).Status);
    }

    [Fact]
    public void A_real_feed_with_no_items_is_empty_rather_than_broken()
    {
        // "The feed published nothing" and "the feed would not parse" need different sentences.
        const string Quiet = """<rss version="2.0"><channel><title>Quiet</title></channel></rss>""";

        var result = FeedParser.Parse(Quiet, 20);

        Assert.Equal(FeedParseStatus.Empty, result.Status);
        Assert.Equal("Quiet", result.Document.Title);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("not xml at all")]
    [InlineData("<rss><channel><title>unclosed")]
    [InlineData("\uFFFF\u0000garbage")]
    public void Nothing_usable_is_malformed_and_never_throws(string? xml)
        => Assert.Equal(FeedParseStatus.Malformed, FeedParser.Parse(xml, 20).Status);

    [Fact]
    public void An_item_with_nothing_in_it_is_dropped_rather_than_drawn_as_a_blank_row()
    {
        const string Padded = """
            <rss version="2.0"><channel><title>T</title>
            <item></item>
            <item><title>Real</title><link>https://example.com/a</link></item>
            <item><guid>only-a-guid</guid></item>
            </channel></rss>
            """;

        var entries = FeedParser.Parse(Padded, 20).Document.Entries;

        Assert.Single(entries);
        Assert.Equal("Real", entries[0].Title);
    }

    [Fact]
    public void A_namespaced_feed_still_parses()
    {
        // Feeds in the wild put RSS elements in a dozen namespaces or declare none at all.
        const string Namespaced = """
            <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#" xmlns="http://purl.org/rss/1.0/">
              <channel><title>Old School</title></channel>
              <item><title>An item</title><link>https://example.com/a</link></item>
            </rdf:RDF>
            """;

        var result = FeedParser.Parse(Namespaced, 20);

        Assert.Equal("Old School", result.Document.Title);
    }

    [Fact]
    public void An_undated_item_keeps_a_null_date_rather_than_claiming_to_be_current()
    {
        // Defaulting to "now" would sort an undated item to the top and present it as the latest
        // news, which is worse than admitting it has no date.
        const string Undated = """
            <rss version="2.0"><channel><title>T</title>
            <item><title>No date</title><link>https://example.com/a</link><pubDate>not a date</pubDate></item>
            </channel></rss>
            """;

        Assert.Null(FeedParser.Parse(Undated, 20).Document.Entries[0].Published);
    }
}

/// <summary>Turning a feed's markup into one line of text a text block can hold.</summary>
public class FeedTextTests
{
    [Fact]
    public void Tags_come_out_and_the_words_either_side_stay_apart()
    {
        // "<p>one</p><p>two</p>" running together as "onetwo" is the obvious way to get this wrong.
        Assert.Equal("one two", FeedText.Plain("<p>one</p><p>two</p>"));
    }

    [Fact]
    public void Runs_of_whitespace_collapse_to_one_space()
        => Assert.Equal("a b", FeedText.Plain("a   \n\t  b"));

    [Theory]
    [InlineData("&amp;", "&")]
    [InlineData("&lt;tag&gt;", "<tag>")]
    [InlineData("&quot;quoted&quot;", "\"quoted\"")]
    [InlineData("&#39;apostrophe&#39;", "'apostrophe'")]
    [InlineData("&#8230;", "…")]
    [InlineData("&#x2014;", "—")]
    public void The_entities_a_feed_still_ships_after_one_xml_decode_are_resolved(string input, string expected)
        => Assert.Equal(expected, FeedText.Plain(input));

    [Fact]
    public void An_entity_is_decoded_once_and_not_in_a_loop()
    {
        // Decoding repeatedly is how "&amp;lt;script&amp;gt;" eventually becomes a tag again.
        Assert.Equal("&lt;b&gt;", FeedText.Plain("&amp;lt;b&amp;gt;"));
    }

    [Fact]
    public void An_unknown_entity_is_left_exactly_as_written()
        => Assert.Equal("&unicorn; here", FeedText.Plain("&unicorn; here"));

    [Fact]
    public void A_stray_ampersand_survives()
        => Assert.Equal("Rock & roll", FeedText.Plain("Rock & roll"));

    [Fact]
    public void A_long_summary_is_cut_on_a_word_boundary_and_marked()
    {
        var long_ = string.Join(' ', Enumerable.Repeat("word", 200));
        var plain = FeedText.Plain(long_, 60);

        Assert.True(plain.Length <= 61);
        Assert.EndsWith("…", plain, StringComparison.Ordinal);
        Assert.DoesNotContain("wor…", plain, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("<p></p>")]
    public void Nothing_in_yields_nothing_out(string? markup)
        => Assert.Equal(string.Empty, FeedText.Plain(markup));

    [Theory]
    [InlineData("Wed, 23 Sep 2026 10:00:00 GMT")]
    [InlineData("Wed, 23 Sep 2026 10:00:00 +0000")]
    [InlineData("Tue, 23 Sep 2026 10:00:00 +0000")]
    [InlineData("23 Sep 2026 10:00:00 +0000")]
    [InlineData("2026-09-23T10:00:00Z")]
    [InlineData("2026-09-23T10:00:00.000Z")]
    [InlineData("2026-09-23T12:00:00+02:00")]
    public void The_date_shapes_feeds_actually_use_all_parse_to_the_same_instant(string raw)
    {
        var parsed = FeedText.Date(raw);

        Assert.NotNull(parsed);
        Assert.Equal(new DateTimeOffset(2026, 9, 23, 10, 0, 0, TimeSpan.Zero), parsed!.Value.ToUniversalTime());
    }

    [Fact]
    public void A_publisher_that_names_the_wrong_weekday_does_not_lose_its_timestamp()
    {
        // 23 September 2026 was a Wednesday. The day name is redundant with the rest of the
        // string, and letting an exact parse validate it means one publisher's off-by-one silently
        // turns every article in the feed into an undated one.
        var parsed = FeedText.Date("Tue, 23 Sep 2026 10:00:00 +0000");

        Assert.NotNull(parsed);
        Assert.Equal(new DateTimeOffset(2026, 9, 23, 10, 0, 0, TimeSpan.Zero), parsed!.Value.ToUniversalTime());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("yesterday")]
    [InlineData("0000-00-00")]
    public void A_date_that_will_not_parse_is_null_rather_than_now(string? raw)
        => Assert.Null(FeedText.Date(raw));
}
