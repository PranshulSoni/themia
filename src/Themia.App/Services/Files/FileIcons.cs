using System.Runtime.InteropServices;
using Microsoft.UI.Xaml.Media;
using Themia.Core.Diagnostics;
using Themia.Core.Widgets;

namespace Themia.App.Services.Files;

/// <summary>
/// [S-1324] What a folder row or tile draws for an item: the shell's real icon, a picture's
/// thumbnail, the shell's type name ("Text Document"), and a folder's item count — each extracted off
/// the UI thread and cached so every folder widget shares one extraction.
/// <para>
/// Bounded: icons are keyed per type where they look alike (<see cref="FolderFormatting.IconKey"/>), so
/// a folder of a thousand text files costs one icon; per-item icons and thumbnails live in caches that
/// are dropped wholesale when they pass their cap, which a widget browsing folder after folder reaches
/// and a desktop that sits still never does. Extraction itself is serialised by
/// <see cref="AppIcons"/> (the shell's image list races on concurrent first requests, the original's
/// [S-64]). UI thread.
/// </para>
/// </summary>
internal static partial class FileIcons
{
    private const string Scope = "file-icons";
    private const int PerItemCap = 512;
    private const int ThumbnailCap = 256;

    /// <summary>Counting stops here; a folder with more shows "999+".</summary>
    public const int CountCap = 1000;

    private static readonly Dictionary<string, ImageSource?> Shared = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, ImageSource?> PerItem = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, ImageSource?> Thumbnails = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, Task<ImageSource?>> Pending = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string> TypeNames = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, int?> Counts = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The cached icon, if there is one already (no extraction): lets a redraw paint at once.</summary>
    public static bool TryIcon(FolderEntry entry, int pixels, out ImageSource? image)
    {
        var key = IconKey(entry, pixels, out var shared);
        return (shared ? Shared : PerItem).TryGetValue(key, out image);
    }

    /// <summary>The shell icon for an entry at <paramref name="pixels"/> (physical) pixels, or null.</summary>
    public static Task<ImageSource?> IconAsync(FolderEntry entry, int pixels)
    {
        var key = IconKey(entry, pixels, out var shared);
        var cache = shared ? Shared : PerItem;

        if (cache.TryGetValue(key, out var known))
        {
            return Task.FromResult(known);
        }

        return Fetch(key, cache, PerItemCap, () => AppIcons.LoadAsync(entry.FullPath, pixels, thumbnail: false));
    }

    /// <summary>
    /// A thumbnail for an image file, or null for one that has none, is not an image, or is a cloud
    /// placeholder (reading its pixels would download it).
    /// </summary>
    public static Task<ImageSource?> ThumbnailAsync(FolderEntry entry, int pixels)
    {
        if (entry.IsDirectory || !ImageFiles.IsImage(entry.Name) || CloudBadges.WouldDownload(entry.Attributes))
        {
            return Task.FromResult<ImageSource?>(null);
        }

        // The modified time is part of the key: an edited photo gets a new thumbnail.
        var key = $"{pixels}|{entry.Modified.UtcTicks}|{entry.FullPath}";

        if (Thumbnails.TryGetValue(key, out var known))
        {
            return Task.FromResult(known);
        }

        return Fetch(key, Thumbnails, ThumbnailCap, () => AppIcons.LoadAsync(entry.FullPath, pixels, thumbnail: true));
    }

    private static async Task<ImageSource?> Fetch(string key, Dictionary<string, ImageSource?> cache, int cap, Func<Task<ImageSource?>> load)
    {
        if (!Pending.TryGetValue(key, out var task))
        {
            task = load();
            Pending[key] = task;
        }

        ImageSource? image;

        try
        {
            image = await task.ConfigureAwait(true);
        }
        finally
        {
            Pending.Remove(key);
        }

        if (!ReferenceEquals(cache, Shared) && cache.Count >= cap)
        {
            cache.Clear();
        }

        cache[key] = image;
        return image;
    }

    private static string IconKey(FolderEntry entry, int pixels, out bool shared)
    {
        var key = FolderFormatting.IconKey(entry);
        shared = !string.Equals(key, entry.FullPath, StringComparison.OrdinalIgnoreCase);
        return string.Concat(pixels.ToString(System.Globalization.CultureInfo.InvariantCulture), "|", key);
    }

    /// <summary>[S-1322] The shell's name for the item's type ("Text Document"), or Explorer's fallback.</summary>
    public static string TypeName(FolderEntry entry)
    {
        if (entry.IsDirectory)
        {
            return FolderFormatting.TypeFallback(entry.Name, true);
        }

        var dot = entry.Name.LastIndexOf('.');
        var extension = dot > 0 ? entry.Name[dot..] : string.Empty;

        if (extension.Length == 0)
        {
            return FolderFormatting.TypeFallback(entry.Name, false);
        }

        if (TypeNames.TryGetValue(extension, out var known))
        {
            return known;
        }

        var name = ShellTypeName(extension) ?? FolderFormatting.TypeFallback(entry.Name, false);
        TypeNames[extension] = name;
        return name;
    }

    private static unsafe string? ShellTypeName(string extension)
    {
        const uint useFileAttributes = 0x10;
        const uint typeName = 0x400;
        const uint normal = 0x80;

        var info = default(FileInfoW);

        if (SHGetFileInfoW(extension, normal, &info, (uint)sizeof(FileInfoW), typeName | useFileAttributes) == 0)
        {
            return null;
        }

        var text = new string(info.TypeName);
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    /// <summary>
    /// [S-1322] How many entries a folder holds, counted on the thread pool up to <see cref="CountCap"/>.
    /// Null for one that cannot be read. Cached per folder and its modified time.
    /// </summary>
    public static async Task<int?> CountAsync(FolderEntry folder)
    {
        var key = $"{folder.Modified.UtcTicks}|{folder.FullPath}";

        if (Counts.TryGetValue(key, out var known))
        {
            return known;
        }

        var count = await Task.Run(() => Count(folder.FullPath)).ConfigureAwait(true);

        if (Counts.Count >= PerItemCap)
        {
            Counts.Clear();
        }

        Counts[key] = count;
        return count;
    }

    private static int? Count(string path)
    {
        try
        {
            var options = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = 0, RecurseSubdirectories = false };
            return Directory.EnumerateFileSystemEntries(path, "*", options).Take(CountCap).Count();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Debug(Scope, $"could not count '{path}': {ex.Message}");
            return null;
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private unsafe struct FileInfoW
    {
        public nint Icon;
        public int IconIndex;
        public uint Attributes;
        public fixed char DisplayName[260];
        public fixed char TypeName[80];
    }

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static unsafe partial nuint SHGetFileInfoW(string path, uint attributes, FileInfoW* info, uint size, uint flags);
}
