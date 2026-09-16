//! Per-application volume mixer (themia-releases#112).
//!
//! Enumerates the audio sessions of the default render endpoint via WASAPI
//! (`IAudioSessionManager2` → `IAudioSessionEnumerator`) and exposes one row per
//! *application* rather than per session: a browser with three noisy tabs owns
//! three sessions and gets ONE slider, keyed by its executable ([S-03]).
//!
//! All COM interfaces are refcounted smart pointers that release on drop (RAII);
//! `ComGuard` balances CoInitialize/CoUninitialize on every path and
//! `CoTaskString` frees every shell-allocated string. No `.unwrap()`: every
//! fallible call returns `Result<_, String>`.

use serde::Serialize;

/// Group key for the Windows "System sounds" session, which has no executable
/// of its own. Disjoint from the two real key spaces (an exe path and a session
/// instance identifier), so `set_level`/`set_muted` can match a key against
/// either without knowing which mode produced it ([S-05]).
pub const SYSTEM_SOUNDS_KEY: &str = "system-sounds";

/// One row of the mixer: an application (grouped) or a single audio session
/// (ungrouped). `name` is empty when Windows tells us nothing usable — the
/// frontend then renders its own localized placeholder, so no English string is
/// ever baked into the backend ([S-04]).
#[derive(Serialize, Clone, Debug, PartialEq)]
pub struct MixerApp {
    pub key: String,
    pub name: String,
    /// Full exe path, for icon extraction via `get_file_icons_batch`. `None` for
    /// system sounds and for processes that refuse a query handle.
    pub path: Option<String>,
    /// 0.0..=1.0 scalar session volume.
    pub level: f32,
    pub muted: bool,
    /// The app is rendering audio right now (`AudioSessionStateActive`).
    pub active: bool,
    pub is_system: bool,
    /// How many audio sessions this row collapses (always 1 when ungrouped).
    pub sessions: u32,
}

/// One raw session as read from WASAPI, before grouping. Public so the pure
/// grouping/ordering step can be unit-tested without COM.
#[derive(Clone, Debug)]
pub struct RawSession {
    /// `IAudioSessionControl2::GetSessionInstanceIdentifier` — unique per session.
    pub session_key: String,
    /// Lowercased exe path, or [`SYSTEM_SOUNDS_KEY`].
    pub group_key: String,
    pub name: String,
    pub path: Option<String>,
    pub level: f32,
    pub muted: bool,
    pub active: bool,
    pub is_system: bool,
}

/// Collapse raw sessions into display rows.
///
/// [S-03] `grouped`: sessions sharing an executable become one row — level is
/// the loudest of the group (after any `set_level` they are all equal anyway),
/// muted only when EVERY session is muted, active when ANY is.
///
/// [S-06] Ordering is by name, case-insensitively, with system sounds pinned
/// last. Deliberately NOT by activity or level: a row that reorders itself the
/// moment an app starts or stops playing would slide out from under the slider
/// the user is dragging.
pub fn group_sessions(raws: Vec<RawSession>, grouped: bool) -> Vec<MixerApp> {
    let mut rows: Vec<MixerApp> = Vec::new();

    for r in raws {
        let key = if grouped {
            r.group_key.clone()
        } else {
            r.session_key.clone()
        };
        match rows.iter_mut().find(|a| a.key == key) {
            Some(existing) => {
                existing.level = existing.level.max(r.level);
                existing.muted = existing.muted && r.muted;
                existing.active = existing.active || r.active;
                existing.sessions += 1;
                if existing.name.is_empty() {
                    existing.name = r.name;
                }
                if existing.path.is_none() {
                    existing.path = r.path;
                }
            }
            None => rows.push(MixerApp {
                key,
                name: r.name,
                path: r.path,
                level: r.level,
                muted: r.muted,
                active: r.active,
                is_system: r.is_system,
                sessions: 1,
            }),
        }
    }

    rows.sort_by(|a, b| {
        a.is_system
            .cmp(&b.is_system)
            .then_with(|| a.name.to_lowercase().cmp(&b.name.to_lowercase()))
            .then_with(|| a.key.cmp(&b.key))
    });
    rows
}

/// Display name for a session, in the order Task Manager itself prefers:
/// version-info FileDescription ("Google Chrome"), then the executable stem
/// ("chrome"), then the session's own display name — skipped when it is an
/// unexpanded resource reference (`@%SystemRoot%\…,-202`), which is what the
/// system-sounds and several UWP sessions report. Empty means "no usable name";
/// the frontend supplies the localized placeholder ([S-04]).
pub fn session_name(description: Option<&str>, exe_path: Option<&str>, display: &str) -> String {
    if let Some(d) = description.map(str::trim).filter(|d| !d.is_empty()) {
        return d.to_string();
    }
    if let Some(stem) = exe_path.and_then(exe_stem) {
        return stem;
    }
    let d = display.trim();
    if d.is_empty() || d.starts_with('@') {
        return String::new();
    }
    d.to_string()
}

/// File name of `path` without its extension, e.g. `C:\…\chrome.exe` → `chrome`.
fn exe_stem(path: &str) -> Option<String> {
    let file = path.rsplit(['\\', '/']).next()?;
    let stem = match file.rfind('.') {
        Some(i) if i > 0 => &file[..i],
        _ => file,
    };
    if stem.is_empty() {
        None
    } else {
        Some(stem.to_string())
    }
}

#[cfg(target_os = "windows")]
mod imp {
    use super::{session_name, RawSession, SYSTEM_SOUNDS_KEY};
    use std::collections::HashMap;
    use std::sync::{Mutex, OnceLock};
    use windows::core::{Interface, PCWSTR, PWSTR};
    use windows::Win32::Foundation::{CloseHandle, HANDLE, S_OK};
    use windows::Win32::Media::Audio::{
        eMultimedia, eRender, AudioSessionStateActive, AudioSessionStateExpired,
        IAudioSessionControl2, IAudioSessionManager2, IMMDeviceEnumerator, ISimpleAudioVolume,
        MMDeviceEnumerator,
    };
    use windows::Win32::Storage::FileSystem::{
        GetFileVersionInfoSizeW, GetFileVersionInfoW, VerQueryValueW,
    };
    use windows::Win32::System::Com::{
        CoCreateInstance, CoInitializeEx, CoTaskMemFree, CoUninitialize, CLSCTX_ALL,
        COINIT_MULTITHREADED,
    };
    use windows::Win32::System::Threading::{
        OpenProcess, QueryFullProcessImageNameW, PROCESS_NAME_WIN32,
        PROCESS_QUERY_LIMITED_INFORMATION,
    };

    /// Balances COM init/uninit for the current thread on all return paths.
    struct ComGuard;
    impl ComGuard {
        fn new() -> Self {
            // S_FALSE (already initialized on this thread) is fine; we still
            // uninit on drop to keep the refcount balanced.
            unsafe {
                let _ = CoInitializeEx(None, COINIT_MULTITHREADED);
            }
            ComGuard
        }
    }
    impl Drop for ComGuard {
        fn drop(&mut self) {
            unsafe { CoUninitialize() };
        }
    }

    /// RAII for a shell-allocated wide string (`CoTaskMemFree` on every path).
    struct CoTaskString(PWSTR);
    impl CoTaskString {
        fn get(&self) -> String {
            if self.0.is_null() {
                return String::new();
            }
            // A session identifier that is not valid UTF-16 is unusable as a
            // key, and "" makes the caller fall back to the group key.
            unsafe { self.0.to_string() }.unwrap_or_default()
        }
    }
    impl Drop for CoTaskString {
        fn drop(&mut self) {
            if !self.0.is_null() {
                unsafe { CoTaskMemFree(Some(self.0 .0 as *const _)) };
            }
        }
    }

    /// RAII guard for a Win32 process handle: closed on every exit path.
    struct ProcessHandle(HANDLE);
    impl Drop for ProcessHandle {
        fn drop(&mut self) {
            // ignore: closing an already-invalid handle has no recovery.
            unsafe { let _ = CloseHandle(self.0); };
        }
    }

    fn wstr(s: &str) -> Vec<u16> {
        s.encode_utf16().chain(std::iter::once(0)).collect()
    }

    /// Full image path of a running process. `None` for protected/elevated
    /// processes (limited-information handle refused) or a PID that exited.
    fn process_exe_path(pid: u32) -> Option<String> {
        if pid == 0 {
            return None;
        }
        unsafe {
            let handle = ProcessHandle(OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid).ok()?);
            let mut buf = [0u16; 1024];
            let mut len = buf.len() as u32;
            QueryFullProcessImageNameW(
                handle.0,
                PROCESS_NAME_WIN32,
                PWSTR(buf.as_mut_ptr()),
                &mut len,
            )
            .ok()?;
            Some(String::from_utf16_lossy(&buf[..len as usize]))
        }
    }

    /// Ordered StringFileInfo sub-block paths to try for FileDescription: each
    /// translation-table language first, then the ubiquitous en-US/Unicode block
    /// (many exes carry only that one even on localised systems).
    fn desc_subblocks(translations: &[(u16, u16)]) -> Vec<String> {
        let mut out: Vec<String> = translations
            .iter()
            .map(|(lang, cp)| format!("\\StringFileInfo\\{lang:04X}{cp:04X}\\FileDescription"))
            .collect();
        let fallback = "\\StringFileInfo\\040904B0\\FileDescription".to_string();
        if !out.contains(&fallback) {
            out.push(fallback);
        }
        out
    }

    /// Read the version-info FileDescription of a binary on disk.
    fn file_description(path: &str) -> Option<String> {
        unsafe {
            let path_w = wstr(path);
            let size = GetFileVersionInfoSizeW(PCWSTR(path_w.as_ptr()), None);
            if size == 0 {
                return None; // no version resource at all
            }
            let mut data = vec![0u8; size as usize];
            GetFileVersionInfoW(
                PCWSTR(path_w.as_ptr()),
                0,
                size,
                data.as_mut_ptr() as *mut std::ffi::c_void,
            )
            .ok()?;

            // (lang, codepage) pairs from the translation table; empty when the
            // resource is malformed, in which case only en-US is tried.
            let mut translations: Vec<(u16, u16)> = Vec::new();
            let trans_key = wstr("\\VarFileInfo\\Translation");
            let mut ptr: *mut std::ffi::c_void = std::ptr::null_mut();
            let mut len: u32 = 0;
            if VerQueryValueW(
                data.as_ptr() as *const _,
                PCWSTR(trans_key.as_ptr()),
                &mut ptr,
                &mut len,
            )
            .as_bool()
                && !ptr.is_null()
            {
                let pairs = std::slice::from_raw_parts(ptr as *const u16, len as usize / 2);
                translations = pairs.chunks_exact(2).map(|c| (c[0], c[1])).collect();
            }

            for sub in desc_subblocks(&translations) {
                let key = wstr(&sub);
                let mut sptr: *mut std::ffi::c_void = std::ptr::null_mut();
                let mut slen: u32 = 0; // length in u16 chars, includes the NUL
                if !VerQueryValueW(
                    data.as_ptr() as *const _,
                    PCWSTR(key.as_ptr()),
                    &mut sptr,
                    &mut slen,
                )
                .as_bool()
                    || sptr.is_null()
                    || slen == 0
                {
                    continue;
                }
                let wide = std::slice::from_raw_parts(sptr as *const u16, slen as usize);
                let s = String::from_utf16_lossy(wide)
                    .trim_end_matches('\0')
                    .trim()
                    .to_string();
                if !s.is_empty() {
                    return Some(s);
                }
            }
            None
        }
    }

    /// Cache backstop: keyed by exe path, so it can only grow as large as the
    /// set of distinct binaries that have ever held an audio session. Clear
    /// rather than grow unbounded under pathological churn.
    const DESC_CACHE_MAX: usize = 256;

    /// Exe path → FileDescription, resolved from disk once per binary. The
    /// mixer polls, so re-reading a version resource every tick would be pure
    /// waste. A poisoned lock is recovered, never unwrapped.
    fn cached_description(path: &str) -> Option<String> {
        static CACHE: OnceLock<Mutex<HashMap<String, Option<String>>>> = OnceLock::new();
        let cache = CACHE.get_or_init(|| Mutex::new(HashMap::new()));
        let mut guard = match cache.lock() {
            Ok(g) => g,
            Err(poisoned) => poisoned.into_inner(),
        };
        if let Some(hit) = guard.get(path) {
            return hit.clone();
        }
        let desc = file_description(path);
        if guard.len() >= DESC_CACHE_MAX {
            guard.clear();
        }
        guard.insert(path.to_string(), desc.clone());
        desc
    }

    /// The session enumerator of the default render endpoint. Held by the caller
    /// for the duration of one command, then released with the ComGuard.
    fn session_enumerator() -> windows::core::Result<windows::Win32::Media::Audio::IAudioSessionEnumerator>
    {
        unsafe {
            let enumerator: IMMDeviceEnumerator =
                CoCreateInstance(&MMDeviceEnumerator, None, CLSCTX_ALL)?;
            let device = enumerator.GetDefaultAudioEndpoint(eRender, eMultimedia)?;
            let manager: IAudioSessionManager2 = device.Activate(CLSCTX_ALL, None)?;
            manager.GetSessionEnumerator()
        }
    }

    /// Read every live session of the default render endpoint. A session that
    /// fails mid-enumeration (the app exited between `GetCount` and the read) is
    /// skipped, never fatal: one dying app must not blank the whole mixer.
    pub fn read_sessions() -> Result<Vec<RawSession>, String> {
        let _guard = ComGuard::new();
        let list = session_enumerator().map_err(|e| e.to_string())?;
        let count = unsafe { list.GetCount() }.map_err(|e| e.to_string())?;

        let mut out = Vec::new();
        for i in 0..count {
            let Ok(ctl) = (unsafe { list.GetSession(i) }) else {
                continue;
            };
            // Expired = the app is gone and Windows has not yet reaped the
            // session; it would render as a dead row.
            let state = unsafe { ctl.GetState() }.ok();
            if state == Some(AudioSessionStateExpired) {
                continue;
            }
            let Ok(ctl2) = ctl.cast::<IAudioSessionControl2>() else {
                continue;
            };
            let Ok(volume) = ctl.cast::<ISimpleAudioVolume>() else {
                continue;
            };

            // IsSystemSoundsSession returns S_FALSE (a SUCCESS code) for a
            // normal session, so the raw HRESULT is the only answer here — a
            // `Result` would read as "yes" for every session.
            let is_system = unsafe { ctl2.IsSystemSoundsSession() } == S_OK;
            let pid = unsafe { ctl2.GetProcessId() }.unwrap_or(0);
            let path = if is_system {
                None
            } else {
                process_exe_path(pid)
            };
            let display = CoTaskString(unsafe { ctl.GetDisplayName() }.unwrap_or(PWSTR::null()));
            let name = if is_system {
                String::new() // localized by the frontend
            } else {
                session_name(
                    path.as_deref().and_then(cached_description).as_deref(),
                    path.as_deref(),
                    &display.get(),
                )
            };

            let instance =
                CoTaskString(unsafe { ctl2.GetSessionInstanceIdentifier() }.unwrap_or(PWSTR::null()));
            let group_key = if is_system {
                SYSTEM_SOUNDS_KEY.to_string()
            } else {
                match path.as_deref() {
                    Some(p) => p.to_lowercase(),
                    // No path (protected process): the PID keeps the row
                    // distinct without pretending two of them are one app.
                    None => format!("pid:{pid}"),
                }
            };
            let session_key = match instance.get() {
                s if s.is_empty() => format!("{group_key}#{i}"),
                s => s,
            };

            out.push(RawSession {
                session_key,
                group_key,
                name,
                path,
                level: unsafe { volume.GetMasterVolume() }.unwrap_or(0.0),
                muted: unsafe { volume.GetMute() }
                    .map(|b| b.as_bool())
                    .unwrap_or(false),
                active: state == Some(AudioSessionStateActive),
                is_system,
            });
        }
        Ok(out)
    }

    /// Apply `f` to every session whose group key OR session key is `key` — the
    /// two key spaces are disjoint, so one command serves both the grouped row
    /// (all of an app's sessions move together) and the ungrouped one ([S-05]).
    fn for_matching(key: &str, f: impl Fn(&ISimpleAudioVolume) -> windows::core::Result<()>) -> Result<(), String> {
        let _guard = ComGuard::new();
        let list = session_enumerator().map_err(|e| e.to_string())?;
        let count = unsafe { list.GetCount() }.map_err(|e| e.to_string())?;

        for i in 0..count {
            let Ok(ctl) = (unsafe { list.GetSession(i) }) else {
                continue;
            };
            let Ok(ctl2) = ctl.cast::<IAudioSessionControl2>() else {
                continue;
            };
            let is_system = unsafe { ctl2.IsSystemSoundsSession() } == S_OK;
            let pid = unsafe { ctl2.GetProcessId() }.unwrap_or(0);
            let group_key = if is_system {
                SYSTEM_SOUNDS_KEY.to_string()
            } else {
                match process_exe_path(pid) {
                    Some(p) => p.to_lowercase(),
                    None => format!("pid:{pid}"),
                }
            };
            let instance =
                CoTaskString(unsafe { ctl2.GetSessionInstanceIdentifier() }.unwrap_or(PWSTR::null()));
            let session_key = match instance.get() {
                s if s.is_empty() => format!("{group_key}#{i}"),
                s => s,
            };
            if group_key != key && session_key != key {
                continue;
            }
            let Ok(volume) = ctl.cast::<ISimpleAudioVolume>() else {
                continue;
            };
            f(&volume).map_err(|e| e.to_string())?;
        }
        // A key that matches nothing is NOT an error: the app can exit between
        // the poll that drew the row and the drag that moves it ([S-05]).
        Ok(())
    }

    pub fn set_level(key: &str, level: f32) -> Result<(), String> {
        let clamped = level.clamp(0.0, 1.0);
        for_matching(key, |v| unsafe {
            v.SetMasterVolume(clamped, std::ptr::null())
        })
    }

    pub fn set_muted(key: &str, muted: bool) -> Result<(), String> {
        for_matching(key, |v| unsafe { v.SetMute(muted, std::ptr::null()) })
    }
}

#[cfg(target_os = "windows")]
pub fn get_sessions(grouped: bool) -> Result<Vec<MixerApp>, String> {
    Ok(group_sessions(imp::read_sessions()?, grouped))
}

#[cfg(target_os = "windows")]
pub fn set_level(key: &str, level: f32) -> Result<(), String> {
    imp::set_level(key, level)
}

#[cfg(target_os = "windows")]
pub fn set_muted(key: &str, muted: bool) -> Result<(), String> {
    imp::set_muted(key, muted)
}

// Non-Windows stubs so the workspace type-checks on other platforms.
#[cfg(not(target_os = "windows"))]
pub fn get_sessions(_grouped: bool) -> Result<Vec<MixerApp>, String> {
    Err("the volume mixer is only supported on Windows".into())
}
#[cfg(not(target_os = "windows"))]
pub fn set_level(_key: &str, _level: f32) -> Result<(), String> {
    Err("the volume mixer is only supported on Windows".into())
}
#[cfg(not(target_os = "windows"))]
pub fn set_muted(_key: &str, _muted: bool) -> Result<(), String> {
    Err("the volume mixer is only supported on Windows".into())
}

#[cfg(test)]
mod tests {
    use super::*;

    fn raw(session: &str, group: &str, name: &str, level: f32, muted: bool, active: bool) -> RawSession {
        RawSession {
            session_key: session.into(),
            group_key: group.into(),
            name: name.into(),
            path: Some(group.into()),
            level,
            muted,
            active,
            is_system: false,
        }
    }

    // [S-03] a browser with three noisy tabs is ONE row, not three.
    #[test]
    fn s03_groups_sessions_by_executable() {
        let rows = group_sessions(
            vec![
                raw("s1", "c:\\chrome.exe", "Google Chrome", 0.4, false, false),
                raw("s2", "c:\\chrome.exe", "Google Chrome", 0.9, true, true),
                raw("s3", "c:\\spotify.exe", "Spotify", 0.5, false, false),
            ],
            true,
        );
        assert_eq!(rows.len(), 2);
        let chrome = &rows[0];
        assert_eq!(chrome.name, "Google Chrome");
        assert_eq!(chrome.sessions, 2);
        assert_eq!(chrome.level, 0.9, "group level is the loudest session");
        assert!(!chrome.muted, "one unmuted session leaves the group unmuted");
        assert!(chrome.active, "one playing session makes the group active");
    }

    #[test]
    fn s03_ungrouped_keeps_one_row_per_session() {
        let rows = group_sessions(
            vec![
                raw("s1", "c:\\chrome.exe", "Google Chrome", 0.4, false, false),
                raw("s2", "c:\\chrome.exe", "Google Chrome", 0.9, false, false),
            ],
            false,
        );
        assert_eq!(rows.len(), 2);
        assert_eq!(rows[0].key, "s1");
        assert_eq!(rows[1].key, "s2");
        assert!(rows.iter().all(|r| r.sessions == 1));
    }

    #[test]
    fn s03_group_is_muted_only_when_every_session_is() {
        let rows = group_sessions(
            vec![
                raw("s1", "c:\\chrome.exe", "Google Chrome", 0.4, true, false),
                raw("s2", "c:\\chrome.exe", "Google Chrome", 0.9, true, false),
            ],
            true,
        );
        assert!(rows[0].muted);
    }

    // [S-06] alphabetical, system sounds last, activity does NOT reorder.
    #[test]
    fn s06_orders_by_name_with_system_sounds_last() {
        let mut sys = raw("sys", SYSTEM_SOUNDS_KEY, "", 1.0, false, true);
        sys.is_system = true;
        sys.path = None;
        let rows = group_sessions(
            vec![
                raw("s1", "c:\\spotify.exe", "Spotify", 0.5, false, false),
                sys,
                raw("s2", "c:\\discord.exe", "Discord", 0.5, false, true),
            ],
            true,
        );
        assert_eq!(
            rows.iter().map(|r| r.key.as_str()).collect::<Vec<_>>(),
            vec!["c:\\discord.exe", "c:\\spotify.exe", SYSTEM_SOUNDS_KEY],
        );
    }

    #[test]
    fn s06_orders_case_insensitively() {
        let rows = group_sessions(
            vec![
                raw("s1", "c:\\zoom.exe", "zoom", 0.5, false, false),
                raw("s2", "c:\\arc.exe", "Arc", 0.5, false, false),
            ],
            true,
        );
        assert_eq!(rows[0].name, "Arc");
    }

    // [S-04] name resolution order + the "no usable name" contract.
    #[test]
    fn s04_prefers_file_description() {
        assert_eq!(
            session_name(Some("Google Chrome"), Some("C:\\x\\chrome.exe"), "Chrome"),
            "Google Chrome"
        );
    }

    #[test]
    fn s04_falls_back_to_exe_stem() {
        assert_eq!(session_name(None, Some("C:\\x\\chrome.exe"), ""), "chrome");
        assert_eq!(session_name(Some("  "), Some("C:/x/vlc.exe"), ""), "vlc");
    }

    #[test]
    fn s04_ignores_unexpanded_resource_display_names() {
        assert_eq!(
            session_name(None, None, "@%SystemRoot%\\System32\\AudioSrv.Dll,-202"),
            ""
        );
        assert_eq!(session_name(None, None, "Discord"), "Discord");
    }

    /// Manual diagnostic: CI has no audio endpoint, so the WASAPI path can only
    /// be exercised on a real desktop. Play something, then run
    /// `cargo test -p widget-mixer -- --ignored --nocapture`.
    #[test]
    #[ignore = "needs a real audio endpoint; run manually"]
    fn diag_dump_live_sessions() {
        match get_sessions(true) {
            Ok(rows) => {
                println!("{} row(s):", rows.len());
                for r in &rows {
                    println!(
                        "  {:<28} lvl={:.2} muted={} active={} sessions={} sys={} key={}",
                        if r.name.is_empty() { "<unnamed>" } else { &r.name },
                        r.level,
                        r.muted,
                        r.active,
                        r.sessions,
                        r.is_system,
                        r.key
                    );
                }
            }
            Err(e) => println!("get_sessions failed: {e}"),
        }
    }

    /// Manual diagnostic for the WRITE path ([S-05]): halve the first real
    /// app's level, read it back, then restore it. Same invocation as above.
    #[test]
    #[ignore = "mutates a live app's volume; run manually"]
    fn diag_roundtrip_first_app_level() {
        let Ok(rows) = get_sessions(true) else {
            println!("no endpoint");
            return;
        };
        let Some(row) = rows.into_iter().find(|r| !r.is_system) else {
            println!("no app session is open — start something that plays audio");
            return;
        };
        let before = row.level;
        let target = if before > 0.5 { 0.25 } else { 0.75 };
        println!("{}: {before:.2} -> {target:.2}", row.name);
        if let Err(e) = set_level(&row.key, target) {
            println!("set_level failed: {e}");
            return;
        }
        let read_back = get_sessions(true)
            .ok()
            .and_then(|rs| rs.into_iter().find(|r| r.key == row.key))
            .map(|r| r.level);
        println!("read back: {read_back:?}");
        assert_eq!(read_back.map(|l| (l * 100.0).round()), Some((target * 100.0).round()));
        let _ = set_level(&row.key, before);
    }
}
