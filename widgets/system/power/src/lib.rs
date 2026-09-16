//! System power actions (shut down, restart, sleep, hibernate, lock, sign out,
//! turn off the display) for the Power widget.
//!
//! Every action is a Win32 call, not a `shutdown.exe` spawn: the CLI hop costs a
//! process, loses the error, and cannot express "sleep". The process token
//! handle is owned by a guard that closes it on every path (RAII), and no call
//! site uses `.unwrap()` — each fallible step maps into `Result<_, String>` so
//! the widget can surface the real reason instead of failing silently.

use serde::Deserialize;

/// The closed set of actions the widget can trigger. A string would let a typo
/// reach the backend and fail at runtime; as an enum an unknown value is a
/// deserialization error at the IPC boundary (CLAUDE.md: closed sets are enums).
#[derive(Debug, Clone, Copy, PartialEq, Eq, Deserialize)]
#[serde(rename_all = "camelCase")]
pub enum PowerAction {
    Shutdown,
    Restart,
    Sleep,
    Hibernate,
    Lock,
    SignOut,
    ScreenOff,
}

#[cfg(target_os = "windows")]
mod imp {
    use super::PowerAction;
    use windows::core::{w, PCWSTR};
    use windows::Win32::Foundation::{
        CloseHandle, GetLastError, BOOLEAN, ERROR_NOT_ALL_ASSIGNED, HANDLE, LPARAM, LUID, WPARAM,
    };
    use windows::Win32::Security::{
        AdjustTokenPrivileges, LookupPrivilegeValueW, LUID_AND_ATTRIBUTES, SE_PRIVILEGE_ENABLED,
        TOKEN_ADJUST_PRIVILEGES, TOKEN_PRIVILEGES, TOKEN_QUERY,
    };
    use windows::Win32::System::Power::SetSuspendState;
    use windows::Win32::System::Shutdown::{
        ExitWindowsEx, LockWorkStation, EXIT_WINDOWS_FLAGS, EWX_FORCEIFHUNG, EWX_LOGOFF,
        EWX_POWEROFF, EWX_REBOOT, SHTDN_REASON_FLAG_PLANNED, SHTDN_REASON_MAJOR_OTHER,
        SHTDN_REASON_MINOR_OTHER, SHUTDOWN_REASON,
    };
    use windows::Win32::System::Threading::{GetCurrentProcess, OpenProcessToken};
    use windows::Win32::UI::WindowsAndMessaging::{
        SendMessageTimeoutW, HWND_BROADCAST, SC_MONITORPOWER, SMTO_ABORTIFHUNG, WM_SYSCOMMAND,
    };

    /// Owns the process token returned by `OpenProcessToken` and closes it when
    /// it goes out of scope, including on the error paths below it.
    struct TokenHandle(HANDLE);

    impl Drop for TokenHandle {
        fn drop(&mut self) {
            // Only ever built from a successful OpenProcessToken, so the handle
            // is valid; a close failure here is unactionable.
            unsafe {
                let _ = CloseHandle(self.0);
            }
        }
    }

    /// Shutdown, restart, sleep and hibernate all require `SeShutdownPrivilege`,
    /// which is present-but-disabled in an interactive user's token. Enabling it
    /// is per-process and per-call cheap, so each action enables it rather than
    /// caching state that a token change could invalidate.
    fn enable_shutdown_privilege() -> Result<(), String> {
        unsafe {
            let mut raw = HANDLE::default();
            OpenProcessToken(
                GetCurrentProcess(),
                TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY,
                &mut raw,
            )
            .map_err(|e| format!("OpenProcessToken: {e}"))?;
            let token = TokenHandle(raw);

            let mut luid = LUID::default();
            LookupPrivilegeValueW(PCWSTR::null(), w!("SeShutdownPrivilege"), &mut luid)
                .map_err(|e| format!("LookupPrivilegeValue: {e}"))?;

            let privileges = TOKEN_PRIVILEGES {
                PrivilegeCount: 1,
                Privileges: [LUID_AND_ATTRIBUTES {
                    Luid: luid,
                    Attributes: SE_PRIVILEGE_ENABLED,
                }],
            };
            AdjustTokenPrivileges(token.0, false, Some(&privileges), 0, None, None)
                .map_err(|e| format!("AdjustTokenPrivileges: {e}"))?;

            // AdjustTokenPrivileges reports success even when it assigned
            // nothing (e.g. policy strips the privilege); the only signal is
            // ERROR_NOT_ALL_ASSIGNED. Without this check the caller would go on
            // to a shutdown that quietly does nothing.
            if GetLastError() == ERROR_NOT_ALL_ASSIGNED {
                return Err("this account is not allowed to shut down the computer".into());
            }
            Ok(())
        }
    }

    /// `SHTDN_REASON_*` for a user-initiated action, so the event log records
    /// the shutdown as planned rather than as an unexpected one.
    const REASON: SHUTDOWN_REASON = SHUTDOWN_REASON(
        SHTDN_REASON_MAJOR_OTHER.0 | SHTDN_REASON_MINOR_OTHER.0 | SHTDN_REASON_FLAG_PLANNED.0,
    );

    fn exit_windows(flags: EXIT_WINDOWS_FLAGS) -> Result<(), String> {
        // EWX_FORCEIFHUNG only overrides apps that stopped answering the
        // close message; a responsive app with unsaved work still gets to
        // block, which is the behaviour of the Start menu's own entries.
        unsafe {
            ExitWindowsEx(flags | EWX_FORCEIFHUNG, REASON)
                .map_err(|e| format!("ExitWindowsEx: {e}"))
        }
    }

    /// Broadcasts the monitor-off system command. `SendMessageTimeoutW` rather
    /// than `SendMessageW`: a broadcast waits on every top-level window, so one
    /// wedged app would otherwise hang this thread indefinitely.
    fn screen_off() -> Result<(), String> {
        unsafe {
            SendMessageTimeoutW(
                HWND_BROADCAST,
                WM_SYSCOMMAND,
                WPARAM(SC_MONITORPOWER as usize),
                // 2 = power off (1 = low power, -1 = on).
                LPARAM(2),
                SMTO_ABORTIFHUNG,
                1000,
                None,
            );
        }
        // The broadcast has no meaningful aggregate return value: a zero LRESULT
        // is what a window that ignored the message returns, not an error.
        Ok(())
    }

    pub fn run(action: PowerAction) -> Result<(), String> {
        match action {
            // Neither needs a privilege: locking and blanking the display are
            // things any interactive user may do to their own session.
            PowerAction::Lock => unsafe {
                LockWorkStation().map_err(|e| format!("LockWorkStation: {e}"))
            },
            PowerAction::ScreenOff => screen_off(),

            PowerAction::SignOut => exit_windows(EWX_LOGOFF),
            PowerAction::Shutdown => {
                enable_shutdown_privilege()?;
                exit_windows(EWX_POWEROFF)
            }
            PowerAction::Restart => {
                enable_shutdown_privilege()?;
                exit_windows(EWX_REBOOT)
            }

            PowerAction::Sleep | PowerAction::Hibernate => {
                enable_shutdown_privilege()?;
                let hibernate = action == PowerAction::Hibernate;
                // `force` stays 0 so drivers may veto (the documented value for
                // Windows Vista and later), and wake events stay enabled so a
                // scheduled task or Wake-on-LAN still brings the machine back.
                //
                // SetSuspendState does not return until the machine RESUMES, so
                // this call owns its thread for the whole sleep. It is reached
                // only from a `spawn_blocking` worker, never the event loop.
                let ok = unsafe {
                    SetSuspendState(BOOLEAN(hibernate as u8), BOOLEAN(0), BOOLEAN(0))
                };
                if ok.0 != 0 {
                    Ok(())
                } else if hibernate {
                    Err("hibernation is disabled on this system".into())
                } else {
                    Err("the system refused to enter sleep".into())
                }
            }
        }
    }
}

#[cfg(target_os = "windows")]
pub fn run(action: PowerAction) -> Result<(), String> {
    imp::run(action)
}

// Non-Windows stub so the workspace type-checks on other platforms.
#[cfg(not(target_os = "windows"))]
pub fn run(_action: PowerAction) -> Result<(), String> {
    Err("power actions are only supported on Windows".into())
}
