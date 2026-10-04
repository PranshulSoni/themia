namespace Themia.Core.Shell;

/// <summary>
/// [S-938] What a click on a widget must clean up after, as pure rules.
/// <para>
/// The widget surface never takes the foreground outside edit mode (<c>WS_EX_NOACTIVATE</c>), so a
/// click on a widget does not do what a click on any other window does: Start and Search stay open,
/// the desktop's context menu stays open, and the desktop's icon selection stays lit. Each of those
/// waits for a foreground change that never comes, so the surface has to ask for it explicitly.
/// </para>
/// </summary>
public static class ShellClickRules
{
    /// <summary>The processes that host Start and Search on Windows 10 and 11.</summary>
    private static readonly string[] StartHosts = ["startmenuexperiencehost.exe", "searchhost.exe", "searchapp.exe"];

    /// <summary>The window class every one of those hosts uses — and so does every UWP app.</summary>
    public const string CoreWindowClass = "Windows.UI.Core.CoreWindow";

    /// <summary>The classic tracked popup menu: always a transient menu, whoever owns it.</summary>
    public const string ClassicMenuClass = "#32768";

    /// <summary>
    /// The window classes the Windows 11 context menu is drawn in. Apps' own flyouts and tooltips use
    /// them too, so on their own they prove nothing; the owner must also be Explorer.
    /// </summary>
    private static readonly string[] ModernMenuClasses = ["Microsoft.UI.Content.PopupWindowSiteBridge", "Xaml_WindowedPopupClass"];

    /// <summary>
    /// [S-938] Whether the foreground is Start or Search: a <c>CoreWindow</c> owned by one of their
    /// hosts. The class alone would also match Settings or Calculator, which must not be sent Escape.
    /// </summary>
    public static bool IsStartOrSearch(string? foregroundClass, string? processPath)
        => foregroundClass == CoreWindowClass && ImageName(processPath) is { } name
            && Array.IndexOf(StartHosts, name) >= 0;

    /// <summary>[S-938] Whether a visible popup's class can be a shell context menu at all.</summary>
    public static bool IsMenuCandidate(string? windowClass)
        => windowClass == ClassicMenuClass || (windowClass is not null && Array.IndexOf(ModernMenuClasses, windowClass) >= 0);

    /// <summary>
    /// [S-938] Whether a candidate popup is a shell context menu: a classic menu always is (it is
    /// modal and only ever transient), a modern one only when Explorer owns it.
    /// </summary>
    public static bool IsShellMenu(string? windowClass, string? ownerProcessPath)
        => windowClass == ClassicMenuClass
            || (IsMenuCandidate(windowClass) && ImageName(ownerProcessPath) == "explorer.exe");

    /// <summary>The lower-case file name of a process image path, or null.</summary>
    public static string? ImageName(string? processPath)
    {
        if (string.IsNullOrWhiteSpace(processPath))
        {
            return null;
        }

        var slash = processPath.LastIndexOfAny(['\\', '/']);
        var name = slash >= 0 ? processPath[(slash + 1)..] : processPath;
        return name.Length == 0 ? null : name.ToLowerInvariant();
    }
}
