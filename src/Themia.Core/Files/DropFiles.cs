using System.Buffers.Binary;
using System.Text;

namespace Themia.Core.Files;

/// <summary>
/// [S-1329] The two Windows clipboard payloads a file copy is made of, as bytes: <c>CF_HDROP</c> (a
/// <c>DROPFILES</c> header and a double-NUL-terminated list of UTF-16 paths) and the
/// <c>Preferred DropEffect</c> DWORD that says copy or cut. Pure, so the encoding Explorer reads is
/// tested here rather than discovered by pasting.
/// <para>
/// Decoding treats the bytes as untrusted: they come from whichever application last wrote the
/// clipboard or started the drag. Every offset is bounds-checked and a malformed payload yields
/// whatever whole paths precede the damage, never an exception.
/// </para>
/// </summary>
public static class DropFiles
{
    /// <summary><c>sizeof(DROPFILES)</c>: pFiles, pt.x, pt.y, fNC, fWide.</summary>
    public const int HeaderSize = 20;

    /// <summary>The <c>CF_HDROP</c> payload for <paramref name="paths"/>: wide, no point, ready for a global.</summary>
    public static byte[] Encode(IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var chars = paths.Sum(p => p.Length + 1) + 1;
        var bytes = new byte[HeaderSize + (chars * 2)];

        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0), HeaderSize);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), 1); // fWide

        var at = HeaderSize;

        foreach (var path in paths)
        {
            if (path.Contains('\0', StringComparison.Ordinal))
            {
                throw new ArgumentException("a path cannot contain NUL", nameof(paths));
            }

            Encoding.Unicode.GetBytes(path, bytes.AsSpan(at));
            at += (path.Length + 1) * 2;
        }

        return bytes;
    }

    /// <summary>The paths in a <c>CF_HDROP</c> payload, or none for one that cannot be read.</summary>
    public static IReadOnlyList<string> Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < HeaderSize)
        {
            return [];
        }

        var offset = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
        var wide = BinaryPrimitives.ReadInt32LittleEndian(bytes[16..]) != 0;

        if (offset < HeaderSize || offset >= (uint)bytes.Length)
        {
            return [];
        }

        var list = bytes[(int)offset..];
        var paths = new List<string>();

        if (wide)
        {
            while (list.Length >= 2)
            {
                var end = IndexOfWideNul(list);

                if (end <= 0)
                {
                    // A NUL first is the list's end; no NUL at all is a truncated payload, whose last
                    // unterminated path is not trusted.
                    break;
                }

                paths.Add(Encoding.Unicode.GetString(list[..end]));
                list = list[(end + 2)..];
            }
        }
        else
        {
            while (list.Length > 0)
            {
                var end = list.IndexOf((byte)0);

                if (end <= 0)
                {
                    break;
                }

                paths.Add(Encoding.Latin1.GetString(list[..end]));
                list = list[(end + 1)..];
            }
        }

        return paths;
    }

    /// <summary>The <c>Preferred DropEffect</c> payload: one little-endian DWORD.</summary>
    public static byte[] EncodeEffect(DropEffect effect)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)effect);
        return bytes;
    }

    /// <summary>
    /// The effect a <c>Preferred DropEffect</c> payload asks for. Explorer writes Move|Copy for a cut
    /// on some paths and Move alone on others; Move present means the paste is a move.
    /// </summary>
    public static DropEffect DecodeEffect(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 4)
        {
            return DropEffect.Copy;
        }

        var value = (DropEffect)BinaryPrimitives.ReadUInt32LittleEndian(bytes);
        return value.HasFlag(DropEffect.Move) ? DropEffect.Move
            : value.HasFlag(DropEffect.Link) && !value.HasFlag(DropEffect.Copy) ? DropEffect.Link
            : DropEffect.Copy;
    }

    private static int IndexOfWideNul(ReadOnlySpan<byte> bytes)
    {
        for (var i = 0; i + 1 < bytes.Length; i += 2)
        {
            if (bytes[i] == 0 && bytes[i + 1] == 0)
            {
                return i;
            }
        }

        return -1;
    }
}
