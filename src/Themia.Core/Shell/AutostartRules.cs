using Themia.Core.Localization;

namespace Themia.Core.Shell;

/// <summary>
/// How "start with Windows" is registered. Decided once per process by whether the app runs with a
/// package identity, never chosen by the user: an MSIX install cannot write a working Run entry for
/// itself, and an unpackaged one has no startup task to ask for.
/// </summary>
public enum AutostartMechanism
{
    /// <summary>Unpackaged: a value under <c>HKCU\...\CurrentVersion\Run</c>.</summary>
    RunKey = 0,

    /// <summary>Packaged (MSIX): the manifest's <c>windows.startupTask</c> extension.</summary>
    StartupTask = 1,
}

/// <summary>What Windows will do at the next sign-in.</summary>
public enum AutostartState
{
    /// <summary>Themia will not start.</summary>
    Off = 0,

    /// <summary>Themia will start.</summary>
    On = 1,

    /// <summary>
    /// Registered, but switched off in Windows (Settings › Apps › Startup, or Task Manager). Will not
    /// start. A packaged app is not allowed to switch this back on by itself.
    /// </summary>
    TurnedOffInWindows = 2,

    /// <summary>Switched off by policy. Will not start, and nothing the user does in Themia changes that.</summary>
    OffByPolicy = 3,

    /// <summary>Switched on by policy. Will start, and cannot be switched off from Themia.</summary>
    OnByPolicy = 4,

    /// <summary>The state could not be read at all.</summary>
    Unknown = 5,
}

/// <summary>Why the last change did not take, which picks the advice shown under the switch.</summary>
public enum AutostartProblem
{
    None = 0,

    /// <summary>The user switched Themia off as a startup app in Windows; only they can switch it back on there.</summary>
    TurnedOffInWindows = 1,

    /// <summary>A policy decides, in either direction.</summary>
    Policy = 2,

    /// <summary>Windows refused the write.</summary>
    AccessDenied = 3,

    /// <summary>Anything else; the detail carries the raw error.</summary>
    Failed = 4,
}

/// <summary>[S-935] The outcome of reading or changing autostart: the state reached, and why if it is not the one asked for.</summary>
/// <param name="State">What will happen at the next sign-in.</param>
/// <param name="Problem">Why the last change did not take, or <see cref="AutostartProblem.None"/>.</param>
/// <param name="Detail">The raw diagnostic, logged and shown in small print under the advice — never on its own.</param>
public sealed record AutostartStatus(AutostartState State, AutostartProblem Problem = AutostartProblem.None, string? Detail = null)
{
    /// <summary>Whether Themia will actually start at the next sign-in — what the switch shows.</summary>
    public bool WillStart => State is AutostartState.On or AutostartState.OnByPolicy;

    /// <summary>Whether the user can change it from Themia's switch at all.</summary>
    public bool IsLocked => State is AutostartState.OffByPolicy or AutostartState.OnByPolicy;
}

/// <summary>How this process was started, so a sign-in launch can stay quiet.</summary>
public enum LaunchSource
{
    /// <summary>The user started it: Start menu, a shortcut, a double-click.</summary>
    Manual = 0,

    /// <summary>Windows started it at sign-in, through Themia's own autostart registration.</summary>
    Autostart = 1,
}

/// <summary>
/// [S-933] Autostart, as pure rules: the Run entry's shape, what "enabled" means, the startup-task
/// states and the advice shown when a change does not take.
/// </summary>
public static class AutostartRules
{
    /// <summary>
    /// [S-934] The argument the registration launches with. A sign-in launch can tell itself apart
    /// from the user starting Themia by hand, and a hand-made startup entry without it is treated as
    /// a manual launch, as in the original.
    /// </summary>
    public const string Flag = "--autostart";

    /// <summary>The value name under the Run key (and under <c>StartupApproved\Run</c>).</summary>
    public const string RunValueName = "Themia";

    /// <summary>
    /// The MSIX manifest's startup task id. <c>Package.appxmanifest</c> declares exactly this
    /// (<c>desktop:StartupTask TaskId="ThemiaStartup" Enabled="false"</c>); a sign-in launch through it
    /// is recognised by its activation kind, not by an argument (see <see cref="SourceOf"/>).
    /// </summary>
    public const string StartupTaskId = "ThemiaStartup";

    public static AutostartMechanism MechanismFor(bool packaged)
        => packaged ? AutostartMechanism.StartupTask : AutostartMechanism.RunKey;

    /// <summary>The Run value Themia writes: the quoted executable, then <see cref="Flag"/>.</summary>
    public static string RunCommand(string executablePath) => $"\"{executablePath}\" {Flag}";

    /// <summary>
    /// [S-936] Whether a Run value launches this executable. A value left behind by a previous install
    /// location launches nothing, so it is not "on" — the switch would be lying.
    /// </summary>
    public static bool LaunchesThis(string? command, string executablePath)
    {
        if (string.IsNullOrWhiteSpace(command) || string.IsNullOrWhiteSpace(executablePath))
        {
            return false;
        }

        var text = command.Trim();
        string target;

        if (text.StartsWith('"'))
        {
            var close = text.IndexOf('"', 1);
            if (close < 0)
            {
                return false;
            }

            target = text[1..close];
        }
        else
        {
            // Unquoted: Windows reads up to the first space, so a path with spaces in it would run
            // something else — that is not this executable, whatever the rest of the string says.
            var space = text.IndexOf(' ');
            target = space < 0 ? text : text[..space];
        }

        return string.Equals(target.Trim(), executablePath.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// [S-936] Whether Windows' own startup-app switch (<c>Explorer\StartupApproved\Run</c>) lets the
    /// Run value start. No record is the default, which is allowed; otherwise the low bit of the first
    /// byte is "disabled" (02/06 on, 03/07 off).
    /// </summary>
    public static bool StartupApproved(byte[]? record) => record is not { Length: > 0 } || (record[0] & 1) == 0;

    /// <summary>[S-936] The Run-key state: the value must launch this executable, and Windows must not have it switched off.</summary>
    public static AutostartState FromRunKey(bool launchesThis, bool approved) => !launchesThis
        ? AutostartState.Off
        : approved ? AutostartState.On : AutostartState.TurnedOffInWindows;

    /// <summary>
    /// [S-937] A packaged startup task's state, from the WinRT <c>StartupTaskState</c> number
    /// (Disabled 0, DisabledByUser 1, Enabled 2, DisabledByPolicy 3, EnabledByPolicy 4). A value a
    /// later Windows adds reads as unknown, never as on.
    /// </summary>
    public static AutostartState FromStartupTask(int state) => state switch
    {
        0 => AutostartState.Off,
        1 => AutostartState.TurnedOffInWindows,
        2 => AutostartState.On,
        3 => AutostartState.OffByPolicy,
        4 => AutostartState.OnByPolicy,
        _ => AutostartState.Unknown,
    };

    /// <summary>
    /// [S-935] The outcome of asking for <paramref name="wanted"/> when Windows reports
    /// <paramref name="reached"/>: no problem when it took, otherwise the reason that picks the advice.
    /// </summary>
    public static AutostartStatus Outcome(bool wanted, AutostartState reached, string? detail = null)
    {
        var status = new AutostartStatus(reached);
        if (status.WillStart == wanted)
        {
            return status;
        }

        var problem = reached switch
        {
            AutostartState.TurnedOffInWindows => AutostartProblem.TurnedOffInWindows,
            AutostartState.OffByPolicy or AutostartState.OnByPolicy => AutostartProblem.Policy,
            _ => AutostartProblem.Failed,
        };

        return status with { Problem = problem, Detail = detail };
    }

    /// <summary>
    /// [S-935] What to tell the user when a change did not take: what to <i>do</i>, not just that it
    /// failed. Null when there is nothing to say. The raw detail goes under this, never instead of it.
    /// </summary>
    public static string? Advice(AutostartProblem problem) => Advice(Strings.Current, problem);

    /// <summary><see cref="Advice(AutostartProblem)"/> in <paramref name="locale"/>.</summary>
    public static string? Advice(Locale locale, AutostartProblem problem) => problem switch
    {
        AutostartProblem.TurnedOffInWindows => Strings.Get(locale, Str.Shell_Autostart_TurnedOffInWindows),
        AutostartProblem.Policy => Strings.Get(locale, Str.Shell_Autostart_Policy),
        AutostartProblem.AccessDenied => Strings.Get(locale, Str.Shell_Autostart_AccessDenied),
        AutostartProblem.Failed => Strings.Get(locale, Str.Shell_Autostart_Failed),
        _ => null,
    };

    /// <summary>
    /// [S-934] How this process was started: the <see cref="Flag"/> argument (the Run entry, or a
    /// hand-made entry that copied it), or a packaged startup-task activation.
    /// </summary>
    public static LaunchSource SourceOf(IReadOnlyList<string> arguments, bool startupTaskActivation)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        if (startupTaskActivation)
        {
            return LaunchSource.Autostart;
        }

        // Index 0 is the executable itself; a path that happens to end in the flag is not the flag.
        for (var i = 1; i < arguments.Count; i++)
        {
            if (string.Equals(arguments[i], Flag, StringComparison.OrdinalIgnoreCase))
            {
                return LaunchSource.Autostart;
            }
        }

        return LaunchSource.Manual;
    }
}
