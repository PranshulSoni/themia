using Themia.Core.Model;

namespace Themia.Core.Widgets;

/// <summary>
/// How trying to read a folder of pictures ended.
/// <para>
/// Every one of these is a state the widget has its own sentence for. A photo frame showing a blank
/// card is indistinguishable from a broken one, and each of these needs a different thing from the
/// reader — choose a folder, fix the path, grant access, put some pictures in it, or simply wait.
/// </para>
/// </summary>
public enum GalleryStatus
{
    /// <summary>The scan is in flight. A large tree or a disconnected share can sit here.</summary>
    Loading = 0,

    /// <summary>Read, and there are pictures.</summary>
    Ok = 1,

    /// <summary>No folder has been chosen yet.</summary>
    NoFolder = 2,

    /// <summary>The configured path cannot be a path at all, so nothing was opened.</summary>
    InvalidPath = 3,

    /// <summary>Nothing exists at that path any more.</summary>
    NotFound = 4,

    /// <summary>Something is there, but it is a file rather than a folder.</summary>
    NotAFolder = 5,

    /// <summary>Windows refused the read.</summary>
    AccessDenied = 6,

    /// <summary>Read, and there is nothing in it this app can decode.</summary>
    NoImages = 7,

    /// <summary>The read failed some other way. Details are in the log.</summary>
    Failed = 8,

    /// <summary>[S-761] The file source has no picture chosen yet.</summary>
    NoFile = 9,

    /// <summary>[S-761] The chosen file is not a picture this app can draw (or is a folder).</summary>
    NotAPicture = 10,

    /// <summary>[S-762] This machine has no network, so the photo of the day cannot be asked for.</summary>
    Offline = 11,

    /// <summary>[S-762] Bing did not answer, or answered with something that is not a photo.</summary>
    Unavailable = 12,
}

/// <summary>[S-763] The words that go with a photo of the day: what it shows, and whose it is.</summary>
public sealed record GalleryCaption(string Title, string Credit);

/// <summary>What one scan found: a named state, and the pictures that go with it.</summary>
/// <param name="Status">Which state the scan ended in.</param>
/// <param name="Paths">
/// The chosen pictures, already filtered, ordered and capped. Empty unless Ok. Fully qualified local
/// paths, except for the photo of the day, whose one entry is a validated https address.
/// </param>
public sealed record GalleryScan(GalleryStatus Status, IReadOnlyList<string> Paths)
{
    /// <summary>[S-763] The photo of the day's title and credit; null for every other source.</summary>
    public GalleryCaption? Caption { get; init; }

    /// <summary>A result carrying no pictures, for every state that has none.</summary>
    public static GalleryScan Of(GalleryStatus status) => new(status, []);
}

/// <summary>The sentences the gallery shows instead of a picture.</summary>
public static class GalleryFormatting
{
    /// <summary>
    /// What to say about a scan that produced nothing. Each names the state and, where there is one,
    /// the thing to do about it — a message that only says something went wrong leaves the reader
    /// with the same blank card they started with.
    /// </summary>
    public static string StatusMessage(GalleryStatus status, FolderPathProblem problem, GallerySource source = GallerySource.Folder) => status switch
    {
        GalleryStatus.Loading when source == GallerySource.Bing => "Loading today's photo…",
        GalleryStatus.Loading => "Loading pictures…",
        GalleryStatus.NoFolder => "No folder chosen.\nPick one in this widget's settings.",
        GalleryStatus.NoFile => "No picture chosen.\nPick one in this widget's settings.",
        GalleryStatus.NotFound when source == GallerySource.File => "That picture is not there any more.",
        GalleryStatus.NotFound => "That folder is not there any more.",
        GalleryStatus.NotAPicture => "That file is not a picture this app can show.",
        GalleryStatus.Offline => "Offline. Today's photo loads when the network is back.",
        GalleryStatus.Unavailable => "Could not load today's photo.",
        GalleryStatus.NotAFolder => "That is a file, not a folder.",
        GalleryStatus.AccessDenied when source == GallerySource.File => "Windows will not let this app read that picture.",
        GalleryStatus.AccessDenied => "Windows will not let this app read that folder.",
        GalleryStatus.NoImages => "No pictures in this folder.\nTurn on subfolders, or pick another.",
        GalleryStatus.InvalidPath => FolderFormatting.PathMessage(problem),
        GalleryStatus.Failed when source == GallerySource.File => "That picture could not be read.",
        GalleryStatus.Failed => "That folder could not be read.",

        // Ok with nothing in it is the same emptiness as NoImages, and saying "no pictures" is
        // truer than saying nothing.
        _ => "No pictures in this folder.\nTurn on subfolders, or pick another.",
    };
}
