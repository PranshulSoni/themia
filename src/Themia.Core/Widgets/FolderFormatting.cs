using System.Globalization;

namespace Themia.Core.Widgets;

/// <summary>
/// The strings a folder row and a folder's empty state are drawn from.
/// <para>
/// Here rather than in the view for the reason every other rule in this widget is: the view cannot
/// be run in the test suite, and "what does the tile say when the path is a file" is exactly the
/// kind of thing that quietly becomes a blank tile.
/// </para>
/// </summary>
public static class FolderFormatting
{
    /// <summary>
    /// An entry's size, or nothing at all for a directory.
    /// <para>
    /// Explorer leaves a folder's size column blank rather than printing "0 B", and it is right to:
    /// a folder's size is the recursive walk nobody asked this widget to do, and "0 B" would be a
    /// wrong answer rather than an absent one.
    /// </para>
    /// </summary>
    public static string Size(FolderEntry entry) => entry.IsDirectory ? string.Empty : Formatting.Bytes(entry.Size);

    /// <summary>
    /// When an entry was last written, at the precision that is still worth the column width:
    /// the time for something touched today, the day and month within the year, the year beyond it.
    /// </summary>
    /// <param name="when">The entry's last write time.</param>
    /// <param name="now">
    /// The clock to read "today" from. Both arguments must be in the same frame — the view passes
    /// local wall-clock times for both — so this stays a pure function of what it is given rather
    /// than of the machine's time zone.
    /// </param>
    public static string Modified(DateTimeOffset when, DateTimeOffset now)
    {
        // A file with no usable timestamp comes back as the FILETIME epoch rather than as an error.
        // "1 Jan 1601" in a date column is noise pretending to be information.
        if (when.Year <= 1601)
        {
            return string.Empty;
        }

        var day = DateOnly.FromDateTime(when.DateTime);
        var today = DateOnly.FromDateTime(now.DateTime);

        if (day == today)
        {
            return when.DateTime.ToString("HH:mm", CultureInfo.InvariantCulture);
        }

        return day.Year == today.Year
            ? Formatting.ShortDate(day)
            : $"{Formatting.ShortDate(day)} {day.Year.ToString(CultureInfo.InvariantCulture)}";
    }

    /// <summary>
    /// What the tile says when it has no rows to show. Never empty: a widget that shows nothing and
    /// explains nothing is indistinguishable from one that is broken.
    /// </summary>
    /// <param name="status">The state the listing ended in.</param>
    /// <param name="problem">
    /// Why the path was refused, when that is the state. Each reason needs a different correction
    /// from the user, so they do not share a sentence.
    /// </param>
    public static string StatusMessage(FolderListingStatus status, FolderPathProblem problem = FolderPathProblem.None)
        => status switch
        {
            FolderListingStatus.Loading => "Reading this folder…",
            FolderListingStatus.Ok => string.Empty,
            FolderListingStatus.Empty => "This folder is empty.",
            FolderListingStatus.NotFound => "That folder does not exist.",
            FolderListingStatus.NotAFolder => "That path is a file, not a folder.",
            FolderListingStatus.AccessDenied => "Windows will not let Themia read that folder.",
            FolderListingStatus.Failed => "Could not read that folder. See themia.log.",
            // A refused path with no recorded reason should not happen, but "no rows and no
            // sentence" is the one outcome this whole function exists to rule out.
            FolderListingStatus.InvalidPath => problem == FolderPathProblem.None
                ? "That folder path cannot be read."
                : PathMessage(problem),
            _ => "Could not read that folder.",
        };

    /// <summary>The one-line reason shown under the path box in settings, or nothing when it is fine.</summary>
    public static string PathMessage(FolderPathProblem problem) => problem switch
    {
        FolderPathProblem.None => string.Empty,
        FolderPathProblem.Empty => "No folder set.",
        FolderPathProblem.InvalidCharacters => "A folder path cannot contain < > \" | ? or *.",
        FolderPathProblem.NotRooted => @"Enter a full path, like C:\Users\You\Documents.",
        FolderPathProblem.TooLong => "That path is longer than Windows allows.",
        _ => "That path cannot be read.",
    };
}
