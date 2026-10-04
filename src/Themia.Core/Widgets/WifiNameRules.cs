
using Themia.Core.Localization;

namespace Themia.Core.Widgets;

/// <summary>
/// [S-1611] Where Windows' location permission stands for this app, as far as the Wi-Fi network name
/// is concerned.
/// </summary>
public enum LocationPermission
{
    /// <summary>
    /// Nothing to ask: the unpackaged build (Windows decides on its own, [S-1612]) or a Windows too old
    /// to have per-app location consent for desktop apps.
    /// </summary>
    NotRequired = 0,

    /// <summary>The user allowed it.</summary>
    Allowed = 1,

    /// <summary>Windows has not asked the user yet; asking shows its consent prompt.</summary>
    PromptRequired = 2,

    /// <summary>Refused: by the user, by the device's policy, or location is off for the whole PC.</summary>
    Denied = 3,
}

/// <summary>[S-1613] What clicking the "Allow location" hint does.</summary>
public enum WifiHintAction
{
    /// <summary>No hint.</summary>
    None = 0,

    /// <summary>Show Windows' consent prompt (Windows has not asked yet, and this run has not either).</summary>
    AskWindows = 1,

    /// <summary>Open Settings › Privacy &amp; security › Location (refused, or the prompt was already shown).</summary>
    OpenSettings = 2,
}

/// <summary>What the network tile's Wi-Fi line does about a missing network name.</summary>
/// <param name="Action">What the hint's link does; <see cref="WifiHintAction.None"/> for no hint.</param>
public readonly record struct WifiNameDecision(WifiHintAction Action)
{
    public static WifiNameDecision None { get; } = new(WifiHintAction.None);

    /// <summary>Whether the line gets the hint.</summary>
    public bool Hint => Action != WifiHintAction.None;
}

/// <summary>
/// [S-1611]..[S-1613] The Wi-Fi network name and location consent. Windows treats the name of the
/// network a PC is joined to as location data (it can be looked up to a place), so reading it needs
/// the user's location permission. Kept out of the WinRT code so the decision can be tested without a
/// package identity or a Wi-Fi adapter.
/// </summary>
public static class WifiNameRules
{
    /// <summary>[S-1613] The hint's text, a link.</summary>
    public static string Hint => Str.WidgetsB_Network_WifiHint.Text();

    /// <summary>[S-1613] Settings › Privacy &amp; security › Location.</summary>
    public static readonly Uri PrivacyPage = new("ms-settings:privacy-location");

    /// <summary>
    /// [S-1611] What a Wi-Fi line that is about to be drawn shows about its name.
    /// <para>
    /// Consent is asked for only by the packaged build, only from a Wi-Fi line on screen, and only
    /// when the user clicks its hint: the prompt never appears by itself — not at startup, not
    /// because a tile happened to grow. Windows' prompt is shown at most once per run; after that, and
    /// when refused, the hint opens the privacy page. A refusal is a quiet hint, not an error, and the
    /// line still shows the signal and the link speed.
    /// </para>
    /// <para>
    /// [S-1612] The unpackaged build is left exactly as it was: no prompt, no hint.
    /// </para>
    /// </summary>
    /// <param name="packaged">The process has an MSIX package identity.</param>
    /// <param name="permission">The permission as Windows reports it now.</param>
    /// <param name="lineShown">A Wi-Fi connection line is on screen (wireless adapter, detail line fits).</param>
    /// <param name="asked">This run already showed Windows' prompt once.</param>
    /// <param name="hasName">The Wi-Fi API returned a network name.</param>
    /// <param name="nameRefused">The Wi-Fi API refused the query (ERROR_ACCESS_DENIED).</param>
    public static WifiNameDecision Decide(bool packaged, LocationPermission permission, bool lineShown, bool asked, bool hasName, bool nameRefused)
    {
        if (!packaged || !lineShown || hasName)
        {
            return WifiNameDecision.None;
        }

        return permission switch
        {
            LocationPermission.PromptRequired when !asked => new WifiNameDecision(WifiHintAction.AskWindows),

            // Asked and the prompt was closed without an answer: the setting is where to say yes now.
            LocationPermission.PromptRequired or LocationPermission.Denied => new WifiNameDecision(WifiHintAction.OpenSettings),

            // Allowed, yet the Wi-Fi API still said no (location turned off for the whole PC between
            // two checks): the page is still the place to fix it. A hidden network or no connection
            // is not refused, and gets no hint.
            _ => nameRefused ? new WifiNameDecision(WifiHintAction.OpenSettings) : WifiNameDecision.None,
        };
    }
}
