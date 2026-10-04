using Themia.Core.Diagnostics;
using Themia.Core.Storage;

namespace Themia.App.Services.Web;

/// <summary>
/// [S-1319] The web element widget's last good renders across restarts: the app side of
/// <see cref="ElementRenderStore"/>.
/// <para>
/// The index lives in memory once read; pictures are read from disk only when a widget asks for
/// one, off the UI thread. Writes go through one chain, in order, off the UI thread, so there is a
/// single writer. Until <see cref="Persist"/> is called nothing is read or written at all — the
/// gallery never calls it, so a gallery run neither reads nor writes the user's renders.
/// </para>
/// <para>UI thread only, apart from the queued disk work.</para>
/// </summary>
internal static class ElementRenderCache
{
    private const string Scope = "web-element";

    private static readonly Dictionary<string, ElementRender> Entries = new(StringComparer.Ordinal);
    private static readonly Lock WriteGate = new();
    private static string? _folder;
    private static Task _writes = Task.CompletedTask;

    /// <summary>Read the saved index from <paramref name="folder"/> now and keep renders there from now on. Called once, by the app.</summary>
    public static void Persist(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);

        var (entries, outcome) = ElementRenderStore.Load(folder, DateTimeOffset.Now);

        foreach (var entry in entries)
        {
            Entries[entry.File] = entry;
        }

        if (outcome == ElementRenderLoadOutcome.Loaded)
        {
            Log.Info(Scope, $"restored {entries.Count} saved renders");
        }

        _folder = folder;
    }

    /// <summary>The saved render of this element, if there is one.</summary>
    public static ElementRender? Peek(string url, string selector)
        => _folder is not null && Entries.TryGetValue(ElementRenderStore.FileFor(url, selector), out var entry) ? entry : null;

    /// <summary>The saved render's picture, read off the UI thread. Null when it is gone or damaged (logged by the store).</summary>
    public static Task<byte[]?> ReadAsync(ElementRender entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return _folder is { } folder
            ? Task.Run(() => ElementRenderStore.ReadPicture(folder, entry))
            : Task.FromResult<byte[]?>(null);
    }

    /// <summary>Keep a good render, replacing the element's previous one, and prune the rest to the caps.</summary>
    public static void Remember(string url, string selector, byte[] png, double cssWidth, double cssHeight, double zoom, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(png);

        if (_folder is not { } folder)
        {
            return;
        }

        if (!ElementRenderStore.IsStorable(png))
        {
            Log.Debug(Scope, $"not caching a {png.Length}-byte render: not a storable picture");
            return;
        }

        var entry = new ElementRender(url, selector, at, cssWidth, cssHeight, zoom, ElementRenderStore.FileFor(url, selector), png.Length);
        Entries[entry.File] = entry;
        Queue(folder, entry, png);
    }

    /// <summary>[S-477] The element is gone from its page: its old picture is no longer what it shows.</summary>
    public static void Forget(string url, string selector)
    {
        if (_folder is not { } folder || !Entries.Remove(ElementRenderStore.FileFor(url, selector), out var gone))
        {
            return;
        }

        Queue(folder, null, null, gone);
    }

    private static void Queue(string folder, ElementRender? written, byte[]? png, ElementRender? forgotten = null)
    {
        var (kept, dropped) = ElementRenderStore.Prune([.. Entries.Values], DateTimeOffset.Now);

        foreach (var entry in dropped)
        {
            Entries.Remove(entry.File);
        }

        IReadOnlyList<ElementRender> removed = forgotten is null ? dropped : [.. dropped, forgotten];

        lock (WriteGate)
        {
            _writes = _writes.ContinueWith(
                _ =>
                {
                    try
                    {
                        ElementRenderStore.Save(folder, written, png, kept, removed);
                    }
                    catch (Exception ex)
                    {
                        // Save handles the I/O failures it expects; anything here is a bug, logged
                        // rather than left to fault a task nobody observes.
                        Log.Error(Scope, "saving a render threw", ex);
                    }
                },
                CancellationToken.None,
                TaskContinuationOptions.None,
                TaskScheduler.Default);
        }
    }
}
