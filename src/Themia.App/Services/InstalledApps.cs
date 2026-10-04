using Themia.Core.Diagnostics;
using Themia.Core.Widgets;

namespace Themia.App.Services;

/// <summary>
/// The applications this PC has, read off the Start menu.
/// <para>
/// The Start menu rather than the registry's uninstall list or the AppsFolder shell namespace, for
/// two reasons. It is what the user already thinks of as "my apps" — the same names, in the same
/// folders, that the Start menu shows — and it is plain filesystem enumeration, so it needs no COM
/// apartment, no reflection and nothing that Native AOT has an opinion about. The cost is that
/// Store apps without a Start-menu shortcut do not appear; that is a real limitation, and it is a
/// better one than a list of "Microsoft Visual C++ 2015 Redistributable" entries nobody can launch.
/// </para>
/// <para>
/// Every method here is <b>background work</b>: two directory trees, sometimes on a slow profile
/// disk or a roaming share. Called on the UI thread it is the whole desktop frozen while it walks.
/// </para>
/// </summary>
internal static class InstalledApps
{
    private const string Scope = "apps";

    /// <summary>
    /// How deep into the Start menu to walk. The tree is two or three levels in practice; a bound
    /// means a symlink loop in a roaming profile cannot turn this into an infinite walk.
    /// </summary>
    private const int MaxDepth = 4;

    /// <summary>
    /// The most shortcuts to look at before giving up on the rest, whatever the config asks to
    /// show. A profile with a pathological Start menu must not make this allocate without bound.
    /// </summary>
    private const int MaxScanned = 5000;

    /// <summary>
    /// Walk the user's and the machine's Start menus. Never throws: a tree that cannot be read
    /// contributes nothing and says so in the log.
    /// </summary>
    /// <param name="maxCount">How many entries to keep, after de-duplication and sorting.</param>
    /// <param name="token">Cancelled when the view has moved on.</param>
    public static IReadOnlyList<InstalledApp> Read(int maxCount, CancellationToken token)
    {
        var scanned = new List<InstalledApp>();

        // The user's menu first: when both hold a shortcut of the same name, the per-user install
        // is the one that machine actually has, and Arrange keeps the first of a duplicate.
        foreach (var root in Roots())
        {
            Scan(root, string.Empty, 0, scanned, token);
        }

        return InstalledAppRules.Arrange(scanned, maxCount);
    }

    /// <summary>The two Start-menu trees, in the order they take precedence.</summary>
    private static IEnumerable<string> Roots()
    {
        var user = Folder(Environment.SpecialFolder.Programs);

        if (user.Length > 0)
        {
            yield return user;
        }

        var machine = Folder(Environment.SpecialFolder.CommonPrograms);

        if (machine.Length > 0 && !machine.Equals(user, StringComparison.OrdinalIgnoreCase))
        {
            yield return machine;
        }
    }

    private static string Folder(Environment.SpecialFolder folder)
    {
        try
        {
            return Environment.GetFolderPath(folder);
        }
        catch (Exception ex) when (ex is ArgumentException or PlatformNotSupportedException)
        {
            Log.Warn(Scope, $"could not resolve the {folder} folder", ex);
            return string.Empty;
        }
    }

    private static void Scan(
        string directory,
        string category,
        int depth,
        List<InstalledApp> into,
        CancellationToken token)
    {
        if (depth > MaxDepth || into.Count >= MaxScanned)
        {
            return;
        }

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = false,
            ReturnSpecialDirectories = false,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,

            // A Start-menu folder the user cannot read is normal on a managed machine. Skipping it
            // beats failing the whole list.
            IgnoreInaccessible = true,
        };

        try
        {
            // foreach disposes the enumerator, and with it the Win32 find handle, on every path out
            // of this loop — including the throw from ThrowIfCancellationRequested.
            foreach (var entry in new DirectoryInfo(directory).EnumerateFileSystemInfos("*", options))
            {
                token.ThrowIfCancellationRequested();

                if (into.Count >= MaxScanned)
                {
                    Log.Warn(Scope, $"stopped after {MaxScanned} Start-menu entries; the list is truncated");
                    return;
                }

                if (entry is DirectoryInfo folder)
                {
                    if (InstalledAppRules.IsSkippedFolder(folder.Name))
                    {
                        continue;
                    }

                    // The category is the top-level folder's name, not the full path: "Accessories",
                    // not "Accessories\System Tools\…". A category that keeps growing with depth is
                    // a column nobody can read.
                    var next = category.Length == 0 ? folder.Name : category;
                    Scan(folder.FullName, next, depth + 1, into, token);
                    continue;
                }

                if (!entry.Name.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var name = Path.GetFileNameWithoutExtension(entry.Name);

                if (!InstalledAppRules.IsLauncherShortcut(name))
                {
                    continue;
                }

                into.Add(new InstalledApp(name, category, entry.FullName));
            }
        }
        catch (OperationCanceledException)
        {
            throw; // the caller discards the whole scan
        }
        catch (UnauthorizedAccessException ex)
        {
            Log.Debug(Scope, $"skipping '{directory}': {ex.Message}");
        }
        catch (DirectoryNotFoundException ex)
        {
            // A Start-menu folder removed by an uninstaller mid-walk. Ordinary, not a failure.
            Log.Debug(Scope, $"'{directory}' disappeared while it was being read: {ex.Message}");
        }
        catch (IOException ex)
        {
            Log.Warn(Scope, $"could not read '{directory}'", ex);
        }
    }
}
