using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Themia.Core.Diagnostics;
using Themia.Core.Widgets;

namespace Themia.App.Services;

/// <summary>
/// Where a running process's executable is, and what that executable calls itself.
/// <para>
/// Shared by the mixer and screen time, which both have a process id and want to show a person a
/// name. Two calls on two different costs: the path is a kernel query on a limited handle and is
/// asked every time; the name is a read of the file's version resource from disk, so it is cached
/// per path. Both are safe on any thread, and the name read must never be made on the UI thread or
/// on a hook callback — the first time a binary is seen it is disk I/O.
/// </para>
/// </summary>
internal static partial class ProcessNames
{
    private const string Scope = "process-names";

    /// <summary>Only an app that churns through binaries would reach this; the cache starts over rather than grow.</summary>
    private const int CacheLimit = 256;

    private const uint QueryLimitedInformation = 0x1000;

    private static readonly Lock CacheGate = new();
    private static readonly Dictionary<string, string?> Names = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The full path of a running process's executable, or null for a process that has exited, the
    /// idle process, or one that refuses even a limited-information handle (protected or elevated).
    /// </summary>
    public static unsafe string? ExePath(uint pid)
    {
        if (pid == 0)
        {
            return null;
        }

        using var process = OpenProcess(QueryLimitedInformation, false, pid);
        if (process.IsInvalid)
        {
            return null;
        }

        var buffer = stackalloc char[1024];
        var length = 1024;

        return QueryFullProcessImageName(process, 0, buffer, ref length) && length > 0
            ? new string(buffer, 0, length)
            : null;
    }

    /// <summary>
    /// [S-441]/[S-458] What an executable calls itself — its description, or its product name when the
    /// description is an internal identifier — or null when its version resource says nothing usable.
    /// Read from disk once per path.
    /// </summary>
    public static string? DisplayName(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        lock (CacheGate)
        {
            if (Names.TryGetValue(path, out var known))
            {
                return known;
            }
        }

        // Read outside the lock: it is disk I/O, and two threads reading the same file once each is
        // cheaper than every other caller waiting on the first.
        var name = AppNames.FromVersionInfo(VersionString(path, "FileDescription"), VersionString(path, "ProductName"));

        lock (CacheGate)
        {
            if (Names.Count >= CacheLimit)
            {
                Names.Clear();
            }

            Names[path] = name;
        }

        return name;
    }

    /// <summary>
    /// One string from a binary's version resource, trying each language it declares and then the
    /// en-US block most binaries carry whatever their language. Null when there is none.
    /// </summary>
    private static unsafe string? VersionString(string path, string field)
    {
        var size = GetFileVersionInfoSize(path, out _);
        if (size == 0)
        {
            return null;
        }

        var data = NativeMemory.Alloc(size);
        try
        {
            if (!GetFileVersionInfo(path, 0, size, data))
            {
                return null;
            }

            var blocks = new List<string>();

            if (VerQueryValue(data, @"\VarFileInfo\Translation", out var table, out var tableLength) && table != 0)
            {
                var pairs = (ushort*)table;
                for (var i = 0; i + 1 < tableLength / 2; i += 2)
                {
                    blocks.Add($"{pairs[i]:X4}{pairs[i + 1]:X4}");
                }
            }

            if (!blocks.Contains("040904B0"))
            {
                blocks.Add("040904B0");
            }

            foreach (var block in blocks)
            {
                if (!VerQueryValue(data, $@"\StringFileInfo\{block}\{field}", out var text, out var length) || text == 0 || length == 0)
                {
                    continue;
                }

                // The returned length cannot be trusted to stop at the string's end — that is how
                // "Spotify\0…FileV" got into a name — so this reads to the first NUL instead and
                // Sanitize cuts at any control character after that.
                var value = new string((char*)text, 0, (int)Math.Min(length, 260));
                var nul = value.IndexOf('\0', StringComparison.Ordinal);
                if (nul >= 0)
                {
                    value = value[..nul];
                }

                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return null;
        }
        catch (Exception ex) when (ex is ArgumentException or OutOfMemoryException)
        {
            Log.Warn(Scope, $"could not read the version of {path}", ex);
            return null;
        }
        finally
        {
            NativeMemory.Free(data);
        }
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint pid);

    [LibraryImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, char* name, ref int size);

    [LibraryImport("version.dll", EntryPoint = "GetFileVersionInfoSizeW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint GetFileVersionInfoSize(string path, out uint handle);

    [LibraryImport("version.dll", EntryPoint = "GetFileVersionInfoW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool GetFileVersionInfo(string path, uint handle, uint length, void* data);

    [LibraryImport("version.dll", EntryPoint = "VerQueryValueW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool VerQueryValue(void* block, string subBlock, out nint buffer, out uint length);
}
