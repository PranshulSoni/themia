//! Detect Windows fullscreen / game / presentation mode via
//! `SHQueryUserNotificationState` and emit a Tauri event when the state flips.
//!
//! The watcher runs in a dedicated OS thread that polls every ~2s. The frontend
//! toggles it on/off via `set_game_mode_watcher`. When "game mode" is active,
//! the frontend can hide or suspend widgets to minimise background work while
//! the user games or gives a presentation.

use tauri::AppHandle;

pub const EVENT_NAME: &str = "themia:game-mode";

#[derive(Clone, serde::Serialize)]
struct GameModePayload {
    active: bool,
}

#[cfg(target_os = "windows")]
mod imp {
    use super::{GameModePayload, EVENT_NAME};
    use std::sync::atomic::{AtomicBool, Ordering};
    use std::sync::OnceLock;
    use std::thread;
    use std::time::Duration;
    use tauri::{AppHandle, Emitter};

    static RUNNING: AtomicBool = AtomicBool::new(false);
    static LAST_ACTIVE: AtomicBool = AtomicBool::new(false);
    static APP: OnceLock<AppHandle> = OnceLock::new();

    /// Returns true when the system reports a fullscreen D3D app, presentation
    /// mode, or "busy": the signals Windows itself uses for "don't disturb me".
    fn query_active() -> bool {
        use windows::Win32::UI::Shell::{
            SHQueryUserNotificationState, QUNS_BUSY, QUNS_PRESENTATION_MODE,
            QUNS_RUNNING_D3D_FULL_SCREEN,
        };

        unsafe {
            match SHQueryUserNotificationState() {
                Ok(state) => {
                    state == QUNS_RUNNING_D3D_FULL_SCREEN
                        || state == QUNS_PRESENTATION_MODE
                        || state == QUNS_BUSY
                }
                Err(_) => false,
            }
        }
    }

    fn spawn_watcher() {
        thread::spawn(|| {
            // SHQueryUserNotificationState requires COM on the calling thread.
            unsafe {
                use windows::Win32::System::Com::{CoInitializeEx, COINIT_APARTMENTTHREADED};
                // ignore: CoInitializeEx may return S_FALSE (already initialized on
                // this thread) or RPC_E_CHANGED_MODE; neither is fatal for a
                // query-only consumer of SHQueryUserNotificationState.
                let _ = CoInitializeEx(None, COINIT_APARTMENTTHREADED);
            }

            while RUNNING.load(Ordering::Relaxed) {
                let active = query_active();
                let prev = LAST_ACTIVE.swap(active, Ordering::Relaxed);
                if active != prev {
                    if let Some(app) = APP.get() {
                        if let Err(e) = app.emit(EVENT_NAME, GameModePayload { active }) {
                            log::warn!("game_mode: failed to emit {EVENT_NAME}: {e}");
                        }
                    }
                }
                thread::sleep(Duration::from_millis(2000));
            }
        });
    }

    pub fn set_enabled(app: AppHandle, enabled: bool) {
        // ignore: OnceLock::set returns Err only when already populated, which is
        // the expected case on every call after the first enable.
        let _ = APP.set(app.clone());

        if enabled {
            if RUNNING
                .compare_exchange(false, true, Ordering::SeqCst, Ordering::SeqCst)
                .is_ok()
            {
                // Emit current state immediately so the UI reflects reality on enable.
                let active = query_active();
                LAST_ACTIVE.store(active, Ordering::Relaxed);
                if let Err(e) = app.emit(EVENT_NAME, GameModePayload { active }) {
                    log::warn!("game_mode: failed to emit {EVENT_NAME}: {e}");
                }
                spawn_watcher();
            } else {
                // Already running, re-emit last known state so any new subscriber syncs.
                let active = LAST_ACTIVE.load(Ordering::Relaxed);
                if let Err(e) = app.emit(EVENT_NAME, GameModePayload { active }) {
                    log::warn!("game_mode: failed to emit {EVENT_NAME}: {e}");
                }
            }
        } else {
            RUNNING.store(false, Ordering::Relaxed);
            // Announce "not active" so suspended surfaces come back.
            LAST_ACTIVE.store(false, Ordering::Relaxed);
            if let Err(e) = app.emit(EVENT_NAME, GameModePayload { active: false }) {
                log::warn!("game_mode: failed to emit {EVENT_NAME}: {e}");
            }
        }
    }
}

#[cfg(not(target_os = "windows"))]
mod imp {
    use tauri::{AppHandle, Emitter};
    use super::{GameModePayload, EVENT_NAME};

    pub fn set_enabled(app: AppHandle, _enabled: bool) {
        // No detection on non-Windows, always report inactive.
        if let Err(e) = app.emit(EVENT_NAME, GameModePayload { active: false }) {
            log::warn!("game_mode: failed to emit {EVENT_NAME}: {e}");
        }
    }
}

#[tauri::command]
pub fn set_game_mode_watcher(app: AppHandle, enabled: bool) {
    imp::set_enabled(app, enabled);
}
