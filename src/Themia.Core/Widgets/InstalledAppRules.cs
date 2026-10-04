using Themia.Core.Localization;
namespace Themia.Core.Widgets;

/// <summary>One application the Start menu knows about.</summary>
/// <param name="Name">What to call it: the shortcut's own name, without the extension.</param>
/// <param name="Category">The Start-menu folder it sits in, or empty at the top level.</param>
/// <param name="Target">The shortcut file. Handed to the shell unchanged, after being validated.</param>
public readonly record struct InstalledApp(string Name, string Category, string Target);

/// <summary>
/// The rules the app list follows, as pure functions of what was found on disk.
/// <para>
/// Kept out of the enumeration itself so the decisions that shape the list — what counts as an
/// application, which duplicate wins, how the filter box narrows it — can be tested without a Start
/// menu to point at. The enumeration is then only "walk these folders and call these".
/// </para>
/// </summary>
public static class InstalledAppRules
{
    /// <summary>The most entries the list will hold, whatever the Start menu contains.</summary>
    public const int MaxEntries = 1000;

    /// <summary>
    /// Start-menu folders that are not a category of applications.
    /// <para>
    /// <c>Startup</c> is the one that matters: its contents are things Windows runs at sign-in, and
    /// listing them as launchable apps is both wrong and confusing.
    /// </para>
    /// </summary>
    public static bool IsSkippedFolder(string? folderName)
        => folderName is not null && folderName.Equals("Startup", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether a shortcut is a launcher entry rather than one of the pieces of housekeeping that
    /// ship beside it. An "Uninstall Foo" tile one mis-click away on the desktop is a hazard, not a
    /// feature.
    /// </summary>
    public static bool IsLauncherShortcut(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        foreach (var excluded in Excluded)
        {
            if (name.Contains(excluded, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static readonly string[] Excluded = ["uninstall", "uninst", "remove "];

    /// <summary>
    /// Put the scan in order: one entry per name, sorted, capped.
    /// <para>
    /// The first of a duplicate name wins, and the caller scans the user's Start menu before the
    /// machine's, so a per-user install shadows the all-users one — which is the one the user
    /// actually has.
    /// </para>
    /// </summary>
    public static List<InstalledApp> Arrange(IEnumerable<InstalledApp> scanned, int maxCount)
    {
        ArgumentNullException.ThrowIfNull(scanned);

        var limit = Math.Clamp(maxCount, 1, MaxEntries);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var kept = new List<InstalledApp>();

        foreach (var app in scanned)
        {
            if (app.Name.Length == 0 || !seen.Add(app.Name))
            {
                continue;
            }

            kept.Add(app);
        }

        kept.Sort(static (a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

        if (kept.Count > limit)
        {
            kept.RemoveRange(limit, kept.Count - limit);
        }

        return kept;
    }

    /// <summary>
    /// Narrow the list to what matches the filter box: a case-insensitive substring of the name or
    /// of the folder it came from. A blank filter is not a filter.
    /// </summary>
    public static List<InstalledApp> Filter(IReadOnlyList<InstalledApp> apps, string? query)
    {
        ArgumentNullException.ThrowIfNull(apps);

        var text = (query ?? string.Empty).Trim();

        if (text.Length == 0)
        {
            return [.. apps];
        }

        var matched = new List<InstalledApp>();

        foreach (var app in apps)
        {
            if (app.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
                || app.Category.Contains(text, StringComparison.OrdinalIgnoreCase))
            {
                matched.Add(app);
            }
        }

        return matched;
    }

    /// <summary>What the top-level bucket is called: apps that sit in no Start-menu folder.</summary>
    public static string TopLevelTitle(Locale locale) => Strings.Get(locale, Str.Category_Other);

    /// <summary>
    /// [S-1523] What the widget shows: the scan less what the user hid — by shortcut path, then by
    /// folder — then narrowed to one folder (the XL folder chips; null is every folder), then by the
    /// filter box. Hiding comes first (original S-22 / S-23), so a search never brings a hidden app
    /// back.
    /// </summary>
    public static List<InstalledApp> Visible(
        IReadOnlyList<InstalledApp> apps,
        IReadOnlyCollection<string> hiddenApps,
        IReadOnlyCollection<string> hiddenCategories,
        string? category,
        string? query)
    {
        ArgumentNullException.ThrowIfNull(apps);
        ArgumentNullException.ThrowIfNull(hiddenApps);
        ArgumentNullException.ThrowIfNull(hiddenCategories);

        var hiddenPaths = new HashSet<string>(hiddenApps, StringComparer.OrdinalIgnoreCase);
        var hiddenFolders = new HashSet<string>(hiddenCategories, StringComparer.OrdinalIgnoreCase);

        var kept = apps
            .Where(app => !hiddenPaths.Contains(app.Target) && !hiddenFolders.Contains(app.Category))
            .Where(app => category is null || app.Category.Equals(category, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return Filter(kept, query);
    }

    /// <summary>How many of <paramref name="apps"/> the user's hide lists leave out.</summary>
    public static int HiddenCount(
        IReadOnlyList<InstalledApp> apps,
        IReadOnlyCollection<string> hiddenApps,
        IReadOnlyCollection<string> hiddenCategories)
        => apps.Count - Visible(apps, hiddenApps, hiddenCategories, null, null).Count;

    /// <summary>
    /// [S-1521] The list in folder sections (original S-01 / S-05): named folders in alphabetical
    /// order, then the top-level apps as <see cref="TopLevelTitle"/> in <paramref name="locale"/>. The order inside a section is
    /// the list's own (by name). Not grouping is one untitled section. Callers show the titles only
    /// when there is more than one section: a lone bucket under a heading is a heading over nothing.
    /// </summary>
    public static List<AppSection> Sections(IReadOnlyList<InstalledApp> apps, bool group, Locale locale)
    {
        ArgumentNullException.ThrowIfNull(apps);

        if (apps.Count == 0)
        {
            return [];
        }

        if (!group)
        {
            return [new AppSection(string.Empty, string.Empty, [.. apps])];
        }

        var buckets = new Dictionary<string, List<InstalledApp>>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();

        foreach (var app in apps)
        {
            if (!buckets.TryGetValue(app.Category, out var bucket))
            {
                bucket = [];
                buckets[app.Category] = bucket;
                order.Add(app.Category);
            }

            bucket.Add(app);
        }

        order.Sort(static (a, b) => (a.Length == 0, b.Length == 0) switch
        {
            (true, false) => 1,
            (false, true) => -1,
            _ => string.Compare(a, b, StringComparison.OrdinalIgnoreCase),
        });

        return order.Select(key => new AppSection(key, CategoryTitle(key, locale), buckets[key])).ToList();
    }

    /// <summary>[S-1524] Every folder the scan found, in section order, for the settings page and the folder chips.</summary>
    public static List<string> Categories(IEnumerable<InstalledApp> apps)
    {
        ArgumentNullException.ThrowIfNull(apps);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var found = apps.Select(app => app.Category).Where(seen.Add).ToList();

        found.Sort(static (a, b) => (a.Length == 0, b.Length == 0) switch
        {
            (true, false) => 1,
            (false, true) => -1,
            _ => string.Compare(a, b, StringComparison.OrdinalIgnoreCase),
        });

        return found;
    }

    /// <summary>A folder's name as shown (Windows names the folders in its own language): the top level reads <see cref="TopLevelTitle"/>.</summary>
    public static string CategoryTitle(string? category, Locale locale)
        => string.IsNullOrEmpty(category) ? TopLevelTitle(locale) : category;

    /// <summary>
    /// [S-1522] The initials a tile shows until its icon arrives, or for good when it has none
    /// (original S-07): the first letter of each of the first two words, upper-cased; "?" for a name
    /// with no words. Letters are text elements, so an accented or composed first letter is kept whole.
    /// </summary>
    public static string Initials(string? name)
    {
        var words = (name ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        if (words.Length == 0)
        {
            return "?";
        }

        return string.Concat(words.Take(2).Select(word =>
            System.Globalization.StringInfo.GetNextTextElement(word, 0).ToUpperInvariant()));
    }

    /// <summary>
    /// [S-1522] The hue, 0–359, behind a tile's initials: a hash of the name, so an app keeps its
    /// colour across scans, restarts and machines. Computed exactly as the original did
    /// (<c>h = (h * 31 + code) | 0</c> over UTF-16 code units, then <c>|h| % 360</c>), so an app keeps
    /// the colour it had there.
    /// </summary>
    public static int Hue(string? name)
    {
        var h = 0;

        foreach (var c in name ?? string.Empty)
        {
            h = unchecked((h * 31) + c);
        }

        return (int)(Math.Abs((long)h) % 360);
    }
}

/// <summary>[S-1521] One folder's apps under its title; the untitled section is the ungrouped list.</summary>
/// <param name="Category">The folder name as scanned (empty for the top level).</param>
/// <param name="Title">What the section heading says.</param>
/// <param name="Apps">Its apps, in list order.</param>
public sealed record AppSection(string Category, string Title, IReadOnlyList<InstalledApp> Apps);
