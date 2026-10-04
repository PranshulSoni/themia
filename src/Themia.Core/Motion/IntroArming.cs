namespace Themia.Core.Motion;

/// <summary>
/// [S-1001] Intro plays are armed one-shots, per display surface. A layout pass animates only when a
/// sanctioned moment armed it: the surface's first real layout (startup) or a change of the view the
/// display shows (transition). Everything else that rebuilds cards — a rename, a widget added, pasted
/// or restored, a settings change, a game-mode resume — lays out silently.
/// <para>
/// Replays (the settings preview and the session unlock) do not go through here: they re-run the
/// cards already on screen, and are refused while a show is still playing ([S-1012]).
/// </para>
/// </summary>
public sealed class IntroArming
{
    private bool _startupPending = true;
    private string? _shownViewId;

    /// <summary>
    /// The intro a layout pass of <paramref name="viewId"/> plays, or null for a silent pass. Called
    /// once per pass that actually laid cards out (the surface is loaded and has a usable size), so
    /// the startup arm is not spent on a pass nobody could see.
    /// </summary>
    public IntroMode? OnLayout(string viewId)
    {
        if (_startupPending)
        {
            _startupPending = false;
            _shownViewId = viewId;
            return IntroMode.Startup;
        }

        var changed = _shownViewId is not null && !string.Equals(_shownViewId, viewId, StringComparison.Ordinal);
        _shownViewId = viewId;
        return changed ? IntroMode.Transition : null;
    }
}
