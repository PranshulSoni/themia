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

/// <summary>One thing the uninstall does after Themia has closed. [S-1071]</summary>
public enum UninstallStep
{
    /// <summary>Switch "start with Windows" off (Run value or StartupTask).</summary>
    RemoveAutostart,

    /// <summary>Delete <see cref="UninstallPlan.FoldersToDelete"/> from this process (packaged).</summary>
    DeleteDataNow,

    /// <summary><c>PackageManager.RemovePackageAsync</c> on this package; on failure, open <see cref="UninstallPlan.Fallback"/>.</summary>
    RemovePackage,

    /// <summary>Start <c>Themia.exe --purge-data &lt;pid&gt;</c>, which deletes the folders once this process has exited (loose).</summary>
    StartDataPurge,

    /// <summary>Open Installed apps for the user to finish (loose).</summary>
    OpenInstalledApps,
}

/// <summary>
/// What pressing "Uninstall" will do, worked out before anything happens so the confirmation can say
/// it and the tests can pin it. [S-1552..S-1554]
/// </summary>
/// <param name="Route">How the app itself goes.</param>
/// <param name="FoldersToDelete">
/// The user's own data: <c>%APPDATA%\Themia</c> and <c>%LOCALAPPDATA%\Themia</c>, never anything
/// else — see <see cref="DataFolders"/>. Loose builds: only when the user ticked the box. Packaged:
/// always ([S-1070]), see <see cref="DataGoesWithPackage"/>.
/// </param>
/// <param name="Survey">The goodbye survey, as the original installer opened it on an interactive uninstall.</param>
/// <param name="Fallback">Where to send the user when removing the package itself fails.</param>
public sealed record UninstallPlan(
    UninstallRoute Route,
    IReadOnlyList<string> FoldersToDelete,
    Uri Survey,
    Uri Fallback)
{
    /// <summary>
    /// [S-1070] Packaged, the data cannot be kept, so there is no choice to offer. A packaged
    /// full-trust app's new files under <c>AppData</c> are redirected to its private per-package
    /// location, which Windows deletes with the package ("Understanding how packaged desktop apps
    /// run on Windows", MSIX docs). Deleting the folders first, from the app, also takes any data a
    /// developer build left in the real location that the package went on using.
    /// </summary>
    public bool DataGoesWithPackage => Route == UninstallRoute.RemovePackage;

    /// <summary>
    /// [S-1071] What happens after the app has closed and saved, in order. Autostart is cleared
    /// first, whatever else fails: a loose build would otherwise leave a Run entry pointing at a
    /// removed exe (a packaged StartupTask goes with the package anyway; clearing it is harmless).
    /// Packaged, the data goes before the package because removing the package ends this process.
    /// </summary>
    public IReadOnlyList<UninstallStep> Steps => Route == UninstallRoute.RemovePackage
        ? [UninstallStep.RemoveAutostart, UninstallStep.DeleteDataNow, UninstallStep.RemovePackage]
        : FoldersToDelete.Count > 0
            ? [UninstallStep.RemoveAutostart, UninstallStep.StartDataPurge, UninstallStep.OpenInstalledApps]
            : [UninstallStep.RemoveAutostart, UninstallStep.OpenInstalledApps];

    /// <summary>Windows Settings › Apps › Installed apps.</summary>
    public static readonly Uri InstalledApps = new("ms-settings:appsfeatures");

    /// <summary>The calling package's own page in Installed apps, with its Uninstall button.</summary>
    public static readonly Uri OwnAppPage = new("ms-settings:appsfeatures-app");

    /// <summary>
    /// The plan for this install.
    /// </summary>
    /// <param name="packaged">Whether the process runs with package identity.</param>
    /// <param name="deleteData">The user ticked "also delete my widgets, settings and accounts" (loose builds; packaged, the data always goes).</param>
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
            deleteData || packaged ? DataFolders(roamingRoot, localRoot, folderName) : [],
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
