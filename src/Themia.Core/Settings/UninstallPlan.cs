namespace Themia.Core.Settings;

/// <summary>How Themia takes itself off the PC. [S-1552]</summary>
public enum UninstallRoute
{
    /// <summary>
    /// Installed as an MSIX package (the Store build): the package removes itself through
    /// <c>PackageManager.RemovePackageAsync</c>. No capability is needed — a full-trust desktop app
    /// runs at medium integrity, and the caller's publisher is the package's own.
    /// </summary>
    RemovePackage,

    /// <summary>
    /// Not packaged (a development build, or a copy run from a folder): Windows' "Installed apps" page
    /// is opened for the user to finish, and Themia quits.
    /// </summary>
    OpenInstalledApps,
}

/// <summary>
/// What pressing "Uninstall" will do, worked out before anything happens so the confirmation can say
/// it and the tests can pin it. [S-1552..S-1554]
/// </summary>
/// <param name="Route">How the app itself goes.</param>
/// <param name="FoldersToDelete">
/// The user's own data, only when they ticked the box: <c>%APPDATA%\Themia</c> and
/// <c>%LOCALAPPDATA%\Themia</c>. Never anything else — see <see cref="DataFolders"/>.
/// </param>
/// <param name="Survey">The goodbye survey, as the original installer opened it on an interactive uninstall.</param>
/// <param name="Fallback">Where to send the user when removing the package itself fails.</param>
public sealed record UninstallPlan(
    UninstallRoute Route,
    IReadOnlyList<string> FoldersToDelete,
    Uri Survey,
    Uri Fallback)
{
    /// <summary>Windows Settings › Apps › Installed apps.</summary>
    public static readonly Uri InstalledApps = new("ms-settings:appsfeatures");

    /// <summary>The calling package's own page in Installed apps, with its Uninstall button.</summary>
    public static readonly Uri OwnAppPage = new("ms-settings:appsfeatures-app");

    /// <summary>
    /// The plan for this install.
    /// </summary>
    /// <param name="packaged">Whether the process runs with package identity.</param>
    /// <param name="deleteData">The user ticked "also delete my widgets, settings and accounts".</param>
    /// <param name="roamingRoot"><c>%APPDATA%</c>.</param>
    /// <param name="localRoot"><c>%LOCALAPPDATA%</c>.</param>
    /// <param name="folderName">The app's folder name in both (<c>AppStateStore.FolderName</c>).</param>
    /// <param name="version">The running version, for the survey.</param>
    public static UninstallPlan Create(
        bool packaged,
        bool deleteData,
        string? roamingRoot,
        string? localRoot,
        string folderName,
        string? version)
        => new(
            packaged ? UninstallRoute.RemovePackage : UninstallRoute.OpenInstalledApps,
            deleteData ? DataFolders(roamingRoot, localRoot, folderName) : [],
            SurveyUrl(version),
            packaged ? OwnAppPage : InstalledApps);

    /// <summary>
    /// <c>https://themia.app/goodbye?v=&lt;version&gt;</c>, the version escaped. A missing version is
    /// still a survey worth opening, so it is sent as "unknown" rather than dropped.
    /// </summary>
    public static Uri SurveyUrl(string? version)
    {
        var v = string.IsNullOrWhiteSpace(version) ? "unknown" : version.Trim();
        return new Uri($"https://themia.app/goodbye?v={Uri.EscapeDataString(v)}");
    }

    /// <summary>
    /// The folders that hold the user's data, and nothing that is not provably one of them.
    /// <para>
    /// This list feeds a recursive delete, so it is built defensively: a root that is missing or
    /// not absolute (an environment with no profile), and a folder name that is empty, has a
    /// separator in it, or climbs out with "..", produce no entry at all. Deleting nothing is
    /// recoverable; deleting the wrong folder is not.
    /// </para>
    /// </summary>
    public static IReadOnlyList<string> DataFolders(string? roamingRoot, string? localRoot, string folderName)
    {
        if (string.IsNullOrWhiteSpace(folderName)
            || folderName.Trim() != folderName
            || folderName is "." or ".."
            || folderName.IndexOfAny(['\\', '/', ':']) >= 0
            || folderName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return [];
        }

        var folders = new List<string>();

        foreach (var root in new[] { roamingRoot, localRoot })
        {
            if (string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root))
            {
                continue;
            }

            var folder = Path.GetFullPath(Path.Combine(root, folderName));

            // The folder must sit directly in its root: anything else means the name was not what
            // it looked like.
            if (!string.Equals(Path.GetDirectoryName(folder), Path.GetFullPath(root).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!folders.Contains(folder, StringComparer.OrdinalIgnoreCase))
            {
                folders.Add(folder);
            }
        }

        return folders;
    }
}
