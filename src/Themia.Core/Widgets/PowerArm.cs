using Themia.Core.Model;

namespace Themia.Core.Widgets;

/// <summary>What a press on a power tile did.</summary>
public enum PowerPress
{
    /// <summary>Run the action now.</summary>
    Run = 0,

    /// <summary>The tile is now armed and shows "Confirm"; nothing ran.</summary>
    Armed = 1,

    /// <summary>
    /// The press came too soon after arming to be a second, deliberate click (a double-click is one
    /// intent, not two). Still armed; nothing ran.
    /// </summary>
    Ignored = 2,
}

/// <summary>
/// [S-1245][S-1247] The confirm step of the power tile: the first press on a session-ending action
/// arms its tile, a second press on the same tile runs it.
/// <para>
/// Safety lives here, not in the view's timer. An armed tile is only armed for
/// <see cref="Window"/> after it was armed, whatever the disarm timer did; a second press sooner
/// than <see cref="MinimumDelay"/> is ignored, so an accidental double-click cannot shut the PC
/// down; one tile is armed at a time, and arming another disarms the first.
/// </para>
/// </summary>
public sealed class PowerArm
{
    /// <summary>[S-1247] How long an armed tile stays armed: time to read "Confirm" and click, no longer.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(5);

    /// <summary>[S-1247] A second press sooner than this after arming is the tail of a double-click.</summary>
    public static readonly TimeSpan MinimumDelay = TimeSpan.FromMilliseconds(400);

    private DateTimeOffset _armedAt;

    /// <summary>The tile awaiting its second press, or null.</summary>
    public PowerAction? Armed { get; private set; }

    /// <summary>Whether <paramref name="action"/> is armed at <paramref name="now"/>.</summary>
    public bool IsArmed(PowerAction action, DateTimeOffset now)
        => Armed == action && Live(now);

    /// <summary>A press on <paramref name="action"/>'s tile at <paramref name="now"/>.</summary>
    public PowerPress Press(PowerAction action, bool confirmEnabled, DateTimeOffset now)
    {
        if (!PowerActionRules.NeedsConfirm(action, confirmEnabled))
        {
            Disarm();
            return PowerPress.Run;
        }

        if (Armed == action && Live(now))
        {
            if (now - _armedAt < MinimumDelay)
            {
                return PowerPress.Ignored;
            }

            Disarm();
            return PowerPress.Run;
        }

        Armed = action;
        _armedAt = now;
        return PowerPress.Armed;
    }

    /// <summary>[S-1247] Pointer left the widget, the timer ran out, or the widget went away.</summary>
    public void Disarm() => Armed = null;

    /// <summary>
    /// Disarm if the window has passed. Returns true when that changed anything, so the view only
    /// repaints when there is something to repaint.
    /// </summary>
    public bool Expire(DateTimeOffset now)
    {
        if (Armed is null || Live(now))
        {
            return false;
        }

        Disarm();
        return true;
    }

    /// <summary>
    /// Inside the window. A clock that went backwards (a time-zone or NTP jump) is outside it: the
    /// safe reading of "I cannot tell how long ago" is "too long".
    /// </summary>
    private bool Live(DateTimeOffset now) => now >= _armedAt && now - _armedAt < Window;
}
