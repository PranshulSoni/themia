using System.Diagnostics;
using Themia.Core.Diagnostics;
using Themia.Core.Model;
using Themia.Core.Widgets;

namespace Themia.App.Services;

/// <summary>What one listing was asked for. A value, so it can be captured by a background task.</summary>
/// <param name="Path">An already-validated, fully qualified folder path.</param>
/// <param name="Sort">Which column orders the rows.</param>
/// <param name="Descending">Reverse that column.</param>
/// <param name="FoldersFirst">Group directories above files.</param>
/// <param name="ShowHidden">Include hidden and system entries.</param>
/// <param name="MaxCount">How many rows to keep, already clamped by the config.</param>
internal readonly record struct FolderRequest(
    string Path,
    FolderSort Sort,
    bool Descending,
    bool FoldersFirst,
    bool ShowHidden,
    int MaxCount);

/// <summary>
/// Reading a folder off the disk, and handing it to the shell when a row is opened.
/// <para>
/// Every method here is <b>background work</b>. Enumerating a directory is I/O, and on a mapped
/// drive whose server has gone away a single <c>FindFirstFile</c> can sit for the better part of a
/// minute; run on the UI thread that is the whole desktop frozen. So the view marshals a request
/// here, onto a thread pool thread, and marshals a <see cref="FolderSnapshot"/> back.
/// </para>
/// <para>
/// Nothing here writes. The widget is read-only by design, so the only shell verb it knows is the
/// default one, and file operations stay in Explorer where there is an undo stack.
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
    public static FolderSnapshot Read(FolderRequest request, CancellationToken token)
    {
        try
        {
            // The order of these two matters. "It is a file" and "it is not there" want different
            // sentences, and Directory.Exists answers false to both.
            if (!Directory.Exists(request.Path))
            {
                return FolderSnapshot.Of(File.Exists(request.Path)
                    ? FolderListingStatus.NotAFolder
                    : FolderListingStatus.NotFound);
            }

            var scanned = Scan(request.Path, token);

            var arranged = FolderRules.Arrange(
                scanned,
                request.Sort,
                request.Descending,
                request.FoldersFirst,
                request.ShowHidden,
                request.MaxCount);

            return arranged.Count == 0
                ? FolderSnapshot.Of(FolderListingStatus.Empty)
                : new FolderSnapshot(FolderListingStatus.Ok, arranged);
        }
        catch (OperationCanceledException)
        {
            // The view moved on. Not a failure, and nothing will look at this result.
            return FolderSnapshot.Of(FolderListingStatus.Loading);
        }
        catch (UnauthorizedAccessException ex)
        {
            Log.Warn(Scope, $"access denied listing '{request.Path}'", ex);
            return FolderSnapshot.Of(FolderListingStatus.AccessDenied);
        }
        catch (DirectoryNotFoundException ex)
        {
            // Racing a delete between the Exists check and the enumeration.
            Log.Warn(Scope, $"'{request.Path}' disappeared while it was being listed", ex);
            return FolderSnapshot.Of(FolderListingStatus.NotFound);
        }
        catch (IOException ex)
        {
            Log.Error(Scope, $"could not list '{request.Path}'", ex);
            return FolderSnapshot.Of(FolderListingStatus.Failed);
        }
        catch (ArgumentException ex)
        {
            // The path passed validation but the framework still refused it. Worth saying out loud,
            // because it means the two disagree about what a path is.
            Log.Error(Scope, $"'{request.Path}' was refused as a path", ex);
            return FolderSnapshot.Of(FolderListingStatus.InvalidPath);
        }
        catch (NotSupportedException ex)
        {
            Log.Error(Scope, $"'{request.Path}' is not a supported path", ex);
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
                (attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Debug(Scope, $"skipping '{info.Name}': {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Hand a path to the shell, which is what double-clicking a row does: the same open Explorer
    /// would do, with the user's own file associations, and no verb this widget chose.
    /// </summary>
    public static void Open(string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            return;
        }

        try
        {
            // using: ShellExecute hands back a process handle even when nothing is going to be
            // waited on, and leaking one per double-click on a surface that lives for weeks adds up.
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = fullPath,
                UseShellExecute = true,
            });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            // No association, a cancelled elevation prompt, or the file went away. The desktop
            // stays up and the log says why nothing opened.
            Log.Warn(Scope, $"could not open '{fullPath}'", ex);
        }
    }
}
