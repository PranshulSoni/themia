using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Themia.Core.Diagnostics;
using Themia.Core.Widgets;

namespace Themia.Core.Storage;

/// <summary>
/// One remembered render of a pinned element: which page and selector it shows, when it was taken,
/// the element's CSS size, the zoom it was captured at, and the picture file that holds it.
/// </summary>
public sealed record ElementRender(
    string Url,
    string Selector,
    DateTimeOffset At,
    double CssWidth,
    double CssHeight,
    double Zoom,
    string File,
    long Bytes);

/// <summary>How the render cache's index came off disk.</summary>
public enum ElementRenderLoadOutcome
{
    /// <summary>No index yet.</summary>
    Fresh = 0,

    Loaded = 1,

    /// <summary>The index could not be used; it was set aside (when corrupt) and the cache starts empty.</summary>
    Recovered = 2,
}

/// <summary>
/// [S-1319] The web element widget's last good renders, kept across restarts in an
/// <c>element_renders</c> folder beside the app state: one PNG per page and selector and an
/// <c>index.json</c> naming them. A cold launch draws the last picture at once, marked with its
/// age, instead of "Rendering the page…" and a browser start just to show something.
/// <para>
/// Its own folder, never the state file: it is a picture of somebody else's page and must never
/// ride along in an export. The index is schema-versioned and validated on the way in — a file
/// that is not this schema is set aside as <c>.corrupt</c>, an entry that is not a usable render is
/// dropped — and a picture's file name is derived from its page and selector, never read from the
/// index as a path, so a hand-edited index cannot point anywhere outside the folder. Every write is
/// a temp file and a rename. Bounded: at most <see cref="MaxEntries"/> pictures and
/// <see cref="MaxTotalBytes"/> in all, newest kept, and nothing older than <see cref="MaxAge"/>.
/// </para>
/// </summary>
public static class ElementRenderStore
{
    public const int SchemaVersion = 1;

    public const string FolderName = "element_renders";

    public const string IndexName = "index.json";

    public const int MaxEntries = 48;

    public const long MaxTotalBytes = 32L * 1024 * 1024;

    /// <summary>One picture larger than this is not cached (an 8192-pixel capture of a busy page can be).</summary>
    public const long MaxFileBytes = 8L * 1024 * 1024;

    /// <summary>The oldest render worth drawing at launch, even marked stale.</summary>
    public static TimeSpan MaxAge { get; } = TimeSpan.FromDays(30);

    private const string Scope = "web-element";

    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static long _tempSequence;

    /// <summary>The picture file for a page and selector: a hash, so it is a fixed shape and never a path.</summary>
    public static string FileFor(string url, string selector)
    {
        ArgumentNullException.ThrowIfNull(url);
        ArgumentNullException.ThrowIfNull(selector);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(url + "\n" + selector));
        return Convert.ToHexStringLower(hash.AsSpan(0, 16)) + ".png";
    }

    /// <summary>
    /// Read the index. Never throws for bad data: absent is a quiet empty cache; unreadable (locked)
    /// is an empty cache and a warning, the file left alone; corrupt is set aside and reported.
    /// Entries whose picture is missing are dropped.
    /// </summary>
    public static (IReadOnlyList<ElementRender> Entries, ElementRenderLoadOutcome Outcome) Load(string folder, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        var path = Path.Combine(folder, IndexName);

        string json;
        try
        {
            if (!File.Exists(path))
            {
                return ([], ElementRenderLoadOutcome.Fresh);
            }

            json = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn(Scope, $"could not read {path}; starting without saved renders", ex);
            return ([], ElementRenderLoadOutcome.Recovered);
        }

        if (Parse(json, now) is not { } entries)
        {
            Log.Warn(Scope, $"{path} is corrupt or from another version; starting without saved renders");

            try
            {
                File.Move(path, path + ".corrupt", overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Warn(Scope, "could not set the unreadable render index aside", ex);
            }

            return ([], ElementRenderLoadOutcome.Recovered);
        }

        var present = entries.Where(entry => File.Exists(Path.Combine(folder, entry.File))).ToList();
        return (present, ElementRenderLoadOutcome.Loaded);
    }

    /// <summary>
    /// Parse an index, or null when the envelope is not one this build can trust (not JSON, another
    /// schema version, the list missing). Entries that are not usable renders are dropped; of two
    /// for the same page and selector, the newer is kept.
    /// </summary>
    public static IReadOnlyList<ElementRender>? Parse(string json, DateTimeOffset now)
    {
        ElementRenderIndex? file;
        try
        {
            file = JsonSerializer.Deserialize(json, ElementRenderJsonContext.Default.ElementRenderIndex);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }

        if (file is null || file.SchemaVersion != SchemaVersion || file.Renders is null)
        {
            return null;
        }

        var kept = new Dictionary<string, ElementRender>(StringComparer.Ordinal);

        foreach (var entry in file.Renders)
        {
            if (Usable(entry, now) is { } render
                && (!kept.TryGetValue(render.File, out var other) || other.At < render.At))
            {
                kept[render.File] = render;
            }
        }

        return [.. kept.Values.OrderByDescending(render => render.At)];
    }

    public static string Serialize(IEnumerable<ElementRender> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var file = new ElementRenderIndex
        {
            SchemaVersion = SchemaVersion,
            Renders = [.. entries.Select(entry => new ElementRenderEntry
            {
                Url = entry.Url,
                Selector = entry.Selector,
                At = entry.At,
                CssWidth = entry.CssWidth,
                CssHeight = entry.CssHeight,
                Zoom = entry.Zoom,
                Bytes = entry.Bytes,
            })],
        };

        return JsonSerializer.Serialize(file, ElementRenderJsonContext.Default.ElementRenderIndex);
    }

    /// <summary>
    /// [S-1319] What to keep: newest first, at most <see cref="MaxEntries"/> and
    /// <see cref="MaxTotalBytes"/>, nothing past <see cref="MaxAge"/>. The rest is dropped, files and all.
    /// </summary>
    public static (IReadOnlyList<ElementRender> Kept, IReadOnlyList<ElementRender> Dropped) Prune(
        IEnumerable<ElementRender> entries, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var kept = new List<ElementRender>();
        var dropped = new List<ElementRender>();
        long total = 0;

        foreach (var entry in entries.OrderByDescending(entry => entry.At))
        {
            if (kept.Count < MaxEntries && total + entry.Bytes <= MaxTotalBytes && now - entry.At <= MaxAge)
            {
                kept.Add(entry);
                total += entry.Bytes;
            }
            else
            {
                dropped.Add(entry);
            }
        }

        return (kept, dropped);
    }

    /// <summary>Whether <paramref name="bytes"/> is a picture this cache will hold: a PNG, not empty, not oversized.</summary>
    public static bool IsStorable(ReadOnlySpan<byte> bytes)
        => bytes.Length > PngSignature.Length && bytes.Length <= MaxFileBytes && bytes.StartsWith(PngSignature);

    /// <summary>
    /// Read one render's picture. Null — logged — when it is missing, unreadable, or not the PNG of
    /// the size the index recorded: a torn or swapped file is never drawn.
    /// </summary>
    public static byte[]? ReadPicture(string folder, ElementRender entry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        ArgumentNullException.ThrowIfNull(entry);

        try
        {
            var bytes = File.ReadAllBytes(Path.Combine(folder, entry.File));

            if (bytes.Length != entry.Bytes || !IsStorable(bytes))
            {
                Log.Warn(Scope, $"saved render {entry.File} is not the picture the index recorded; ignoring it");
                return null;
            }

            return bytes;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn(Scope, $"could not read saved render {entry.File}", ex);
            return null;
        }
    }

    /// <summary>
    /// Write one picture, then the index, each atomically (temp file, then rename), then delete the
    /// pictures of <paramref name="dropped"/>. The caller is the single writer.
    /// </summary>
    /// <returns>False when a write failed; logged, and what was on disk is still consistent.</returns>
    public static bool Save(string folder, ElementRender? written, byte[]? picture, IReadOnlyList<ElementRender> index, IReadOnlyList<ElementRender> dropped)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(dropped);

        try
        {
            Directory.CreateDirectory(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error(Scope, $"could not create {folder}", ex);
            return false;
        }

        // The picture before the index: an index never names a file that is not there yet.
        if (written is not null && picture is not null && !WriteAtomically(Path.Combine(folder, written.File), picture))
        {
            return false;
        }

        if (!WriteAtomically(Path.Combine(folder, IndexName), Encoding.UTF8.GetBytes(Serialize(index))))
        {
            return false;
        }

        foreach (var entry in dropped)
        {
            // A dropped entry may share its file with a kept one only if they are the same render.
            if (index.Any(kept => kept.File == entry.File))
            {
                continue;
            }

            try
            {
                File.Delete(Path.Combine(folder, entry.File));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Warn(Scope, $"could not remove the old render {entry.File}", ex);
            }
        }

        return true;
    }

    private static bool WriteAtomically(string path, byte[] bytes)
    {
        var temp = $"{path}.tmp.{Environment.ProcessId}.{Interlocked.Increment(ref _tempSequence)}";

        try
        {
            File.WriteAllBytes(temp, bytes);
            File.Move(temp, path, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error(Scope, $"could not save {path}", ex);

            try
            {
                File.Delete(temp);
            }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
            {
                Log.Warn(Scope, "could not remove a temp render file", cleanup);
            }

            return false;
        }
    }

    /// <summary>The entry as a render, or null when any part of it is not one this widget would have stored.</summary>
    private static ElementRender? Usable(ElementRenderEntry? entry, DateTimeOffset now)
    {
        if (entry is null
            || entry.Url is not { } url || !WebInput.Url(url).IsValid || WebInput.Url(url).Url != url
            || entry.Selector is not { } selector || !WebElementRules.IsPlausibleSelector(selector) || selector.Trim() != selector
            || entry.At is not { } at || at > now.AddMinutes(5) || now - at > MaxAge
            || !InRange(entry.CssWidth, WebElementRules.MaxCaptureWidth) || !InRange(entry.CssHeight, WebElementRules.MaxCaptureHeight)
            || !InRange(entry.Zoom, ElementFitRules.MaxScale)
            || entry.Bytes is not (> 0 and <= MaxFileBytes))
        {
            return null;
        }

        return new ElementRender(url, selector, at, entry.CssWidth!.Value, entry.CssHeight!.Value, entry.Zoom!.Value, FileFor(url, selector), entry.Bytes.Value);
    }

    private static bool InRange(double? value, double max)
        => value is { } number && double.IsFinite(number) && number > 0 && number <= max;
}

// ---- on-disk shape ----------------------------------------------------------------------------
// Every member nullable, so a field missing on disk is told apart from a zero. The file name is not
// stored: it is derived from the page and selector on the way in.

internal sealed class ElementRenderIndex
{
    public int SchemaVersion { get; set; }

    public List<ElementRenderEntry?>? Renders { get; set; }
}

internal sealed class ElementRenderEntry
{
    public string? Url { get; set; }

    public string? Selector { get; set; }

    public DateTimeOffset? At { get; set; }

    public double? CssWidth { get; set; }

    public double? CssHeight { get; set; }

    public double? Zoom { get; set; }

    public long? Bytes { get; set; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ElementRenderIndex))]
internal sealed partial class ElementRenderJsonContext : JsonSerializerContext;
