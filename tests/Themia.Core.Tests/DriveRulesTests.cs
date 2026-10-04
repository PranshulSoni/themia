using System.Text.Json;
using Themia.Core.Model;
using Themia.Core.Widgets;
using Xunit;

namespace Themia.Core.Tests;

/// <summary>The OneDrive widget's rules, from the classic widget's SPEC [S-06]..[S-24].</summary>
public sealed class DriveRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);

    private static DriveItem File(string name, string? mime = null, long size = 2048) => new("id-" + name, name, size, Now.AddHours(-3), "https://onedrive.live.com/x", false, mime);

    [Fact]
    public void S606_maps_a_Graph_item_and_drops_one_without_an_id_or_name()
    {
        var item = JsonSerializer.Deserialize(
            """{"id":"A1","name":"Plan.docx","size":"1536","lastModifiedDateTime":"2026-10-04T09:00:00Z","webUrl":"https://onedrive.live.com/edit?id=A1","file":{"mimeType":"application/vnd.openxmlformats"}}""",
            DriveWireContext.Default.GraphDriveItem)!;

        var mapped = DriveRules.FromGraph(item)!;
        Assert.Equal("A1", mapped.Id);
        Assert.Equal(1536, mapped.Size);
        Assert.False(mapped.IsFolder);

        Assert.Null(DriveRules.FromGraph(new GraphDriveItem { Name = "x" }));
        Assert.Null(DriveRules.FromGraph(new GraphDriveItem { Id = "x", Name = "  " }));
    }

    [Fact]
    public void S609_only_a_folder_on_this_drive_is_walked_into()
    {
        Assert.True(DriveRules.FromGraph(new GraphDriveItem { Id = "f", Name = "Docs", Folder = new() })!.IsFolder);

        // A shared folder from another drive opens on the web like a file.
        var shared = DriveRules.FromGraph(new GraphDriveItem { Id = "r", Name = "Shared", RemoteItem = new() { Id = "rid", WebUrl = "https://x.sharepoint.com/s", Folder = new() } })!;
        Assert.False(shared.IsFolder);
        Assert.Equal("https://x.sharepoint.com/s", shared.WebUrl);
    }

    [Theory]
    [InlineData("photo.JPG", null, DriveItemKind.Image)]
    [InlineData("scan", "image/png", DriveItemKind.Image)]
    [InlineData("clip.mov", null, DriveItemKind.Video)]
    [InlineData("song.mp3", null, DriveItemKind.Audio)]
    [InlineData("budget.xlsx", null, DriveItemKind.Spreadsheet)]
    [InlineData("deck.pptx", null, DriveItemKind.Presentation)]
    [InlineData("notes.md", null, DriveItemKind.Document)]
    [InlineData("archive.zip", null, DriveItemKind.File)]
    [InlineData("noextension.", null, DriveItemKind.File)]
    public void S606_picks_the_icon_from_the_type_then_the_extension(string name, string? mime, DriveItemKind expected)
        => Assert.Equal(expected, DriveRules.Kind(File(name, mime)));

    [Fact]
    public void S606_a_file_says_size_and_age_a_folder_only_age()
    {
        Assert.Equal("2.0 KB · 3h ago", DriveRules.Meta(File("a.txt"), Now));
        Assert.Equal("3h ago", DriveRules.Meta(File("a.txt") with { IsFolder = true }, Now));
        Assert.Equal("2.0 KB", DriveRules.Meta(File("a.txt") with { Modified = null }, Now));
    }

    [Theory]
    [InlineData(-5, "Just now")] // a provider clock ahead of this one
    [InlineData(30, "Just now")]
    [InlineData(5 * 60, "5m ago")]
    [InlineData(3 * 3600, "3h ago")]
    [InlineData(2 * 86400, "2d ago")]
    public void S606_relative_time(int secondsAgo, string expected)
        => Assert.Equal(expected, DriveRules.Ago(Now.AddSeconds(-secondsAgo), Now));

    [Fact]
    public void S606_older_than_a_week_is_a_date()
        => Assert.Matches(@"^\d{1,2} [A-Z][a-z]{2}$", DriveRules.Ago(Now.AddDays(-20), Now));

    [Fact]
    public void S609_the_trail_walks_in_and_back_to_the_configured_start()
    {
        var folder = new DriveItem("F1", "Docs", 0, null, string.Empty, true, null);
        var trail = DriveRules.Enter([], folder);

        Assert.Equal("F1", Assert.Single(trail).Id);
        Assert.Contains("/me/drive/items/F1/children", DriveRules.Address(trail, DriveStart.Recent, 15).AbsoluteUri, StringComparison.Ordinal);

        var back = DriveRules.Back(trail);
        Assert.Empty(back);
        Assert.Contains("/me/drive/recent", DriveRules.Address(back, DriveStart.Recent, 15).AbsoluteUri, StringComparison.Ordinal);
        Assert.Contains("/me/drive/root/children", DriveRules.Address(back, DriveStart.Root, 15).AbsoluteUri, StringComparison.Ordinal);

        Assert.Same(back, DriveRules.Back(back));
        Assert.Same(back, DriveRules.Enter(back, File("a.txt")));
    }

    [Fact]
    public void S607_the_count_is_clamped_and_a_folder_id_cannot_add_a_segment()
    {
        Assert.Contains("$top=50", DriveRules.Recent(500).Query, StringComparison.Ordinal);
        Assert.Contains("$top=5", DriveRules.Recent(0).Query, StringComparison.Ordinal);
        Assert.Contains("/items/a%2F..%2Fb/children", DriveRules.Children("a/../b", 15).AbsoluteUri, StringComparison.Ordinal);
    }
}
