using System.Runtime.InteropServices;

namespace Themia.App.Interop;

/// <summary>
/// [S-1011] Terminal Services session notifications: how a window hears the session being unlocked.
/// <c>WM_WTSSESSION_CHANGE</c> arrives at a window registered here, with the change in wParam.
/// </summary>
internal static partial class SessionNotifications
{
    public const uint WmWtsSessionChange = 0x02B1;

    /// <summary><c>WTS_SESSION_UNLOCK</c>. Lock, logon, logoff and the remote changes are other values.</summary>
    public const int SessionUnlock = 0x8;

    /// <summary><c>NOTIFY_FOR_THIS_SESSION</c>: only the session this process runs in.</summary>
    public const uint ForThisSession = 0;

    /// <summary>Whether a session change message is the unlock, the one change that replays the intro.</summary>
    public static bool IsUnlock(uint msg, nint wParam) => msg == WmWtsSessionChange && (int)wParam == SessionUnlock;

    [LibraryImport("wtsapi32.dll", EntryPoint = "WTSRegisterSessionNotification", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool Register(nint hwnd, uint flags);

    [LibraryImport("wtsapi32.dll", EntryPoint = "WTSUnRegisterSessionNotification")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool Unregister(nint hwnd);
}
