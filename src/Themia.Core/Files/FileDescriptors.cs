using System.Buffers.Binary;
using System.Text;

namespace Themia.Core.Files;

/// <summary>One file a source offers by content rather than by path (an Outlook attachment, a zip entry).</summary>
/// <param name="RelativePath">Where it goes under the destination; may name sub-folders, already made safe.</param>
/// <param name="IsDirectory">A folder to create rather than a stream to save.</param>
/// <param name="Size">The size the source declared, or null when it did not.</param>
public readonly record struct VirtualFile(string RelativePath, bool IsDirectory, long? Size);

/// <summary>
/// [S-1332] <c>FileGroupDescriptorW</c>: the list of "virtual" files a drag or clipboard source
/// offers through <c>FileContents</c> streams. Pure, and hostile-input safe: the count in the header
/// is clamped to what the buffer really holds, names are read only up to their fixed field, and a
/// name that would escape the destination (rooted, <c>..</c>, a device name) is dropped.
/// <para>
/// <c>FILEDESCRIPTORW</c> is 592 bytes with the name at offset 72 (every member is 4-byte aligned:
/// FILETIME is two DWORDs). The original read 596 / 76, which would have shifted every name after the
/// first by two characters; this layout is the SDK's.
/// </para>
/// </summary>
public static class FileDescriptors
{
    public const int DescriptorSize = 592;
    private const int AttributesOffset = 36;
    private const int SizeHighOffset = 64;
    private const int SizeLowOffset = 68;
    private const int NameOffset = 72;
    private const int NameChars = 260;
    private const uint FdAttributes = 0x4;
    private const uint FdFileSize = 0x40;
    private const uint AttributeDirectory = 0x10;

    /// <summary>The safe entries of a <c>FILEGROUPDESCRIPTORW</c>, in order, with each one's index into <c>FileContents</c>.</summary>
    public static IReadOnlyList<(int Index, VirtualFile File)> Parse(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 4)
        {
            return [];
        }

        var claimed = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
        var fits = (bytes.Length - 4) / DescriptorSize;
        var count = (int)Math.Min(claimed, (uint)fits);
        var files = new List<(int, VirtualFile)>(Math.Min(count, 1024));

        for (var i = 0; i < count; i++)
        {
            var d = bytes.Slice(4 + (i * DescriptorSize), DescriptorSize);
            var flags = BinaryPrimitives.ReadUInt32LittleEndian(d);
            var attributes = BinaryPrimitives.ReadUInt32LittleEndian(d[AttributesOffset..]);

            var nameBytes = d.Slice(NameOffset, NameChars * 2);
            var end = 0;

            while (end < NameChars && (nameBytes[end * 2] != 0 || nameBytes[(end * 2) + 1] != 0))
            {
                end++;
            }

            var name = Encoding.Unicode.GetString(nameBytes[..(end * 2)]);

            if (SafeRelative(name) is not { } safe)
            {
                continue;
            }

            long? size = (flags & FdFileSize) != 0
                ? ((long)BinaryPrimitives.ReadUInt32LittleEndian(d[SizeHighOffset..]) << 32)
                    | BinaryPrimitives.ReadUInt32LittleEndian(d[SizeLowOffset..])
                : null;

            var isDirectory = (flags & FdAttributes) != 0 && (attributes & AttributeDirectory) != 0;
            files.Add((i, new VirtualFile(safe, isDirectory, size)));
        }

        return files;
    }

    /// <summary>
    /// A source-supplied relative name made safe to join under a destination folder, or null when it
    /// cannot be: rooted, a drive, <c>..</c> or <c>.</c> segments, characters Windows forbids in a
    /// name, or a reserved device name (<c>CON</c>, <c>NUL</c>, <c>COM1</c>…).
    /// </summary>
    public static string? SafeRelative(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var segments = name.Replace('/', '\\').Split('\\');
        var clean = new List<string>(segments.Length);

        foreach (var segment in segments)
        {
            if (FileNames.Validate(segment) != FileNameProblem.None)
            {
                return null;
            }

            clean.Add(segment);
        }

        return string.Join('\\', clean);
    }
}
