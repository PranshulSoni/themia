using Themia.Core.Diagnostics;
using Themia.Core.Motion;

namespace Themia.App.Services;

/// <summary>
/// [S-1009]/[S-1011]/[S-1014] The app's side of the intros: the unlock replay, the settings preview,
/// and the startup sound, which is the app's to play — once, not once per display.
/// </summary>
internal sealed partial class AppHost
{
    /// <summary>
    /// [S-1009] The startup sound is owed: set at launch and by a Startup preview, spent by the first
    /// surface that reports a startup moment, so a desktop on three displays plays it once.
    /// </summary>
    private bool _startupSoundOwed = true;

    /// <summary>Listen for the session unlock. Before the command window is created, like the display events.</summary>
    private void StartMotion() => _command.SessionUnlocked += () => ReplayIntro(IntroMode.Unlock);

    /// <summary>
    /// [S-1014] Replay <paramref name="mode"/>'s intro on every display: the ▶ buttons, and a change of
    /// style, length or the sound switch. Only a Startup replay carries the sound.
    /// </summary>
    public void ReplayIntro(IntroMode mode)
    {
        _startupSoundOwed = mode == IntroMode.Startup;

        foreach (var window in _windows.ToList())
        {
            window.ReplayIntro(mode);
        }

        // A preview every surface refused (all still playing) owes nothing to a later surface.
        _startupSoundOwed = false;
    }

    /// <summary>
    /// [S-1009] A surface reached an armed moment. The first startup moment spends the sound, and plays
    /// it when the intro really plays and the user opted in.
    /// </summary>
    public void OnIntro(IntroMode mode, bool playing)
    {
        if (mode != IntroMode.Startup || !_startupSoundOwed)
        {
            return;
        }

        _startupSoundOwed = false;

        if (!playing || !State.Design.StartupSound)
        {
            return;
        }

        var plan = StartupSound.Plan(State.Design.IntroAnimation, IntroMotion.MsFor(State.Design, IntroMode.Startup));

        // Rendered off the UI thread: the warp is four seconds of samples, and the intro is already
        // running on the compositor while they are computed.
        _ = Task.Run(() =>
        {
            try
            {
                CuePlayer.Play(plan.RenderWave(1));
            }
            catch (Exception ex)
            {
                Log.Error(Scope, "the startup sound failed", ex);
            }
        });
    }
}
