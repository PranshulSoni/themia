using System.Globalization;
using System.Text.Json.Serialization;
using Themia.Core.Model;

namespace Themia.Core.Widgets;

/// <summary>Graph <c>driveItem</c>, just what the widget reads.</summary>
public sealed class GraphDriveItem
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("size")]
    public long? Size { get; set; }

    [JsonPropertyName("lastModifiedDateTime")]
    public DateTimeOffset? LastModified { get; set; }

    [JsonPropertyName("webUrl")]
    public string? WebUrl { get; set; }

    /// <summary>Present (with any content) exactly when the item is a folder.</summary>
    [JsonPropertyName("folder")]
    public GraphDriveFolder? Folder { get; set; }

    [JsonPropertyName("file")]
    public GraphDriveFile? File { get; set; }

    /// <summary>On <c>/me/drive/recent</c>, an item shared from someone else's drive; its web address may only be here.</summary>
    [JsonPropertyName("remoteItem")]
    public GraphRemoteItem? RemoteItem { get; set; }

    /// <summary>[S-1293] Who changed it last.</summary>
    [JsonPropertyName("lastModifiedBy")]
    public GraphIdentitySet? LastModifiedBy { get; set; }

    /// <summary>[S-1293] The folder it sits in.</summary>
    [JsonPropertyName("parentReference")]
    public GraphItemReference? ParentReference { get; set; }
}

public sealed class GraphIdentitySet
{
    [JsonPropertyName("user")]
    public GraphIdentity? User { get; set; }
}

public sealed class GraphIdentity
{
    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }
}

public sealed class GraphItemReference
{
    /// <summary>Like <c>/drive/root:/Documents/Reports</c>; absent for an item at the top of another drive.</summary>
    [JsonPropertyName("path")]
    public string? Path { get; set; }
}

/// <summary>[S-1294] Graph <c>drive</c>, just its quota.</summary>
public sealed class GraphDrive
{
    [JsonPropertyName("quota")]
    public GraphQuota? Quota { get; set; }
}

public sealed class GraphQuota
{
    [JsonPropertyName("used")]
    public long? Used { get; set; }

    [JsonPropertyName("total")]
    public long? Total { get; set; }
}

public sealed class GraphDriveFolder
{
    [JsonPropertyName("childCount")]
    public long? ChildCount { get; set; }
}

public sealed class GraphDriveFile
{
    [JsonPropertyName("mimeType")]
    public string? MimeType { get; set; }
}

public sealed class GraphRemoteItem
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("webUrl")]
    public string? WebUrl { get; set; }

    [JsonPropertyName("folder")]
    public GraphDriveFolder? Folder { get; set; }

    [JsonPropertyName("lastModifiedBy")]
    public GraphIdentitySet? LastModifiedBy { get; set; }

    [JsonPropertyName("parentReference")]
    public GraphItemReference? ParentReference { get; set; }
}

/// <summary>Source-generated contract for the drive endpoints. Native AOT has no reflection serializer.</summary>
[JsonSourceGenerationOptions(NumberHandling = JsonNumberHandling.AllowReadingFromString)]
[JsonSerializable(typeof(GraphDriveItem))]
[JsonSerializable(typeof(GraphDrive))]
public sealed partial class DriveWireContext : JsonSerializerContext;

/// <summary>[S-606] Which icon a drive item gets.</summary>
public enum DriveItemKind
{
    Folder,
    Image,
    Video,
    Audio,
    Spreadsheet,
    Presentation,
    Document,
    File,
}

/// <summary>One file or folder, reduced from Graph.</summary>
/// <param name="WebUrl">The OneDrive page for it. Opened only through the launcher's allow list.</param>
/// <param name="ModifiedBy">[S-1293] Who changed it last, when Graph says.</param>
/// <param name="Location">[S-1293] The folder it sits in, as <see cref="DriveRules.Location"/> words it.</param>
/// <param name="ChildCount">[S-1293] How many items a folder holds, when Graph says.</param>
public sealed record DriveItem(
    string Id,
    string Name,
    long Size,
    DateTimeOffset? Modified,
    string WebUrl,
    bool IsFolder,
    string? MimeType,
    string? ModifiedBy = null,
    string? Location = null,
    long? ChildCount = null);

/// <summary>[S-1294] How full a drive is.</summary>
public readonly record struct DriveQuota(long Used, long Total);

/// <summary>A folder the widget has walked into, for the way back.</summary>
public sealed record DriveFolder(string Id, string Name);

/// <summary>
/// [S-606]..[S-609] The OneDrive widget's rules: the Graph addresses, the mapping, the icon a file
/// gets, the line under its name, and the folder trail. Pure, so each is tested.
/// </summary>
public static class DriveRules
{
    public static readonly Uri GraphRoot = new("https://graph.microsoft.com/v1.0/");

    private const string Select = "$select=id,name,size,lastModifiedDateTime,webUrl,file,folder,remoteItem,lastModifiedBy,parentReference";

    /// <summary>The words for the top of the drive, as OneDrive itself names it.</summary>
    public const string RootName = "My files";

    private static readonly string[] MonthNames = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

    private static readonly HashSet<string> Images = new(StringComparer.OrdinalIgnoreCase) { "png", "jpg", "jpeg", "gif", "svg", "webp", "bmp", "heic", "tif", "tiff" };
    private static readonly HashSet<string> Videos = new(StringComparer.OrdinalIgnoreCase) { "mp4", "mkv", "avi", "mov", "wmv", "webm" };
    private static readonly HashSet<string> Audio = new(StringComparer.OrdinalIgnoreCase) { "mp3", "wav", "flac", "m4a", "aac", "ogg", "wma" };
    private static readonly HashSet<string> Sheets = new(StringComparer.OrdinalIgnoreCase) { "xlsx", "xls", "xlsm", "csv", "ods" };
    private static readonly HashSet<string> Slides = new(StringComparer.OrdinalIgnoreCase) { "pptx", "ppt", "odp" };
    private static readonly HashSet<string> Documents = new(StringComparer.OrdinalIgnoreCase) { "doc", "docx", "pdf", "txt", "md", "rtf", "odt", "one" };

    // ---- addresses -----------------------------------------------------------------------------

    /// <summary>[S-607] The files touched lately.</summary>
    public static Uri Recent(int count) => new(GraphRoot, $"me/drive/recent?$top={Clamp(count)}&{Select}");

    /// <summary>
    /// [S-607] A folder's contents, newest first: the root when <paramref name="folderId"/> is null.
    /// The id is escaped into the path — it is provider data, and one with a <c>/</c> in it must not
    /// become another segment.
    /// </summary>
    public static Uri Children(string? folderId, int count)
    {
        var where = string.IsNullOrEmpty(folderId) ? "me/drive/root" : $"me/drive/items/{Uri.EscapeDataString(folderId)}";
        return new Uri(GraphRoot, $"{where}/children?$top={Clamp(count)}&$orderby=lastModifiedDateTime%20desc&{Select}");
    }

    /// <summary>[S-1294] The drive's quota, for the footer of a big tile.</summary>
    public static Uri QuotaAddress { get; } = new(GraphRoot, "me/drive?$select=quota");

    // ---- reading -------------------------------------------------------------------------------

    /// <summary>A Graph item, or null when it has no id or name.</summary>
    public static DriveItem? FromGraph(GraphDriveItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        var id = item.Id ?? item.RemoteItem?.Id;
        if (string.IsNullOrEmpty(id) || string.IsNullOrWhiteSpace(item.Name))
        {
            return null;
        }

        return new DriveItem(
            id,
            item.Name,
            Math.Max(0, item.Size ?? 0),
            item.LastModified,
            item.WebUrl ?? item.RemoteItem?.WebUrl ?? string.Empty,
            // Only a folder on this drive is walked into. A shared folder from someone else's drive
            // (a recent item's remoteItem) lives at another drive's address, so it opens on the web
            // like a file rather than failing as a listing.
            item.Folder is not null,
            item.File?.MimeType,
            Blank(item.LastModifiedBy?.User?.DisplayName ?? item.RemoteItem?.LastModifiedBy?.User?.DisplayName),
            Location(item.ParentReference?.Path ?? item.RemoteItem?.ParentReference?.Path),
            item.Folder?.ChildCount ?? item.RemoteItem?.Folder?.ChildCount);
    }

    /// <summary>
    /// [S-1293] A parent path in words: <c>/drive/root:/Documents/Reports</c> is
    /// <c>Documents › Reports</c>, the top of the drive is <see cref="RootName"/>, and a path that is
    /// not a drive path (another drive's item, a missing reference) is null rather than a guess.
    /// </summary>
    public static string? Location(string? parentPath)
    {
        if (string.IsNullOrWhiteSpace(parentPath))
        {
            return null;
        }

        var colon = parentPath.IndexOf(':', StringComparison.Ordinal);
        if (colon < 0 || !parentPath.StartsWith("/drive", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var segments = parentPath[(colon + 1)..]
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Uri.UnescapeDataString)
            .ToList();

        return segments.Count == 0 ? RootName : string.Join(" › ", segments);
    }

    /// <summary>[S-1294] A Graph quota, or null when it says too little to show (no total, nonsense numbers).</summary>
    public static DriveQuota? FromGraph(GraphDrive drive)
    {
        ArgumentNullException.ThrowIfNull(drive);
        return drive.Quota is { Used: { } used, Total: { } total } && total > 0 && used >= 0
            ? new DriveQuota(Math.Min(used, total), total)
            : null;
    }

    /// <summary>[S-1294] "12.4 GB of 1 TB used".</summary>
    public static string QuotaText(DriveQuota quota) => $"{Formatting.Bytes(quota.Used)} of {Formatting.Bytes(quota.Total)} used";

    /// <summary>[S-1293] A folder's size column: how many items it holds ("1 item", "12 items"), or nothing.</summary>
    public static string ChildCountText(long? count) => count switch
    {
        null or < 0 => string.Empty,
        1 => "1 item",
        _ => $"{count.Value.ToString(CultureInfo.InvariantCulture)} items",
    };

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>[S-606] The icon: folder, then the MIME type, then the extension, then a plain file.</summary>
    public static DriveItemKind Kind(DriveItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (item.IsFolder)
        {
            return DriveItemKind.Folder;
        }

        var mime = item.MimeType ?? string.Empty;
        var dot = item.Name.LastIndexOf('.');
        var extension = dot >= 0 && dot < item.Name.Length - 1 ? item.Name[(dot + 1)..] : string.Empty;

        if (mime.StartsWith("image/", StringComparison.OrdinalIgnoreCase) || Images.Contains(extension))
        {
            return DriveItemKind.Image;
        }

        if (mime.StartsWith("video/", StringComparison.OrdinalIgnoreCase) || Videos.Contains(extension))
        {
            return DriveItemKind.Video;
        }

        if (mime.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) || Audio.Contains(extension))
        {
            return DriveItemKind.Audio;
        }

        if (Sheets.Contains(extension))
        {
            return DriveItemKind.Spreadsheet;
        }

        if (Slides.Contains(extension))
        {
            return DriveItemKind.Presentation;
        }

        return Documents.Contains(extension) ? DriveItemKind.Document : DriveItemKind.File;
    }

    /// <summary>
    /// [S-606] The line under a name: <c>size · when</c> for a file, just <c>when</c> for a folder
    /// (a folder's size is the sum of things the row is not about).
    /// </summary>
    public static string Meta(DriveItem item, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(item);

        var when = item.Modified is { } modified ? Ago(modified, now) : string.Empty;

        if (item.IsFolder)
        {
            return when;
        }

        var size = Formatting.Bytes(item.Size);
        return when.Length == 0 ? size : $"{size} · {when}";
    }

    /// <summary>[S-606] "Just now", "5m ago", "3h ago", "2d ago", then the date.</summary>
    public static string Ago(DateTimeOffset when, DateTimeOffset now)
    {
        var age = now - when;

        if (age < TimeSpan.FromMinutes(1))
        {
            // A provider clock ahead of this one is not the future.
            return "Just now";
        }

        if (age < TimeSpan.FromHours(1))
        {
            return $"{(int)age.TotalMinutes}m ago";
        }

        if (age < TimeSpan.FromDays(1))
        {
            return $"{(int)age.TotalHours}h ago";
        }

        if (age < TimeSpan.FromDays(7))
        {
            return $"{(int)age.TotalDays}d ago";
        }

        var local = when.ToLocalTime();
        return local.Year == now.ToLocalTime().Year
            ? $"{local.Day} {MonthNames[local.Month - 1]}"
            : $"{local.Day} {MonthNames[local.Month - 1]} {local.Year.ToString(CultureInfo.InvariantCulture)}";
    }

    // ---- the folder trail ------------------------------------------------------------------------

    /// <summary>[S-609] Walk into a folder.</summary>
    public static IReadOnlyList<DriveFolder> Enter(IReadOnlyList<DriveFolder> trail, DriveItem folder)
    {
        ArgumentNullException.ThrowIfNull(trail);
        ArgumentNullException.ThrowIfNull(folder);
        return folder.IsFolder ? [.. trail, new DriveFolder(folder.Id, folder.Name)] : trail;
    }

    /// <summary>
    /// [S-609] One step back. Popping the last folder returns to the configured start — recent files
    /// or the root — not to an implied root the user never saw.
    /// </summary>
    public static IReadOnlyList<DriveFolder> Back(IReadOnlyList<DriveFolder> trail)
    {
        ArgumentNullException.ThrowIfNull(trail);
        return trail.Count == 0 ? trail : [.. trail.Take(trail.Count - 1)];
    }

    /// <summary>[S-607] What a trail shows: the innermost folder, else the start the config names.</summary>
    public static Uri Address(IReadOnlyList<DriveFolder> trail, DriveStart start, int count)
    {
        ArgumentNullException.ThrowIfNull(trail);

        if (trail.Count > 0)
        {
            return Children(trail[^1].Id, count);
        }

        return start == DriveStart.Root ? Children(null, count) : Recent(count);
    }

    private static int Clamp(int count) => Math.Clamp(count, OneDriveConfig.MinFiles, OneDriveConfig.MaxFiles);
}
