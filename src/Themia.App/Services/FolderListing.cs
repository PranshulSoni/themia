using Themia.Core.Diagnostics;
using Themia.Core.Widgets;

namespace Themia.App.Services;

/// <summary>
/// Reading a folder off the disk.
/// <para>
/// Every method here is <b>background work</b>. Enumerating a directory is I/O, and on a mapped
/// drive whose server has gone away a single <c>FindFirstFile</c> can sit for the better part of a
/// minute; run on the UI thread that is the whole desktop frozen. So the view marshals a request
/// here, onto a thread pool thread, and marshals a <see cref="FolderSnapshot"/> back.
/// </para>
/// <para>
/// Everything is returned unsorted and unfiltered: the order, the filters and the search are applied
/// by the view ([S-1321]), so changing any of them redraws what is in hand rather than re-reading the
/// disk. File operations live in <see cref="Files.ShellFileOps"/>.
/// </para>
/// </summary>
internal static class FolderListing
{
    private const string Scope = "folder";

    /// <summary>
    /// The most entries one listing will materialise.
    /// <para>
    /// A folder can hold hundreds of thousands of files, and building a value for every one of them
    /// to then show twenty is how a widget ends up holding a hundred megabytes. Past this the
    /// listing is truncated and says so in the log: the order is then the order of the first
    /// <see cref="MaxScanned"/> the filesystem returned, which is a real limitation, but a bounded
    /// wrong order beats an unbounded allocation on a background thread nobody is watching.
    /// </para>
    /// </summary>
    private const int MaxScanned = 50_000;

    /// <summary>
    /// List a folder. Never throws: every failure comes back as a named
    /// <see cref="FolderListingStatus"/> the widget has a sentence for.
    /// </summary>
    public static FolderSnapshot Read(string path, CancellationToken token)
    {
        try
        {
            // The order of these two matters. "It is a file" and "it is not there" want different
            // sentences, and Directory.Exists answers false to both.
            if (!Directory.Exists(path))
            {
                return FolderSnapshot.Of(File.Exists(path)
                    ? FolderListingStatus.NotAFolder
                    : FolderListingStatus.NotFound);
            }

            var scanned = Scan(path, token);

            return scanned.Count == 0
                ? FolderSnapshot.Of(FolderListingStatus.Empty)
                : new FolderSnapshot(FolderListingStatus.Ok, scanned);
        }
        catch (OperationCanceledException)
        {
            // The view moved on. Not a failure, and nothing will look at this result.
            return FolderSnapshot.Of(FolderListingStatus.Loading);
        }
        catch (UnauthorizedAccessException ex)
        {
            Log.Warn(Scope, $"access denied listing '{path}'", ex);
            return FolderSnapshot.Of(FolderListingStatus.AccessDenied);
        }
        catch (DirectoryNotFoundException ex)
        {
            // Racing a delete between the Exists check and the enumeration.
            Log.Warn(Scope, $"'{path}' disappeared while it was being listed", ex);
            return FolderSnapshot.Of(FolderListingStatus.NotFound);
        }
        catch (IOException ex)
        {
            Log.Error(Scope, $"could not list '{path}'", ex);
            return FolderSnapshot.Of(FolderListingStatus.Failed);
        }
        catch (ArgumentException ex)
        {
            // The path passed validation but the framework still refused it. Worth saying out loud,
            // because it means the two disagree about what a path is.
            Log.Error(Scope, $"'{path}' was refused as a path", ex);
            return FolderSnapshot.Of(FolderListingStatus.InvalidPath);
        }
        catch (NotSupportedException ex)
        {
            Log.Error(Scope, $"'{path}' is not a supported path", ex);
            return FolderSnapshot.Of(FolderListingStatus.InvalidPath);
        }
    }

    /// <summary>
    /// Walk the directory once, taking everything a row needs from the find data the enumeration
    /// already has. No second call per entry: that is what turns a listing into a per-file stat
    /// storm on a network share.
    /// </summary>
    private static List<FolderEntry> Scan(string path, CancellationToken token)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = false,
            ReturnSpecialDirectories = false,

            // Nothing is skipped here: hidden and system entries are filtered by the config, which
            // can be turned back on, and letting the enumeration drop them would make the setting
            // unable to show them again.
            AttributesToSkip = 0,

            // A folder the user can list but which contains a subtree they cannot is normal on a
            // system drive. Skipping what cannot be read beats failing the whole listing.
            IgnoreInaccessible = true,
        };

        var entries = new List<FolderEntry>();

        // foreach disposes the enumerator, and with it the Win32 find handle, on every path out of
        // this loop — including the throw from ThrowIfCancellationRequested.
        foreach (var info in new DirectoryInfo(path).EnumerateFileSystemInfos("*", options))
        {
            // Cancellation is checked between entries, which is the only place it can be: a
            // MoveNext that is blocked inside FindNextFile on a dead share cannot be interrupted,
            // so a disconnected path is abandoned rather than stopped. The view discards the
            // result either way.
            token.ThrowIfCancellationRequested();

            if (entries.Count >= MaxScanned)
            {
                Log.Warn(Scope, $"'{path}' holds more than {MaxScanned} entries; the listing is truncated");
                break;
            }

            var entry = Describe(info);

            if (entry is not null)
            {
                entries.Add(entry.Value);
            }
        }

        return entries;
    }

    /// <summary>
    /// One entry, or null when it evaporated mid-walk. A file deleted between the find and the read
    /// is ordinary in a folder somebody is working in, and is not worth failing the tile over.
    /// </summary>
    private static FolderEntry? Describe(FileSystemInfo info)
    {
        try
        {
            var attributes = info.Attributes;
            var isDirectory = (attributes & FileAttributes.Directory) != 0;

            return new FolderEntry(
                info.Name,
                info.FullName,
                isDirectory,
                info is FileInfo file ? file.Length : 0,
                new DateTimeOffset(info.LastWriteTime),
                (attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0,
                (int)attributes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Debug(Scope, $"skipping '{info.Name}': {ex.Message}");
            return null;
        }
    }
}
