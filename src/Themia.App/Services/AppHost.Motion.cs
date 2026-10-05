using Themia.Core.Motion;

namespace Themia.App.Services;

/// <summary>[S-1011]/[S-1014] The app's side of the intros: the unlock replay and the settings preview.</summary>
internal sealed partial class AppHost
{
    /// <summary>Listen for the session unlock. Before the command window is created, like the display events.</summary>
    private void StartMotion() => _command.SessionUnlocked += () => ReplayIntro(IntroMode.Unlock);

    /// <summary>[S-1014] Replay <paramref name="mode"/>'s intro on every display: the ▶ buttons, and a change of style or length.</summary>
    public void ReplayIntro(IntroMode mode)
    {
        foreach (var window in _windows.ToList())
        {
            window.ReplayIntro(mode);
        }
    }
}
