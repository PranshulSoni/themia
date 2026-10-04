using Themia.Core.Widgets;
using Xunit;

namespace Themia.Core.Tests;

/// <summary>
/// Which files a gallery treats as pictures, and which of them a tile ends up showing.
/// <para>
/// The filter is the cheapest place in the widget to be wrong: every name it lets through costs a
/// file open and a decode attempt, per file, on every scan. So the awkward names — no extension, an
/// extension and nothing else, a dot at the end, a folder path that happens to contain ".jpg" — are
/// all pinned here rather than discovered on somebody's photo library.
/// </para>
/// </summary>
public class ImageFilesTests
{
    // -----------------------------------------------------------------------------------------
    // [S-303] what counts as a picture
    // -----------------------------------------------------------------------------------------

    [Theory]
    [InlineData("photo.jpg")]
    [InlineData("photo.jpeg")]
    [InlineData("photo.JPG")]
    [InlineData("photo.Png")]
    [InlineData("scan.TIFF")]
    [InlineData("icon.ico")]
    [InlineData("animation.gif")]
    [InlineData("shot.webp")]
    [InlineData("old.bmp")]
    [InlineData("old.dib")]
    [InlineData("mail.jfif")]
    [InlineData("mail.jpe")]
    public void S303_the_known_extensions_are_pictures(string name)
        => Assert.True(ImageFiles.IsImage(name));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("README")]
    [InlineData("notes.txt")]
    [InlineData("archive.zip")]
    [InlineData("raw.cr2")]         // needs a codec most machines do not have
    [InlineData("phone.heic")]      // likewise
    [InlineData("video.mp4")]
    [InlineData("photo.jpg.txt")]   // the last extension is the one that counts
    [InlineData("photo.")]          // a trailing dot is not an extension
    [InlineData(".jpg")]            // an extension and no name at all
    [InlineData("photo")]
    public void S303_everything_else_is_not(string? name)
        => Assert.False(ImageFiles.IsImage(name));

    [Theory]
    [InlineData(@"C:\Users\me\Pictures\holiday.jpg")]
    [InlineData("C:/Users/me/Pictures/holiday.jpg")]
    [InlineData(@"\\server\share\holiday.JPG")]
    public void S303_a_full_path_is_judged_by_its_last_segment(string path)
        => Assert.True(ImageFiles.IsImage(path));

    [Theory]
    [InlineData(@"C:\my.jpg folder\notes.txt")]
    [InlineData(@"C:\photos.png\README")]
    public void S303_a_folder_named_like_a_picture_does_not_make_its_files_pictures(string path)
        => Assert.False(ImageFiles.IsImage(path));

    [Fact]
    public void S303_a_dotted_name_is_judged_by_its_last_extension()
    {
        Assert.True(ImageFiles.IsImage("2026.06.15 holiday.png"));
        Assert.False(ImageFiles.IsImage("2026.06.15 holiday.png.bak"));
    }

    // -----------------------------------------------------------------------------------------
    // [S-304] which of them a tile gets
    // -----------------------------------------------------------------------------------------

    private static readonly string[] Found = ["c.jpg", "a.jpg", "B.jpg", "d.jpg", "e.jpg"];

    [Fact]
    public void S304_unshuffled_is_a_stable_order_rather_than_the_filesystems()
    {
        var chosen = ImageFiles.Choose(Found, shuffle: false, seed: 0, max: 5);

        Assert.Equal(new[] { "a.jpg", "B.jpg", "c.jpg", "d.jpg", "e.jpg" }, chosen);
    }

    [Fact]
    public void S304_the_cap_is_what_bounds_everything_downstream()
    {
        var chosen = ImageFiles.Choose(Found, shuffle: false, seed: 0, max: 2);

        Assert.Equal(new[] { "a.jpg", "B.jpg" }, chosen);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-4)]
    public void S304_a_cap_below_one_still_yields_a_picture(int max)
        => Assert.Single(ImageFiles.Choose(Found, shuffle: false, seed: 0, max: max));

    [Fact]
    public void S304_a_cap_above_what_was_found_yields_what_was_found()
        => Assert.Equal(Found.Length, ImageFiles.Choose(Found, shuffle: false, seed: 0, max: 500).Count);

    [Fact]
    public void S304_nothing_found_is_nothing_chosen()
    {
        Assert.Empty(ImageFiles.Choose(null, shuffle: true, seed: 1, max: 10));
        Assert.Empty(ImageFiles.Choose([], shuffle: true, seed: 1, max: 10));
    }

    [Fact]
    public void S304_blank_paths_are_dropped_rather_than_taking_a_tile_each()
    {
        var chosen = ImageFiles.Choose(["a.jpg", string.Empty, "  ", "b.jpg"], shuffle: false, seed: 0, max: 10);

        Assert.Equal(new[] { "a.jpg", "b.jpg" }, chosen);
    }

    [Fact]
    public void S304_a_shuffle_is_a_permutation_and_not_a_subset_of_the_front()
    {
        // Shuffling the whole list before taking the front is what gives every picture in a large
        // folder a chance of being shown; shuffling only the first N would not.
        var chosen = ImageFiles.Choose(Found, shuffle: true, seed: 7, max: Found.Length);

        Assert.Equal(Found.Length, chosen.Count);
        Assert.Equal(Found.Order(StringComparer.Ordinal), chosen.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void S304_the_same_seed_gives_the_same_selection()
    {
        var first = ImageFiles.Choose(Found, shuffle: true, seed: 42, max: 3);
        var second = ImageFiles.Choose(Found, shuffle: true, seed: 42, max: 3);

        Assert.Equal(first, second);
    }

    [Fact]
    public void S304_the_seed_actually_moves_the_selection()
    {
        // Not a guarantee about any particular seed, but over a range of them a five-item list must
        // produce more than one order, or the "shuffle" is doing nothing.
        var orders = Enumerable
            .Range(0, 40)
            .Select(seed => string.Join(",", ImageFiles.Choose(Found, shuffle: true, seed: seed, max: 3)))
            .Distinct()
            .Count();

        Assert.True(orders > 1, "shuffling with forty different seeds produced one selection");
    }
}
