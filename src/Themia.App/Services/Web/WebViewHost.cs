using Microsoft.Web.WebView2.Core;
using Themia.Core.Diagnostics;

namespace Themia.App.Services.Web;

/// <summary>
/// The one way this app starts a browser engine, used by the web element's off-screen renderer and
/// by its element picker.
/// <para>
/// One user-data folder and one set of options, so the two share a browser process whenever both
/// are open, and so a cookie banner dismissed in the picker stays dismissed when the renderer loads
/// the same page. WebView2 refuses a second environment on the same folder with different options,
/// which is the other reason there is exactly one factory.
/// </para>
/// <para>
/// No environment is held between uses. A browser process lives for as long as one of its views
/// does; both callers close theirs as soon as they are done, so a desktop showing a pinned element
/// runs no browser at all between refreshes.
/// </para>
/// </summary>
internal static class WebViewHost
{
    private const string Scope = "webview";

    /// <summary>
    /// The renderer's window is shown off-screen, and Chromium would otherwise treat a window
    /// nobody can see as occluded and stop painting and timing it — the page would never settle and
    /// the screenshot would never come. These keep it rendering as if on screen.
    /// </summary>
    private const string BrowserArguments =
        "--disable-features=CalculateNativeWinOcclusion " +
        "--disable-background-timer-throttling " +
        "--disable-renderer-backgrounding " +
        "--disable-backgrounding-occluded-windows";

    /// <summary>
    /// Where the browser keeps its profile: cookies, cache. Local, not roaming — a browser cache has
    /// no business following the user between machines — and beside, not inside, the state file's
    /// folder.
    /// </summary>
    public static string UserDataFolder { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Themia.WinUI",
        "WebView2");

    /// <summary>
    /// Create an environment, or null when the WebView2 runtime is missing or refuses. Logged once
    /// per failure; the caller shows the reason.
    /// </summary>
    public static async Task<CoreWebView2Environment?> CreateEnvironmentAsync()
    {
        try
        {
            Directory.CreateDirectory(UserDataFolder);

            var options = new CoreWebView2EnvironmentOptions
            {
                AdditionalBrowserArguments = BrowserArguments,
                AreBrowserExtensionsEnabled = false,
            };

            return await CoreWebView2Environment.CreateWithOptionsAsync(string.Empty, UserDataFolder, options);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException
            or IOException
            or UnauthorizedAccessException
            or ArgumentException
            or InvalidOperationException)
        {
            Log.Error(Scope, "could not start WebView2", ex);
            return null;
        }
    }

    /// <summary>
    /// Take away everything a desktop widget's browser has no use for. The page is arbitrary
    /// third-party code: it gets no host objects, no dev tools, no dialogs, no autofill, no popups.
    /// </summary>
    public static void LockDown(CoreWebView2 core)
    {
        ArgumentNullException.ThrowIfNull(core);

        var settings = core.Settings;
        settings.AreDevToolsEnabled = false;
        settings.AreHostObjectsAllowed = false;
        settings.AreDefaultContextMenusEnabled = false;
        settings.AreDefaultScriptDialogsEnabled = false;
        settings.IsStatusBarEnabled = false;
        settings.IsZoomControlEnabled = false;
        settings.IsGeneralAutofillEnabled = false;
        settings.IsPasswordAutosaveEnabled = false;
        settings.AreBrowserAcceleratorKeysEnabled = false;

        core.IsMuted = true;

        // A download started by a page nobody is looking at is never wanted.
        core.DownloadStarting += (_, args) => args.Cancel = true;

        // Camera, microphone, location, notifications: refused without asking.
        core.PermissionRequested += (_, args) => args.State = CoreWebView2PermissionState.Deny;

        // alert/confirm/prompt: with the default dialogs off and nothing accepting them, WebView2
        // answers each one as cancelled, which is what a page nobody is looking at should get.
    }
}
