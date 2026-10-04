namespace Themia.Core.Shell;

/// <summary>
/// [S-930] The desktop-icons switch, as pure rules.
/// <para>
/// Windows owns this setting, not Themia: the shell's own command flips it and persists it, and the
/// user can flip it back from the desktop's context menu at any time. So the stored preference is
/// only a first-paint cache for the settings switch, and the only write Themia ever makes is the one
/// the user asks for by moving that switch.
/// </para>
/// </summary>
public static class DesktopIconRules
{
    /// <summary>
    /// [S-931] Whether to post the shell's toggle command. It has no "set" form, so it may only be
    /// posted when the live state is known and differs; posting on an unreadable state has an even
    /// chance of hiding the icons of a user who just asked to show them.
    /// </summary>
    public static bool NeedsToggle(bool? live, bool wanted) => live is bool current && current != wanted;

    /// <summary>
    /// [S-932] What to write into the cached preference after reading the live state: the live value
    /// when it is known and disagrees, otherwise null for "leave it alone" — an unreadable shell must
    /// not clobber the cache, and an agreeing one must not cause a save.
    /// </summary>
    public static bool? Adopt(bool? live, bool stored) => live is bool current && current != stored ? current : null;
}
