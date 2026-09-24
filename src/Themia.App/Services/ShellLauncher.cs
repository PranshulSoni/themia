using System.Diagnostics;
using Themia.Core.Diagnostics;
using Themia.Core.Widgets;

namespace Themia.App.Services;

/// <summary>What happened when something was asked to open.</summary>
public enum LaunchOutcome
{
    /// <summary>Handed to the shell.</summary>
    Opened = 0,

    /// <summary><see cref="LaunchTargets"/> would not have it.</summary>
    Refused = 1,

    /// <summary>A well-formed local path that is not there any more.</summary>
    Missing = 2,

    /// <summary>The shell was asked and said no: no association, a cancelled elevation prompt, a dead share.</summary>
    Failed = 3,
}

/// <summary>
/// The one place in this app that opens anything.
/// <para>
/// The launcher widgets — apps, shortcuts, search, and the clipboard's copy-back — all come through
/// here, and that is the entire security design. A widget layer that sits under every window and
/// opens whatever a config file names is a persistence and execution primitive; the only way that
/// is defensible is if there is exactly one function deciding what may be opened, one rule it
/// enforces, and no second path anywhere that skips it.
/// </para>
/// <para>
/// Two properties hold on every path out of this file:
/// </para>
/// <list type="bullet">
/// <item>
/// <b>Nothing is ever a command line.</b> The target goes into <see cref="ProcessStartInfo.FileName"/>
/// with <see cref="ProcessStartInfo.UseShellExecute"/> set, and <see cref="ProcessStartInfo.Arguments"/>
/// is never touched. There is no string being concatenated, so there is nothing for a quote, an
/// ampersand or a caret in a config file to escape out of.
/// </item>
/// <item>
/// <b>Nothing is opened that was not classified.</b> <see cref="LaunchTargets.Classify"/> is a pure
/// allow list of three shapes — an http/https URL, a fully qualified local or UNC path, and a
/// <c>shell:</c> name — and it runs on the literal string that is about to be launched, not on an
/// earlier version of it.
/// </item>
/// </list>
/// <para>
/// A local path is also checked for <i>existence</i> here, which the pure rule cannot do. That is
/// not security — a path that exists can still be anything — it is honesty: a broken shortcut says
/// so in the log instead of producing a shell error box over the desktop.
/// </para>
/// </summary>
internal static class ShellLauncher
{
    private const string Scope = "launcher";

    /// <summary>
    /// Open a target, or say why not. Never throws: this is called from a click handler on a
    /// surface that must stay up.
    /// </summary>
    public static LaunchOutcome Open(string? target)
    {
        var classified = LaunchTargets.Classify(target);

        if (!classified.IsValid)
        {
            // Loud, because a refusal is either a typo the user needs to see or a config that has
            // been edited by something other than the user.
            Log.Warn(Scope, $"refused to open '{Excerpt(target)}': {classified.Refusal}");
            return LaunchOutcome.Refused;
        }

        if (classified.Kind == LaunchKind.LocalPath && !Exists(classified.Value))
        {
            Log.Warn(Scope, $"'{classified.Value}' is not there any more");
            return LaunchOutcome.Missing;
        }

        return Launch(classified.Value);
    }

    /// <summary>
    /// Open the folder a file lives in, for the "show me where this is" action. Goes through the
    /// same classification as everything else — the parent of a validated path is still a path from
    /// a config file.
    /// </summary>
    public static LaunchOutcome OpenContainingFolder(string? target)
    {
        var classified = LaunchTargets.Classify(target);

        if (!classified.IsValid || classified.Kind != LaunchKind.LocalPath)
        {
            Log.Warn(Scope, $"refused to reveal '{Excerpt(target)}': {classified.Refusal}");
            return LaunchOutcome.Refused;
        }

        string? parent;

        try
        {
            parent = Directory.Exists(classified.Value)
                ? classified.Value
                : Path.GetDirectoryName(classified.Value);
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException)
        {
            Log.Warn(Scope, $"could not work out the folder holding '{classified.Value}'", ex);
            return LaunchOutcome.Refused;
        }

        // Re-classified rather than trusted: GetDirectoryName is string surgery, and its result has
        // not been through the rule.
        return Open(parent);
    }

    /// <summary>
    /// The shell call itself, and the only one in the app.
    /// </summary>
    private static LaunchOutcome Launch(string value)
    {
        try
        {
            // using: ShellExecute hands back a process handle even when nothing waits on it, and a
            // surface that lives for weeks leaks one per click without this.
            //
            // No Arguments, ever. UseShellExecute with a bare FileName is ShellExecuteW's "open"
            // verb on one string; the moment an argument string exists, so does quoting, and so
            // does a way out of it.
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = value,
                UseShellExecute = true,
            });

            return LaunchOutcome.Opened;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception
            or InvalidOperationException
            or IOException
            or ObjectDisposedException)
        {
            // No association, a cancelled elevation prompt, an unreachable share. The desktop stays
            // up and the log says why nothing opened.
            Log.Warn(Scope, $"the shell would not open '{value}'", ex);
            return LaunchOutcome.Failed;
        }
    }

    /// <summary>
    /// Whether a validated path names something that is there. Answers false rather than throwing:
    /// an unreachable network share throws from both of these.
    /// </summary>
    private static bool Exists(string path)
    {
        try
        {
            return File.Exists(path) || Directory.Exists(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Debug(Scope, $"could not tell whether '{path}' exists: {ex.Message}");

            // Unknown, not absent. Let the shell be the one to fail, rather than refusing something
            // that is merely slow to answer.
            return true;
        }
    }

    /// <summary>
    /// A refused target, short enough to log. The whole of a rejected string is exactly the thing
    /// not worth pasting into a log file at full length.
    /// </summary>
    private static string Excerpt(string? raw)
    {
        var value = (raw ?? string.Empty).Trim();

        if (value.Length <= 120)
        {
            return value.ReplaceLineEndings(" ");
        }

        return string.Concat(value.AsSpan(0, 120).ToString().ReplaceLineEndings(" "), "…");
    }
}
