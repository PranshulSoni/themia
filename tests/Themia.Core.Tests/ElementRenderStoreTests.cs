using Themia.Core.Storage;
using Xunit;

namespace Themia.Core.Tests;

/// <summary>[S-1319] The web element's saved renders: round trip, validation, pruning and the files on disk.</summary>
public sealed class ElementRenderStoreTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 15, 0, 0, TimeSpan.FromHours(2));

    private const string Url = "https://example.org/prices";
    private const string Selector = "#price > span:nth-of-type(2)";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "themia-renders-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private static byte[] Png(int length = 64)
    {
        var bytes = new byte[length];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(bytes, 0);
        return bytes;
    }

    private static ElementRender Render(string url = Url, string selector = Selector, DateTimeOffset? at = null, long bytes = 64)
        => new(url, selector, at ?? Now.AddHours(-2), 320, 80, 1.5, ElementRenderStore.FileFor(url, selector), bytes);

    [Fact]
    public void S1319_an_index_survives_the_round_trip()
    {
        var back = ElementRenderStore.Parse(ElementRenderStore.Serialize([Render()]), Now);

        var entry = Assert.Single(back!);
        Assert.Equal(Render(), entry);
    }

    [Fact]
    public void S1319_the_file_name_is_derived_never_read_from_the_index()
    {
        var json = ElementRenderStore.Serialize([Render()]).Replace("\"bytes\"", "\"file\":\"..\\\\..\\\\evil.png\",\"bytes\"", StringComparison.Ordinal);
        var entry = Assert.Single(ElementRenderStore.Parse(json, Now)!);

        Assert.Equal(ElementRenderStore.FileFor(Url, Selector), entry.File);
        Assert.Matches("^[0-9a-f]{32}\\.png$", entry.File);
    }

    [Fact]
    public void S1319_different_pages_or_selectors_get_different_files()
    {
        Assert.NotEqual(ElementRenderStore.FileFor(Url, "#a"), ElementRenderStore.FileFor(Url, "#b"));
        Assert.NotEqual(ElementRenderStore.FileFor(Url + "?x", "#a"), ElementRenderStore.FileFor(Url, "#a"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"schemaVersion\":2,\"renders\":[]}")]
    [InlineData("{\"schemaVersion\":1}")]
    public void S1319_an_index_that_is_not_this_schema_is_corrupt(string json)
        => Assert.Null(ElementRenderStore.Parse(json, Now));

    [Fact]
    public void S1319_entries_that_are_not_usable_renders_are_dropped()
    {
        var entries = new[]
        {
            Render(url: "file:///C:/secret.html"),
            Render(selector: "a\u0001b"),
            Render(at: Now.AddDays(-40)),
            Render(at: Now.AddHours(1)),
            Render(bytes: 0),
            Render(bytes: ElementRenderStore.MaxFileBytes + 1),
            Render(url: "https://ok.example/", selector: "#fine"),
        };

        var back = ElementRenderStore.Parse(ElementRenderStore.Serialize(entries), Now);
        var entry = Assert.Single(back!);
        Assert.Equal("#fine", entry.Selector);
    }

    [Fact]
    public void S1319_a_size_or_zoom_out_of_range_is_dropped()
    {
        var bad = new[]
        {
            Render() with { CssWidth = 0 },
            Render() with { CssHeight = 99999 },
            Render() with { Zoom = 4 },
            Render() with { Zoom = -1 },
        };

        foreach (var entry in bad)
        {
            Assert.Empty(ElementRenderStore.Parse(ElementRenderStore.Serialize([entry]), Now)!);
        }
    }

    [Fact]
    public void S1319_of_two_entries_for_the_same_element_the_newer_wins()
    {
        var back = ElementRenderStore.Parse(ElementRenderStore.Serialize([Render(at: Now.AddHours(-5)), Render(at: Now.AddHours(-1))]), Now);
        Assert.Equal(Now.AddHours(-1), Assert.Single(back!).At);
    }

    [Fact]
    public void S1319_pruning_keeps_the_newest_within_the_count_and_byte_caps()
    {
        var many = Enumerable.Range(0, ElementRenderStore.MaxEntries + 5)
            .Select(i => Render(selector: $"#e{i}", at: Now.AddMinutes(-i)))
            .ToList();

        var (kept, dropped) = ElementRenderStore.Prune(many, Now);
        Assert.Equal(ElementRenderStore.MaxEntries, kept.Count);
        Assert.Equal(5, dropped.Count);
        Assert.Equal("#e0", kept[0].Selector);
        Assert.All(dropped, entry => Assert.True(entry.At < kept[^1].At));

        var heavy = new[]
        {
            Render(selector: "#new", at: Now, bytes: ElementRenderStore.MaxTotalBytes - 10),
            Render(selector: "#old", at: Now.AddMinutes(-1), bytes: 64),
        };
        var (keptHeavy, droppedHeavy) = ElementRenderStore.Prune(heavy, Now);
        Assert.Equal("#new", Assert.Single(keptHeavy).Selector);
        Assert.Equal("#old", Assert.Single(droppedHeavy).Selector);
    }

    [Fact]
    public void S1319_only_a_png_of_sane_size_is_storable()
    {
        Assert.True(ElementRenderStore.IsStorable(Png()));
        Assert.False(ElementRenderStore.IsStorable([1, 2, 3, 4, 5, 6, 7, 8, 9]));
        Assert.False(ElementRenderStore.IsStorable(Png(8)));
    }

    [Fact]
    public void S1319_an_absent_index_is_a_quiet_empty_cache()
    {
        var (entries, outcome) = ElementRenderStore.Load(_dir, Now);
        Assert.Empty(entries);
        Assert.Equal(ElementRenderLoadOutcome.Fresh, outcome);
    }

    [Fact]
    public void S1319_a_corrupt_index_is_set_aside_not_overwritten()
    {
        Directory.CreateDirectory(_dir);
        var index = Path.Combine(_dir, ElementRenderStore.IndexName);
        File.WriteAllText(index, "{ broken");

        var (entries, outcome) = ElementRenderStore.Load(_dir, Now);

        Assert.Empty(entries);
        Assert.Equal(ElementRenderLoadOutcome.Recovered, outcome);
        Assert.False(File.Exists(index));
        Assert.Equal("{ broken", File.ReadAllText(index + ".corrupt"));
    }

    [Fact]
    public void S1319_a_save_is_read_back_and_dropped_pictures_are_deleted()
    {
        var old = Render(selector: "#old");
        var png = Png();
        Assert.True(ElementRenderStore.Save(_dir, old, png, [old], []));

        var fresh = Render(selector: "#new", at: Now);
        Assert.True(ElementRenderStore.Save(_dir, fresh, png, [fresh], [old]));

        var (entries, outcome) = ElementRenderStore.Load(_dir, Now);
        Assert.Equal(ElementRenderLoadOutcome.Loaded, outcome);
        Assert.Equal("#new", Assert.Single(entries).Selector);
        Assert.False(File.Exists(Path.Combine(_dir, old.File)));
        Assert.Equal(png, ElementRenderStore.ReadPicture(_dir, fresh));
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp.*"));
    }

    [Fact]
    public void S1319_an_entry_whose_picture_is_missing_or_torn_is_not_drawn()
    {
        var entry = Render();
        Assert.True(ElementRenderStore.Save(_dir, entry, Png(), [entry], []));

        // Torn: a different length from what the index recorded.
        File.WriteAllBytes(Path.Combine(_dir, entry.File), Png(32));
        Assert.Null(ElementRenderStore.ReadPicture(_dir, entry));

        File.Delete(Path.Combine(_dir, entry.File));
        Assert.Empty(ElementRenderStore.Load(_dir, Now).Entries);
    }
}
