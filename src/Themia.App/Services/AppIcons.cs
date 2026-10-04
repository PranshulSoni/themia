using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Themia.Core.Diagnostics;

namespace Themia.App.Services;

/// <summary>
/// An executable's own icon, as something XAML can draw.
/// <para>
/// The extraction — shell icon lookup, then the icon's pixels copied out through GDI — runs on the
/// thread pool, because the first look at a binary is disk I/O. Only the last step, wrapping the
/// pixels in a bitmap, happens on the UI thread, where XAML objects must be made. Each path is
/// extracted once per run and the bitmap shared by every widget that shows it; a path with no
/// usable icon is remembered as such, so it is not asked again on every refresh.
/// </para>
/// <para>
/// Every handle the extraction takes — the icon, its two bitmaps, the memory DC — is released on
/// every path out, by the <c>finally</c> that owns it.
/// </para>
/// </summary>
internal static partial class AppIcons
{
    private const string Scope = "app-icons";

    /// <summary>Pixels per side: crisp at 200% for an icon drawn at 20 DIPs, and still small in memory.</summary>
    private const int Size = 48;

    private static readonly Dictionary<string, ImageSource?> Ready = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, Task<Pixels?>> Pending = new(StringComparer.OrdinalIgnoreCase);

    private sealed record Pixels(int Width, int Height, byte[] Bgra);

    /// <summary>
    /// The icon for <paramref name="path"/>, or null when it has none. UI thread only: it builds the
    /// bitmap there, and the two caches are only ever touched from it.
    /// </summary>
    public static async Task<ImageSource?> GetAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        if (Ready.TryGetValue(path, out var known))
        {
            return known;
        }

        if (!Pending.TryGetValue(path, out var task))
        {
            task = Task.Run(() => Extract(path));
            Pending[path] = task;
        }

        Pixels? pixels;
        try
        {
            pixels = await task.ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Log.Warn(Scope, $"could not extract the icon of {path}", ex);
            pixels = null;
        }

        Pending.Remove(path);

        if (Ready.TryGetValue(path, out known))
        {
            return known;
        }

        var image = pixels is null ? null : ToBitmap(pixels);
        Ready[path] = image;
        return image;
    }

    private static WriteableBitmap ToBitmap(Pixels pixels)
    {
        var bitmap = new WriteableBitmap(pixels.Width, pixels.Height);
        pixels.Bgra.CopyTo(bitmap.PixelBuffer);
        bitmap.Invalidate();
        return bitmap;
    }

    private static unsafe Pixels? Extract(string path)
    {
        nint icon = 0;

        try
        {
            if (SHDefExtractIcon(path, 0, 0, &icon, null, Size) != 0 || icon == 0)
            {
                return null;
            }

            return Read(icon);
        }
        finally
        {
            if (icon != 0)
            {
                DestroyIcon(icon);
            }
        }
    }

    /// <summary>An icon's pixels as premultiplied BGRA, or null for a monochrome or unreadable icon.</summary>
    private static unsafe Pixels? Read(nint icon)
    {
        if (!GetIconInfo(icon, out var info))
        {
            return null;
        }

        nint memory = 0;

        try
        {
            if (info.Color == 0)
            {
                return null;
            }

            BitmapHeader bm;
            if (GetObject(info.Color, sizeof(BitmapHeader), &bm) == 0 || bm.Width <= 0 || bm.Height <= 0 || bm.Width > 256 || bm.Height > 256)
            {
                return null;
            }

            memory = CreateCompatibleDC(0);
            if (memory == 0)
            {
                return null;
            }

            var width = bm.Width;
            var height = bm.Height;
            var color = new byte[width * height * 4];

            if (!Bits(memory, info.Color, width, height, color))
            {
                return null;
            }

            var hasAlpha = false;
            for (var i = 3; i < color.Length; i += 4)
            {
                if (color[i] != 0)
                {
                    hasAlpha = true;
                    break;
                }
            }

            // An old-style icon carries its transparency in the mask instead of an alpha channel.
            if (!hasAlpha)
            {
                var mask = new byte[width * height * 4];
                var haveMask = info.Mask != 0 && Bits(memory, info.Mask, width, height, mask);

                for (var i = 0; i < color.Length; i += 4)
                {
                    color[i + 3] = haveMask && mask[i] != 0 ? (byte)0 : (byte)255;
                }
            }

            // XAML wants premultiplied alpha; GDI hands over straight alpha.
            for (var i = 0; i < color.Length; i += 4)
            {
                var alpha = color[i + 3];
                if (alpha == 255)
                {
                    continue;
                }

                color[i] = (byte)(color[i] * alpha / 255);
                color[i + 1] = (byte)(color[i + 1] * alpha / 255);
                color[i + 2] = (byte)(color[i + 2] * alpha / 255);
            }

            return new Pixels(width, height, color);
        }
        finally
        {
            if (memory != 0)
            {
                DeleteDC(memory);
            }

            if (info.Color != 0)
            {
                DeleteObject(info.Color);
            }

            if (info.Mask != 0)
            {
                DeleteObject(info.Mask);
            }
        }
    }

    private static unsafe bool Bits(nint dc, nint bitmap, int width, int height, byte[] target)
    {
        var header = new BitmapInfoHeader
        {
            Size = (uint)sizeof(BitmapInfoHeader),
            Width = width,

            // Negative: top-down rows, the order the bitmap wants them in.
            Height = -height,
            Planes = 1,
            BitCount = 32,
        };

        fixed (byte* bits = target)
        {
            return GetDIBits(dc, bitmap, 0, (uint)height, bits, &header, 0) == height;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IconInfo
    {
        public int IsIcon;
        public uint HotspotX;
        public uint HotspotY;
        public nint Mask;
        public nint Color;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapHeader
    {
        public int Type;
        public int Width;
        public int Height;
        public int WidthBytes;
        public ushort Planes;
        public ushort BitsPixel;
        public nint Bits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;

        // GetDIBits writes a colour table after the header for some formats; for 32-bit BI_RGB it
        // writes none, but the documented buffer is a BITMAPINFO, so room for one entry is kept.
        public uint Palette0;
    }

    [LibraryImport("shell32.dll", EntryPoint = "SHDefExtractIconW", StringMarshalling = StringMarshalling.Utf16)]
    private static unsafe partial int SHDefExtractIcon(string iconFile, int index, uint flags, nint* large, nint* small, uint size);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyIcon(nint icon);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetIconInfo(nint icon, out IconInfo info);

    [LibraryImport("gdi32.dll", EntryPoint = "GetObjectW")]
    private static unsafe partial int GetObject(nint handle, int size, void* buffer);

    [LibraryImport("gdi32.dll")]
    private static partial nint CreateCompatibleDC(nint dc);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteDC(nint dc);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(nint handle);

    [LibraryImport("gdi32.dll")]
    private static unsafe partial int GetDIBits(nint dc, nint bitmap, uint start, uint lines, void* bits, BitmapInfoHeader* info, uint usage);
}
