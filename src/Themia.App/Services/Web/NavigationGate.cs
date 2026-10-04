namespace Themia.App.Services.Web;

/// <summary>
/// [S-1314] The request-forgery guard for a browser that runs a stranger's page: every main-frame
/// navigation of the web element's renderer and picker goes through <see cref="NetGuard"/> first.
/// <para>
/// WebView2's <c>NavigationStarting</c> cannot wait for a DNS lookup, so a navigation to a host this
/// gate has not cleared yet is cancelled, checked, and — when allowed — started again; one already
/// cleared goes straight through. Cleared means the whole origin (scheme, host, port) for this
/// gate's lifetime, which is one render or one picker window. Frames inside the page are not
/// checked, as the original's <c>check_url</c> never checked them either.
/// </para>
/// <para>
/// Only http and https pages are opened at all: a <c>data:</c> or <c>file:</c> document in the main
/// frame is refused outright rather than run.
/// </para>
/// </summary>
internal sealed class NavigationGate
{
    private readonly HashSet<string> _cleared = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Read a navigation's address. False for anything but an absolute http or https address,
    /// which is refused without a lookup.
    /// </summary>
    public static bool TryTarget(string? address, out Uri target)
    {
        if (Uri.TryCreate(address, UriKind.Absolute, out var parsed)
            && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps))
        {
            target = parsed;
            return true;
        }

        target = null!;
        return false;
    }

    /// <summary>Whether this origin has already been checked and allowed, so the navigation can go ahead now.</summary>
    public bool IsCleared(Uri target) => _cleared.Contains(Origin(target));

    /// <summary>Check an address with <see cref="NetGuard"/>; an allowed one is cleared for the rest of this gate's life.</summary>
    public async Task<GuardResult> CheckAsync(Uri target, CancellationToken token)
    {
        var verdict = await NetGuard.CheckAsync(target, token).ConfigureAwait(true);

        if (verdict.Allowed)
        {
            _cleared.Add(Origin(target));
        }

        return verdict;
    }

    private static string Origin(Uri target) => target.GetLeftPart(UriPartial.Authority);
}
