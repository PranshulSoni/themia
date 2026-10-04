using Themia.Core.Model;

namespace Themia.Core.Widgets;

/// <summary>Which Win32 call carries out an action.</summary>
public enum PowerCallKind
{
    /// <summary><c>ExitWindowsEx</c>: shut down, restart, sign out.</summary>
    ExitWindows = 0,

    /// <summary><c>SetSuspendState</c>: sleep, hibernate. Blocks until the machine resumes.</summary>
    Suspend = 1,

    /// <summary><c>LockWorkStation</c>.</summary>
    Lock = 2,

    /// <summary>A <c>WM_SYSCOMMAND</c>/<c>SC_MONITORPOWER</c> broadcast.</summary>
    MonitorOff = 3,
}

/// <summary>
/// [S-1243] Exactly what the backend does for one action, decided here so it can be tested without
/// shutting anything down. The Win32 seam only executes a plan.
/// </summary>
/// <param name="Kind">Which call.</param>
/// <param name="ExitFlags"><c>EWX_*</c> flags for <see cref="PowerCallKind.ExitWindows"/>, else 0.</param>
/// <param name="Hibernate">For <see cref="PowerCallKind.Suspend"/>: hibernate rather than sleep.</param>
/// <param name="NeedsShutdownPrivilege">Enable <c>SeShutdownPrivilege</c> on the process token first.</param>
public readonly record struct PowerCall(PowerCallKind Kind, uint ExitFlags, bool Hibernate, bool NeedsShutdownPrivilege);

/// <summary>Why an action did not happen. The machine is still up, so the tile has to say.</summary>
public enum PowerFailure
{
    None = 0,

    /// <summary><c>AdjustTokenPrivileges</c> assigned nothing (<c>ERROR_NOT_ALL_ASSIGNED</c>): policy strips the privilege.</summary>
    NotAllowed = 1,

    /// <summary><c>SetSuspendState(TRUE, …)</c> refused: hibernation is off on this machine.</summary>
    HibernateUnavailable = 2,

    /// <summary><c>SetSuspendState(FALSE, …)</c> refused.</summary>
    SleepRefused = 3,

    /// <summary>Any other call failed; the Win32 error says which way.</summary>
    Win32 = 4,
}

/// <summary>The result of one action: success, or a named failure with the Win32 error behind it.</summary>
public readonly record struct PowerOutcome(PowerFailure Failure, int Win32Error)
{
    public static PowerOutcome Ok { get; } = new(PowerFailure.None, 0);

    public bool Succeeded => Failure == PowerFailure.None;

    public static PowerOutcome Error(int win32Error) => new(PowerFailure.Win32, win32Error);
}

/// <summary>
/// The power tile's rules: which actions exist and in what default order, which need a second click,
/// what each one calls, and how a failure reads. Pure, so the decisions are tested here and the
/// Win32 seam stays a few lines of calls.
/// </summary>
public static class PowerActionRules
{
    // EWX_* and SHTDN_REASON_* (winuser.h / reason.h).
    public const uint ExitLogoff = 0x0;
    public const uint ExitPowerOff = 0x8;
    public const uint ExitReboot = 0x2;
    public const uint ExitForceIfHung = 0x10;

    /// <summary>
    /// <c>SHTDN_REASON_MAJOR_OTHER | SHTDN_REASON_MINOR_OTHER | SHTDN_REASON_FLAG_PLANNED</c>: the
    /// event log records a user-initiated, planned shutdown rather than an unexpected one.
    /// </summary>
    public const uint PlannedReason = 0x80000000;

    /// <summary><c>ERROR_ACCESS_DENIED</c>.</summary>
    public const int AccessDenied = 5;

    /// <summary><c>ERROR_NOT_ALL_ASSIGNED</c>: <c>AdjustTokenPrivileges</c> "succeeded" and assigned nothing.</summary>
    public const int NotAllAssigned = 1300;

    /// <summary><c>ERROR_PRIVILEGE_NOT_HELD</c>.</summary>
    public const int PrivilegeNotHeld = 1314;

    /// <summary><c>ERROR_SHUTDOWN_IN_PROGRESS</c>.</summary>
    public const int ShutdownInProgress = 1115;

    /// <summary>Every action, in the order the settings page lists the ones not yet chosen.</summary>
    public static IReadOnlyList<PowerAction> All { get; } =
    [
        PowerAction.Shutdown,
        PowerAction.Restart,
        PowerAction.Sleep,
        PowerAction.Lock,
        PowerAction.Hibernate,
        PowerAction.SignOut,
        PowerAction.ScreenOff,
    ];

    /// <summary>[S-1242] The original's default four: shut down, restart, sleep, lock.</summary>
    public static IReadOnlyList<PowerAction> Defaults { get; } =
        [PowerAction.Shutdown, PowerAction.Restart, PowerAction.Sleep, PowerAction.Lock];

    /// <summary>
    /// [S-1241] The list as drawn: defined values only, first occurrence wins, order kept. A null
    /// list is no buttons (the placeholder), not the defaults — the defaults are a new widget's.
    /// </summary>
    public static IReadOnlyList<PowerAction> Visible(IEnumerable<PowerAction>? raw)
    {
        var shown = new List<PowerAction>();

        if (raw is null)
        {
            return shown;
        }

        foreach (var action in raw)
        {
            if (Enum.IsDefined(action) && !shown.Contains(action))
            {
                shown.Add(action);
            }
        }

        return shown;
    }

    /// <summary>
    /// [S-1245] The actions that end the session. They close every app, so unsaved work can be lost.
    /// Sleep, hibernate, lock and display-off come back to the same desktop: a mis-click costs a
    /// keypress, so they never arm.
    /// </summary>
    public static bool EndsSession(PowerAction action)
        => action is PowerAction.Shutdown or PowerAction.Restart or PowerAction.SignOut;

    /// <summary>[S-1245] Whether a press arms the tile rather than running the action.</summary>
    public static bool NeedsConfirm(PowerAction action, bool confirmEnabled)
        => confirmEnabled && EndsSession(action);

    /// <summary>[S-1243] The call behind an action. Mirrors the original backend exactly.</summary>
    public static PowerCall Plan(PowerAction action) => action switch
    {
        // EWX_FORCEIFHUNG overrides only apps that stopped answering; a responsive app with unsaved
        // work still gets to block, as the Start menu's own entries do.
        PowerAction.Shutdown => new(PowerCallKind.ExitWindows, ExitPowerOff | ExitForceIfHung, false, true),
        PowerAction.Restart => new(PowerCallKind.ExitWindows, ExitReboot | ExitForceIfHung, false, true),

        // Signing out ends only this user's session: no privilege needed.
        PowerAction.SignOut => new(PowerCallKind.ExitWindows, ExitLogoff | ExitForceIfHung, false, false),
        PowerAction.Sleep => new(PowerCallKind.Suspend, 0, false, true),
        PowerAction.Hibernate => new(PowerCallKind.Suspend, 0, true, true),
        PowerAction.Lock => new(PowerCallKind.Lock, 0, false, false),
        PowerAction.ScreenOff => new(PowerCallKind.MonitorOff, 0, false, false),
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, "not a power action"),
    };

    /// <summary>
    /// [S-1243] What a privilege adjustment's last error means. <c>AdjustTokenPrivileges</c> returns
    /// success even when it assigned nothing, so a zero return is not enough: without this check the
    /// caller would go on to a shutdown that quietly does nothing.
    /// </summary>
    public static PowerOutcome PrivilegeOutcome(bool adjusted, int lastError)
    {
        if (!adjusted)
        {
            return lastError is NotAllAssigned or PrivilegeNotHeld
                ? new PowerOutcome(PowerFailure.NotAllowed, lastError)
                : PowerOutcome.Error(lastError);
        }

        return lastError is NotAllAssigned
            ? new PowerOutcome(PowerFailure.NotAllowed, lastError)
            : PowerOutcome.Ok;
    }

    /// <summary>[S-1243] What a <c>SetSuspendState</c> refusal means, by which way it was asked.</summary>
    public static PowerOutcome SuspendOutcome(bool succeeded, bool hibernate)
        => succeeded ? PowerOutcome.Ok
            : new PowerOutcome(hibernate ? PowerFailure.HibernateUnavailable : PowerFailure.SleepRefused, 0);

    /// <summary>The button's name.</summary>
    public static string Label(PowerAction action) => action switch
    {
        PowerAction.Shutdown => "Shut down",
        PowerAction.Restart => "Restart",
        PowerAction.Sleep => "Sleep",
        PowerAction.Hibernate => "Hibernate",
        PowerAction.Lock => "Lock",
        PowerAction.SignOut => "Sign out",
        PowerAction.ScreenOff => "Display off",
        _ => action.ToString(),
    };

    /// <summary>What the settings page says each action does.</summary>
    public static string Description(PowerAction action) => action switch
    {
        PowerAction.Shutdown => "Closes every app and turns the PC off",
        PowerAction.Restart => "Closes every app and starts Windows again",
        PowerAction.Sleep => "Low power; comes back to the same desktop",
        PowerAction.Hibernate => "Saves the session to disk and powers off",
        PowerAction.Lock => "Shows the lock screen; apps keep running",
        PowerAction.SignOut => "Closes your apps and ends your session",
        PowerAction.ScreenOff => "Turns the displays off; any input wakes them",
        _ => string.Empty,
    };

    /// <summary>The words an armed tile shows in place of its name.</summary>
    public const string ConfirmLabel = "Confirm";

    /// <summary>The tooltip of an armed tile.</summary>
    public static string ConfirmHint(PowerAction action) => $"Click again to {Verb(action)}";

    /// <summary>The verb used in sentences: "Could not <i>sleep</i>".</summary>
    public static string Verb(PowerAction action) => action switch
    {
        PowerAction.Shutdown => "shut down",
        PowerAction.Restart => "restart",
        PowerAction.Sleep => "sleep",
        PowerAction.Hibernate => "hibernate",
        PowerAction.Lock => "lock",
        PowerAction.SignOut => "sign out",
        PowerAction.ScreenOff => "turn off the display",
        _ => action.ToString(),
    };

    /// <summary>
    /// [S-1246] The tile's error line for a failed action: the action and the reason, in words.
    /// Null when it succeeded.
    /// </summary>
    public static string? FailureMessage(PowerAction action, PowerOutcome outcome)
    {
        var reason = outcome.Failure switch
        {
            PowerFailure.None => null,
            PowerFailure.NotAllowed => "this account is not allowed to",
            PowerFailure.HibernateUnavailable => "hibernation is turned off on this PC",
            PowerFailure.SleepRefused => "Windows refused to sleep",
            _ => outcome.Win32Error switch
            {
                AccessDenied => "access denied",
                PrivilegeNotHeld or NotAllAssigned => "this account is not allowed to",
                ShutdownInProgress => "Windows is already shutting down",
                0 => "Windows refused",
                var code => $"Windows error {code}",
            },
        };

        return reason is null ? null : $"Could not {Verb(action)}: {reason}";
    }

    /// <summary>
    /// [S-1242] Turn one action on or off in an ordered list. On appends to the end (the user then
    /// moves it); off removes it. Returns whether the list changed.
    /// </summary>
    public static bool Toggle(List<PowerAction> actions, PowerAction action, bool on)
    {
        ArgumentNullException.ThrowIfNull(actions);

        if (on)
        {
            if (actions.Contains(action))
            {
                return false;
            }

            actions.Add(action);
            return true;
        }

        return actions.RemoveAll(existing => existing == action) > 0;
    }

    /// <summary>
    /// [S-1242] The settings page's order: the chosen actions in their order, then the rest in
    /// <see cref="All"/>'s order, so every action has exactly one row.
    /// </summary>
    public static IReadOnlyList<PowerAction> SettingsOrder(IEnumerable<PowerAction>? chosen)
    {
        var order = new List<PowerAction>(Visible(chosen));

        foreach (var action in All)
        {
            if (!order.Contains(action))
            {
                order.Add(action);
            }
        }

        return order;
    }
}
