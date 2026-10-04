using Microsoft.UI.Xaml.Media.Imaging;
using Themia.Core.Diagnostics;
using Windows.Storage;

namespace Themia.App.Services;

/// <summary>
/// Turning a path into pixels, at the size it will actually be drawn, without stopping the desktop.
/// <para>
/// Decoding a photograph is the most expensive thing any widget in this app does, and it is
/// expensive twice: the file has to come off the disk, and the JPEG has to be turned into a bitmap.
/// Doing either on the UI thread freezes every widget on every monitor, and a folder of phone
/// photographs will do it for several seconds at a time.
/// </para>
/// <para>
/// Three rules, and they are the whole of this class:
/// </para>
/// <list type="number">
/// <item>
/// <b>Decode to the size shown.</b> <see cref="BitmapImage.DecodePixelWidth"/> makes WIC scale
/// during decode, so a 24-megapixel photograph in a 300px tile costs a 300px bitmap — under a
/// megabyte — instead of ninety. Without it the widget holds the full-resolution surface for every
/// tile and the GPU downsamples it on every frame.
/// </item>
/// <item>
/// <b>Bound what is held.</b> The cache keeps <see cref="Capacity"/> decoded bitmaps and drops the
/// oldest, so a slideshow left running for a week over a folder of ten thousand pictures holds the
/// same amount of memory on the last day as on the first.
/// </item>
/// <item>
/// <b>Bound what is in flight.</b> Two decodes at once. A grid of twenty-four tiles asking at once
/// would open twenty-four file handles and hand WIC twenty-four jobs, which on a spinning disk or a
/// share is slower than doing them in turn as well as far heavier.
/// </item>
/// </list>
/// <para>
/// Everything here runs on the UI thread and is <i>asynchronous</i> rather than backgrounded:
/// <see cref="BitmapImage"/> is a dependency object and cannot be created anywhere else, and both
/// the file open and the decode happen off-thread inside the WinRT calls it awaits.
/// </para>
/// </summary>
internal sealed class ImageLoader
{
    private const string Scope = "gallery-image";

    /// <summary>
    /// How many decoded bitmaps are kept. Twenty-four at tile size is a few megabytes, and it is the
    /// most tiles the gallery will ever show at once, so a full grid never evicts its own contents.
    /// </summary>
    private const int Capacity = 24;

    /// <summary>
    /// Decode widths are rounded to this, so a widget being dragged does not decode the same
    /// picture again at 301px, 303px, 308px and fill the cache with near-duplicates.
    /// </summary>
    private const int WidthStep = 64;

    /// <summary>The largest bitmap worth decoding. Past this the tile is a wall, not a widget.</summary>
    private const int MaxWidth = 2048;

    /// <summary>At most two decodes at a time; the rest wait here rather than at the filesystem.</summary>
    private readonly SemaphoreSlim _gate = new(2, 2);

    private readonly Dictionary<string, BitmapImage> _cache = [];

    /// <summary>Insertion order, which is what makes eviction the oldest rather than an arbitrary one.</summary>
    private readonly Queue<string> _order = new();

    /// <summary>
    /// The bitmap for a path at roughly <paramref name="pixelWidth"/>, or null when it could not be
    /// read or decoded. Never throws: a corrupt file in a folder of good ones must cost that one
    /// tile, not the widget.
    /// </summary>
    public async Task<BitmapImage?> LoadAsync(string path, double pixelWidth, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var width = Bucket(pixelWidth);
        var key = $"{width}|{path}";

        if (_cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        // [S-762] The photo of the day is an https address the scan has already pinned to Bing's
        // host. It is handed to the bitmap as a URI rather than downloaded here: the platform
        // fetches it off-thread, decodes it at the asked width, and keeps it in the system's HTTP
        // cache, so a restart while offline still has today's picture. Nothing else is a URI.
        if (Uri.TryCreate(path, UriKind.Absolute, out var remote) && remote.Scheme == Uri.UriSchemeHttps)
        {
            var image = Remote(remote, width, key);
            Remember(key, image);
            return image;
        }

        await _gate.WaitAsync(token).ConfigureAwait(true);

        try
        {
            // Another tile may have asked for the same picture while this one queued.
            if (_cache.TryGetValue(key, out cached))
            {
                return cached;
            }

            token.ThrowIfCancellationRequested();

            var bitmap = await DecodeAsync(path, width).ConfigureAwait(true);

            if (bitmap is null)
            {
                return null;
            }

            // Checked after the await as well as before: the folder may have changed while this was
            // decoding, and caching a picture nobody is going to draw only evicts one that is.
            token.ThrowIfCancellationRequested();

            Remember(key, bitmap);
            return bitmap;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Open the file and decode it, both through WinRT so both happen off this thread. Returns null
    /// and logs for anything that goes wrong with one file.
    /// </summary>
    private static async Task<BitmapImage?> DecodeAsync(string path, int width)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            using var stream = await file.OpenReadAsync();

            var bitmap = new BitmapImage
            {
                // Physical, not logical: the caller has already multiplied by the display's scale,
                // so asking for logical pixels here would scale it a second time and decode a
                // picture too small for a high-DPI monitor.
                DecodePixelType = DecodePixelType.Physical,
                DecodePixelWidth = width,
            };

            // The decode itself. Awaited rather than fired off, so the stream below is disposed
            // only once WIC has finished reading it.
            await bitmap.SetSourceAsync(stream);
            return bitmap;
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException
            or System.Runtime.InteropServices.COMException)
        {
            // A file that went away, one this machine has no codec for, or one that is simply not
            // the picture its extension claims. One tile's problem, said out loud, and the rest of
            // the folder is unaffected.
            Log.Warn(Scope, $"could not decode '{path}'", ex);
            return null;
        }
    }

    private BitmapImage Remote(Uri uri, int width, string key)
    {
        var bitmap = new BitmapImage
        {
            DecodePixelType = DecodePixelType.Physical,
            DecodePixelWidth = width,
        };

        // Logged, not thrown, and forgotten: the frame stays empty and the next refresh asks again
        // rather than finding the failed bitmap in the cache.
        bitmap.ImageFailed += (_, args) =>
        {
            Log.Warn(Scope, $"could not load the photo of the day: {args.ErrorMessage}");
            _cache.Remove(key);
        };
        bitmap.UriSource = uri;
        return bitmap;
    }

    private void Remember(string key, BitmapImage bitmap)
    {
        _cache[key] = bitmap;
        _order.Enqueue(key);

        while (_order.Count > Capacity)
        {
            var oldest = _order.Dequeue();

            // Only if it is still the entry that was queued: a re-decode at the same key enqueues a
            // second time, and dropping the live bitmap because its stale queue entry came up would
            // evict something the grid is drawing right now.
            if (!_order.Contains(oldest))
            {
                _cache.Remove(oldest);
            }
        }
    }

    private static int Bucket(double pixelWidth)
    {
        if (!double.IsFinite(pixelWidth) || pixelWidth <= 0)
        {
            return WidthStep;
        }

        var stepped = (int)(Math.Ceiling(pixelWidth / WidthStep) * WidthStep);
        return Math.Clamp(stepped, WidthStep, MaxWidth);
    }

    /// <summary>
    /// Drop everything held. Called when the folder changes and on detach, so a widget that has been
    /// pointed somewhere else is not still holding the last folder's pictures.
    /// </summary>
    public void Clear()
    {
        _cache.Clear();
        _order.Clear();
    }
}
