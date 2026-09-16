// ---------------------------------------------------------------------------
// System-tray widgets  (src/Tray.SPEC.md)
// ---------------------------------------------------------------------------
//
// A tray widget is an ordinary widget instance that lives in the `tray`
// collection instead of on a desktop view. Each one owns ONE notification-area
// icon; left-clicking it opens that widget in a small borderless flyout anchored
// beside the tray, which closes again on Esc, on a second click of the same
// icon, or as soon as it loses focus.
//
// Ownership split:
//   * The FRONTEND owns the list (localStorage `widget-layout-tray`), the
//     labels and the rasterized icons, because that is where the registry and
//     the translations already live. It pushes the whole desired set through
//     `tray_sync` ([S-02]).
//   * RUST owns the live `TrayIcon` handles, the click→flyout mapping and the
//     flyout window ([S-04]). `tray_sync` is a DIFF, not a rebuild: syncing the
//     same set twice is a no-op, so every window may push without coordinating
//     (they all read the same localStorage, so they compute the same set).
//
// Windows detail: a tray icon is a message-window resource, so every create /
// update / destroy is marshalled onto the main thread. Building the flyout
// WebView is the opposite — `WebviewWindowBuilder::build` blocks on the main
// thread's message pump, so it must run on a worker (see the module comment on
// `reconcile_disabled_windows`).

use std::sync::Mutex;

use tauri::image::Image;
use tauri::tray::{MouseButton, MouseButtonState, TrayIcon, TrayIconBuilder, TrayIconEvent};
use tauri::Manager;

/// Label of the singleton flyout window. One window is reused for every tray
/// icon (retargeted through `tray-flyout-navigate`), so N icons cost one WebView
/// at most, and only once the user has actually opened one.
const FLYOUT_LABEL: &str = "tray-flyout";

/// Gap in LOGICAL px between the flyout and both the work-area edge it hangs off
/// and the screen edges it is clamped against ([S-16]).
///
/// Logical, not physical, for the same reason the panel's own size is: a fixed
/// physical gap shrinks as the display scales, so at 150% an 8px margin read as
/// about five and the panel looked stuck to the corner. Converted through the
/// target monitor's scale in `flyout_geometry`, alongside the size.
const FLYOUT_GAP: f64 = 12.0;

// ── Frontend-facing shapes ──────────────────────────────────────────────────

/// One tray widget, exactly as the frontend describes it ([S-02]).
#[derive(serde::Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct TraySpec {
    /// The widget id in the `tray` collection. Also the tray icon's id.
    pub id: String,
    /// Hover text: the widget's label, falling back to its type's name.
    pub tooltip: String,
    /// RGBA8 bitmap of the widget's registry icon, `iconSize² * 4` bytes.
    pub icon: Vec<u8>,
    /// Edge length of `icon`, in physical px (see `tray_env`).
    pub icon_size: u32,
    /// Flyout size in LOGICAL px (the widget's configured size, [S-07]).
    pub width: f64,
    pub height: f64,
}

/// The tray menu's strings, translated frontend-side ([S-08]). Sent with every
/// sync rather than stored, so switching language re-labels the live menus.
#[derive(serde::Deserialize, Clone, PartialEq, Eq, Default)]
#[serde(rename_all = "camelCase")]
pub struct TrayLabels {
    pub open: String,
    pub settings: String,
    pub app_settings: String,
    pub quit: String,
}

/// What the frontend needs to rasterize icons that match the shell ([S-03]).
#[derive(serde::Serialize)]
#[serde(rename_all = "camelCase")]
pub struct TrayEnv {
    /// The notification area's icon edge length in physical px (`SM_CXSMICON`),
    /// so the frontend rasterizes at 1:1 instead of letting Windows resample a
    /// fixed-size bitmap and smear the 1.5px lucide strokes.
    pub icon_size: u32,
    /// True when the shell paints the taskbar light, i.e. icons must be dark.
    pub light_theme: bool,
}

// ── Live state ──────────────────────────────────────────────────────────────

/// A live tray icon plus the parts of its spec that decide whether the next
/// sync has to touch it at all.
struct Entry {
    id: String,
    icon: TrayIcon,
    tooltip: String,
    /// Fingerprint of the applied bitmap, so a re-sync from a second window
    /// (identical bytes) does not churn the shell ([S-02]).
    icon_sig: u64,
    width: f64,
    height: f64,
}

/// Insertion-ordered, because that is the order the icons appear in the tray.
/// A handful of entries at most, so linear lookup is the right shape.
static TRAY: Mutex<Vec<Entry>> = Mutex::new(Vec::new());

/// Menu strings the live menus were built with. A change means every menu is
/// stale, which is rare enough (a language switch) to fix by rebuilding.
static LABELS: Mutex<Option<TrayLabels>> = Mutex::new(None);

/// Id of the tray icon the cursor is currently over, maintained from the
/// Enter/Leave tray events. Read when the flyout loses focus, to tell "the user
/// clicked the icon again" (→ the click must CLOSE, [S-06]) apart from "the user
/// clicked something else" (→ the next click must OPEN).
static HOVERED: Mutex<Option<String>> = Mutex::new(None);

/// Armed by the blur handler; swallows exactly one click of that icon.
static SUPPRESS_CLICK: Mutex<Option<String>> = Mutex::new(None);

/// Widget id the flyout is currently showing, or None while it is hidden.
static SHOWN_FOR: Mutex<Option<String>> = Mutex::new(None);

/// Widget id the flyout's PAGE is loaded with — which outlives a hide, because
/// hiding does not unload the document. Set when that document reports itself
/// ready, never when the load is merely requested, so it always names what is
/// actually on screen. Reopening the same icon can therefore skip the reload;
/// only a switch to another icon pays for one ([S-05]).
static LOADED_FOR: Mutex<Option<String>> = Mutex::new(None);

/// An open request waiting for its document to finish loading ([S-12]).
/// Both fields are PHYSICAL px ([S-15]).
#[derive(Clone)]
struct Pending {
    widget: String,
    position: (i32, i32),
    size_px: (i32, i32),
}

/// Set while a flyout load is in flight; consumed by `tray_flyout_ready`.
static PENDING: Mutex<Option<Pending>> = Mutex::new(None);

/// Anchor the visible flyout was placed against, so a live size change can
/// re-place it against the same point ([S-13]).
static SHOWN_ANCHOR: Mutex<Option<(i32, i32)>> = Mutex::new(None);

/// Widget whose flyout is PINNED open as a settings preview ([S-13]). While set,
/// losing focus does not hide the panel — the point of the preview is that it
/// stays visible while the user works in the settings window.
static PINNED_FOR: Mutex<Option<String>> = Mutex::new(None);

// Lock helpers. A panic elsewhere while one of these is held must not take the
// tray down with it, so every one recovers the poisoned guard (CLAUDE.md).
fn tray_state() -> std::sync::MutexGuard<'static, Vec<Entry>> {
    TRAY.lock().unwrap_or_else(|e| e.into_inner())
}
fn labels() -> std::sync::MutexGuard<'static, Option<TrayLabels>> {
    LABELS.lock().unwrap_or_else(|e| e.into_inner())
}
fn hovered() -> std::sync::MutexGuard<'static, Option<String>> {
    HOVERED.lock().unwrap_or_else(|e| e.into_inner())
}
fn suppress_click() -> std::sync::MutexGuard<'static, Option<String>> {
    SUPPRESS_CLICK.lock().unwrap_or_else(|e| e.into_inner())
}
fn shown_for() -> std::sync::MutexGuard<'static, Option<String>> {
    SHOWN_FOR.lock().unwrap_or_else(|e| e.into_inner())
}
fn loaded_for() -> std::sync::MutexGuard<'static, Option<String>> {
    LOADED_FOR.lock().unwrap_or_else(|e| e.into_inner())
}
fn pending() -> std::sync::MutexGuard<'static, Option<Pending>> {
    PENDING.lock().unwrap_or_else(|e| e.into_inner())
}
fn shown_anchor() -> std::sync::MutexGuard<'static, Option<(i32, i32)>> {
    SHOWN_ANCHOR.lock().unwrap_or_else(|e| e.into_inner())
}
fn pinned_for() -> std::sync::MutexGuard<'static, Option<String>> {
    PINNED_FOR.lock().unwrap_or_else(|e| e.into_inner())
}

/// Take one icon out of the shell for good ([S-18]).
///
/// Dropping the `TrayIcon` is NOT enough, which is what the removal path used to
/// assume: `TrayIconBuilder::build` also parks a CLONE in the app's resource
/// table, and `tray_icon` only issues `Shell_NotifyIcon(NIM_DELETE)` when the
/// last clone dies. So a dropped-but-still-registered icon stayed on screen for
/// the life of the process — a removed tray widget kept its icon, and a language
/// switch (which rebuilds every menu) added a second icon per widget on top of
/// the first. Both handles have to go, so both go here, in one place.
///
/// Must run on the main thread, like every other tray mutation.
fn destroy_icon(app: &tauri::AppHandle, entry: Entry) {
    let Entry { id, icon, .. } = entry;
    drop(icon);
    if app.remove_tray_by_id(id.as_str()).is_none() {
        // Not fatal — the shell icon is already gone with the handle above —
        // but it means the registry and our list had drifted apart.
        log::warn!(target: "tray", "tray icon {id} was not in the registry");
    }
    log::info!(target: "tray", "removed tray icon for {id}");
}

/// Drop every live icon ([S-18]). Called from `RunEvent::Exit`, which runs
/// BEFORE Tauri's own `cleanup_before_exit`: our `TRAY` static is never dropped
/// at process exit, so without this the last handle outlives the shell
/// notification and a clean quit ghosted every icon until the user next hovered
/// the notification area.
pub fn shutdown(app: &tauri::AppHandle) {
    let live = std::mem::take(&mut *tray_state());
    for entry in live {
        destroy_icon(app, entry);
    }
}

/// FNV-1a over the bitmap. Only ever compared against another fingerprint from
/// this same function, so the weak collision resistance costs nothing: the worst
/// a collision can do is skip one icon repaint.
fn icon_fingerprint(bytes: &[u8], size: u32) -> u64 {
    let mut h: u64 = 0xcbf2_9ce4_8422_2325;
    for b in bytes.iter().chain(size.to_le_bytes().iter()) {
        h ^= *b as u64;
        h = h.wrapping_mul(0x0000_0100_0000_01b3);
    }
    h
}

// ── Flyout geometry ─────────────────────────────────────────────────────────

/// A monitor work area in physical px: the desktop minus the taskbar.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct WorkArea {
    pub left: i32,
    pub top: i32,
    pub right: i32,
    pub bottom: i32,
}

/// Top-left corner for a flyout of `size` opened from a tray click at `cursor`
/// ([S-05]). Pure, so the taskbar-edge cases are unit-testable without a shell.
///
/// The tray sits in whichever band the work area does NOT cover, so the click
/// position relative to the work area IS the taskbar edge — no separate probe,
/// and it stays correct for a vertical or top-docked taskbar. The flyout hangs
/// off that edge, is centred on the click along the other axis, and is finally
/// clamped inside the work area so a click near a corner cannot push it
/// off-screen. Clamping is low-edge-last (`min` then `max`), so a flyout larger
/// than the work area still lands at the top-left rather than inverting.
pub fn flyout_position(
    work: WorkArea,
    cursor: (i32, i32),
    size: (i32, i32),
    gap: i32,
) -> (i32, i32) {
    let (w, h) = size;
    let (cx, cy) = cursor;
    let (mut x, mut y) = if cx >= work.right {
        // Taskbar on the right: hang off it, vertically centred on the click.
        (work.right - w - gap, cy - h / 2)
    } else if cx < work.left {
        (work.left + gap, cy - h / 2)
    } else if cy < work.top {
        // Taskbar on top.
        (cx - w / 2, work.top + gap)
    } else {
        // Bottom taskbar (the default) — and the right fallback for a click
        // reported inside the work area, where "above the bottom edge" still is.
        (cx - w / 2, work.bottom - h - gap)
    };
    x = x.min(work.right - w - gap).max(work.left + gap);
    y = y.min(work.bottom - h - gap).max(work.top + gap);
    (x, y)
}

/// A point in the notification area, derived from where the taskbar is ([S-13]).
///
/// The taskbar IS the band the work area does not cover, so subtracting one
/// rect from the other names the docked edge without asking the shell anything.
/// The tray sits at the far end of that band — the end the clock is at — which
/// is the bottom/right corner for the usual layouts. Returns a point just
/// OUTSIDE the work area, exactly like a real click on a tray icon, so it feeds
/// `flyout_position` the same way. Falls back to the bottom-right work-area
/// corner when the taskbar is hidden or covers nothing. Pure.
pub fn tray_anchor_point(monitor: WorkArea, work: WorkArea) -> (i32, i32) {
    if work.bottom < monitor.bottom {
        (work.right - 1, work.bottom + 1) // bottom taskbar (the default)
    } else if work.top > monitor.top {
        (work.right - 1, work.top - 1) // top taskbar
    } else if work.right < monitor.right {
        (work.right + 1, work.bottom - 1) // right taskbar
    } else if work.left > monitor.left {
        (work.left - 1, work.bottom - 1) // left taskbar
    } else {
        (work.right - 1, work.bottom - 1) // auto-hidden / no taskbar band
    }
}

/// The live tray anchor on the primary monitor, or None when the shell will not
/// describe it.
#[cfg(target_os = "windows")]
fn tray_anchor(_app: &tauri::AppHandle) -> Option<(i32, i32)> {
    let (monitor, work) = primary_monitor_rects()?;
    Some(tray_anchor_point(monitor, work))
}

#[cfg(not(target_os = "windows"))]
fn tray_anchor(_app: &tauri::AppHandle) -> Option<(i32, i32)> {
    None
}

/// The primary monitor's full rect and its work area, in physical px.
#[cfg(target_os = "windows")]
fn primary_monitor_rects() -> Option<(WorkArea, WorkArea)> {
    use winapi::shared::windef::POINT;
    use winapi::um::winuser::{
        GetMonitorInfoW, MonitorFromPoint, MONITORINFO, MONITOR_DEFAULTTOPRIMARY,
    };

    // SAFETY: stack-owned structs, fully initialized before the call, and the
    // HMONITOR is borrowed for the duration of the call only.
    unsafe {
        let hmon = MonitorFromPoint(POINT { x: 0, y: 0 }, MONITOR_DEFAULTTOPRIMARY);
        if hmon.is_null() {
            return None;
        }
        let mut mi: MONITORINFO = std::mem::zeroed();
        mi.cbSize = std::mem::size_of::<MONITORINFO>() as u32;
        if GetMonitorInfoW(hmon, &mut mi) == 0 {
            return None;
        }
        let rect = |r: winapi::shared::windef::RECT| WorkArea {
            left: r.left,
            top: r.top,
            right: r.right,
            bottom: r.bottom,
        };
        Some((rect(mi.rcMonitor), rect(mi.rcWork)))
    }
}

/// The work area of the monitor under a physical point.
#[cfg(target_os = "windows")]
fn work_area_at(x: i32, y: i32) -> Option<WorkArea> {
    use winapi::shared::windef::POINT;
    use winapi::um::winuser::{
        GetMonitorInfoW, MonitorFromPoint, MONITORINFO, MONITOR_DEFAULTTONEAREST,
    };

    // SAFETY: both calls take stack-owned structs we fully initialize first, and
    // the returned HMONITOR is borrowed for the duration of the call only.
    unsafe {
        let hmon = MonitorFromPoint(POINT { x, y }, MONITOR_DEFAULTTONEAREST);
        if hmon.is_null() {
            return None;
        }
        let mut mi: MONITORINFO = std::mem::zeroed();
        mi.cbSize = std::mem::size_of::<MONITORINFO>() as u32;
        if GetMonitorInfoW(hmon, &mut mi) == 0 {
            return None;
        }
        Some(WorkArea {
            left: mi.rcWork.left,
            top: mi.rcWork.top,
            right: mi.rcWork.right,
            bottom: mi.rcWork.bottom,
        })
    }
}

#[cfg(not(target_os = "windows"))]
fn work_area_at(_x: i32, _y: i32) -> Option<WorkArea> {
    None
}

// ── Shell environment ───────────────────────────────────────────────────────

/// `SM_CXSMICON`, the edge the shell actually draws notification icons at.
#[cfg(target_os = "windows")]
fn small_icon_size() -> u32 {
    use winapi::um::winuser::{GetSystemMetrics, SM_CXSMICON};
    // SAFETY: a pure metric read, no pointers involved.
    let px = unsafe { GetSystemMetrics(SM_CXSMICON) };
    if px > 0 {
        px as u32
    } else {
        16
    }
}

#[cfg(not(target_os = "windows"))]
fn small_icon_size() -> u32 {
    16
}

/// Does the shell paint the taskbar light? `SystemUsesLightTheme` is the value
/// the taskbar/tray follow; `AppsUseLightTheme` (the other value in that key) is
/// the one app windows follow and is deliberately NOT what we read here.
/// Absent/unreadable → dark, which is both the Windows default and the safe
/// answer (a light icon on a dark tray is legible; the inverse is invisible).
#[cfg(target_os = "windows")]
fn system_uses_light_theme() -> bool {
    use std::os::windows::ffi::OsStrExt;
    use winapi::shared::minwindef::DWORD;
    use winapi::shared::minwindef::HKEY__;
    use winapi::um::winnt::{KEY_READ, REG_DWORD};
    use winapi::um::winreg::{RegCloseKey, RegOpenKeyExW, RegQueryValueExW, HKEY_CURRENT_USER};

    fn wide(s: &str) -> Vec<u16> {
        std::ffi::OsStr::new(s)
            .encode_wide()
            .chain(std::iter::once(0))
            .collect()
    }

    let subkey = wide(r"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
    let name = wide("SystemUsesLightTheme");
    // SAFETY: every out-param is a stack local sized by `size_of`, and the key is
    // closed on both exit paths.
    unsafe {
        let mut hkey: *mut HKEY__ = std::ptr::null_mut();
        if RegOpenKeyExW(HKEY_CURRENT_USER, subkey.as_ptr(), 0, KEY_READ, &mut hkey) != 0 {
            return false;
        }
        let mut value: DWORD = 0;
        let mut kind: DWORD = 0;
        let mut len: DWORD = std::mem::size_of::<DWORD>() as DWORD;
        let rc = RegQueryValueExW(
            hkey,
            name.as_ptr(),
            std::ptr::null_mut(),
            &mut kind,
            &mut value as *mut DWORD as *mut u8,
            &mut len,
        );
        RegCloseKey(hkey);
        rc == 0 && kind == REG_DWORD && value == 1
    }
}

#[cfg(not(target_os = "windows"))]
fn system_uses_light_theme() -> bool {
    false
}

// ── Commands ────────────────────────────────────────────────────────────────

/// Icon size + tray theme, so the frontend can rasterize icons the shell will
/// neither have to resample nor hide against its own background ([S-03]).
#[tauri::command]
pub fn tray_env() -> TrayEnv {
    TrayEnv {
        icon_size: small_icon_size(),
        light_theme: system_uses_light_theme(),
    }
}

/// Make the live tray match `specs` ([S-02]). Idempotent by construction:
/// unchanged entries are left alone, so the redundant pushes from a second
/// window (or a re-mount) cost one comparison each.
#[tauri::command]
pub async fn tray_sync(
    app: tauri::AppHandle,
    specs: Vec<TraySpec>,
    labels: TrayLabels,
) -> Result<(), String> {
    // Tray icons are message-window resources: create/update/destroy all have to
    // happen on the thread that owns the pump.
    let handle = app.clone();
    app.run_on_main_thread(move || apply_sync(&handle, specs, labels))
        .map_err(|e| format!("tray_sync: could not reach the main thread: {e}"))
}

/// Hide the flyout. Called by the flyout itself on Esc ([S-06]). Esc is an
/// explicit dismissal, so it also releases a settings preview pin ([S-13]) —
/// otherwise the panel would reappear and look unclosable.
#[tauri::command]
pub fn tray_flyout_hide(app: tauri::AppHandle) {
    *pinned_for() = None;
    hide_flyout(&app);
}

/// The flyout's document reports that it has mounted `widget` ([S-12]). This is
/// what makes the panel visible after a load: the window is held hidden until
/// the document that will be on screen is the one that was asked for, so a
/// switch between icons never flashes the previous widget.
#[tauri::command]
pub fn tray_flyout_ready(app: tauri::AppHandle, widget: String) {
    *loaded_for() = Some(widget.clone());
    // Only the request this document was loaded FOR may show it. A superseded
    // load (the user clicked another icon meanwhile) reports ready too, and
    // showing on that would put the wrong widget on screen.
    let request = {
        let mut p = pending();
        if p.as_ref().is_some_and(|q| q.widget == widget) { p.take() } else { None }
    };
    let Some(request) = request else { return };
    let Some(win) = app.get_webview_window(FLYOUT_LABEL) else { return };
    if let Err(e) = place_and_show(&win, &widget, request.position, request.size_px) {
        log::error!(target: "tray", "could not show the flyout for {widget}: {e}");
    }
}

/// Pin a widget's flyout open as a live settings preview, or release the pin
/// ([S-13]).
///
/// The settings window calls this as the selection moves: while a tray widget's
/// config page is open, its panel stays on screen at the real size, in the real
/// place, updating as the settings change — which is the whole point, so losing
/// focus to the settings window must not dismiss it.
#[tauri::command]
pub fn tray_flyout_preview(app: tauri::AppHandle, widget: Option<String>) {
    let Some(widget) = widget else {
        *pinned_for() = None;
        hide_flyout(&app);
        return;
    };
    *pinned_for() = Some(widget.clone());
    // Anchor where a real tray click would put it, so the preview is a preview
    // of the actual thing and not just of the widget's contents.
    match tray_anchor(&app) {
        Some(anchor) => show_flyout(&app, &widget, anchor),
        None => log::warn!(target: "tray", "no tray anchor; not previewing {widget}"),
    }
}

// ── Sync ────────────────────────────────────────────────────────────────────

/// Main-thread half of `tray_sync`. Deliberately returns nothing: one bad icon
/// must not abort the whole set, so each failure is logged and the pass
/// continues. A missing icon is recoverable on the next sync; a pass that bailed
/// half-way leaves the tray disagreeing with the settings list.
fn apply_sync(app: &tauri::AppHandle, specs: Vec<TraySpec>, new_labels: TrayLabels) {
    // A language switch invalidates every menu. Rebuilding all of them is the
    // one path that keeps menu text and the live list in step; the alternative
    // (patching each item in place) is a second, rarely-exercised update path.
    {
        let stale = {
            let mut current = labels();
            if current.as_ref() == Some(&new_labels) {
                Vec::new()
            } else {
                let had = current.is_some();
                *current = Some(new_labels.clone());
                if had {
                    log::info!(target: "tray", "menu labels changed, rebuilding tray icons");
                    std::mem::take(&mut *tray_state())
                } else {
                    Vec::new()
                }
            }
        };
        // [S-18] Outside the locks: the old icons must actually LEAVE the shell
        // before the loop below rebuilds them under the same ids, or the rebuild
        // doubles the tray instead of replacing it.
        for entry in stale {
            destroy_icon(app, entry);
        }
    }

    let mut state = tray_state();
    // Applied after the state lock is released; `place_and_show` takes locks of
    // its own, and holding two at once is how a deadlock starts.
    let mut resize_shown: Option<(String, f64, f64)> = None;

    // Drop icons whose widget is gone. `partition` keeps insertion order, which
    // is the order the icons sit in the tray.
    let keep: std::collections::HashSet<String> = specs.iter().map(|s| s.id.clone()).collect();
    let (alive, dead): (Vec<Entry>, Vec<Entry>) = std::mem::take(&mut *state)
        .into_iter()
        .partition(|e| keep.contains(&e.id));
    *state = alive;
    for entry in dead {
        destroy_icon(app, entry);
    }

    for spec in specs {
        let sig = icon_fingerprint(&spec.icon, spec.icon_size);
        let expected = (spec.icon_size as usize).saturating_mul(spec.icon_size as usize) * 4;
        if expected == 0 || spec.icon.len() != expected {
            log::error!(
                target: "tray",
                "tray icon for {} has {} bytes, expected {expected} for {}px; skipping",
                spec.id, spec.icon.len(), spec.icon_size,
            );
            continue;
        }

        if let Some(entry) = state.iter_mut().find(|e| e.id == spec.id) {
            // Live icon: patch only what actually changed.
            if entry.icon_sig != sig {
                let image = Image::new_owned(spec.icon, spec.icon_size, spec.icon_size);
                match entry.icon.set_icon(Some(image)) {
                    Ok(()) => entry.icon_sig = sig,
                    Err(e) => log::error!(target: "tray", "set_icon failed for {}: {e}", spec.id),
                }
            }
            if entry.tooltip != spec.tooltip {
                match entry.icon.set_tooltip(Some(&spec.tooltip)) {
                    Ok(()) => entry.tooltip = spec.tooltip,
                    Err(e) => log::error!(target: "tray", "set_tooltip failed for {}: {e}", spec.id),
                }
            }
            // [S-13] A size edited in settings must move the panel that is on
            // screen previewing it, or the preview stops previewing the thing
            // being changed.
            if (entry.width, entry.height) != (spec.width, spec.height)
                && shown_for().as_deref() == Some(spec.id.as_str())
            {
                resize_shown = Some((spec.id.clone(), spec.width, spec.height));
            }
            entry.width = spec.width;
            entry.height = spec.height;
            continue;
        }

        match build_icon(app, &spec, &new_labels) {
            Ok(icon) => {
                log::info!(target: "tray", "added tray icon for {}", spec.id);
                state.push(Entry {
                    id: spec.id,
                    icon,
                    tooltip: spec.tooltip,
                    icon_sig: sig,
                    width: spec.width,
                    height: spec.height,
                });
            }
            Err(e) => {
                log::error!(target: "tray", "could not create tray icon for {}: {e}", spec.id)
            }
        }
    }

    // A widget that just left the tray must not stay on screen in a flyout, and
    // must not stay pinned as a preview either.
    let orphaned = shown_for().as_ref().is_some_and(|id| !keep.contains(id));
    drop(state);

    if orphaned {
        if pinned_for().as_ref().is_some_and(|id| !keep.contains(id)) {
            *pinned_for() = None;
        }
        hide_flyout(app);
        return;
    }

    if let Some((id, width, height)) = resize_shown {
        // Re-derive from the TRAY corner, not from the stored position: that is
        // the flyout's top-left, and re-clamping a grown panel against its own
        // old corner would walk it across the screen.
        if let (Some(anchor), Some(win)) = (tray_anchor(app), app.get_webview_window(FLYOUT_LABEL)) {
            let (position, size_px) = flyout_geometry(app, anchor, width, height);
            if let Err(e) = place_and_show(&win, &id, position, size_px) {
                log::warn!(target: "tray", "could not resize the flyout for {id}: {e}");
            }
        }
    }
}

/// Create one tray icon, wired to its widget id.
fn build_icon(
    app: &tauri::AppHandle,
    spec: &TraySpec,
    labels: &TrayLabels,
) -> Result<TrayIcon, String> {
    let menu = build_menu(app, &spec.id, labels)?;
    let click_id = spec.id.clone();
    TrayIconBuilder::with_id(spec.id.clone())
        .icon(Image::new_owned(
            spec.icon.clone(),
            spec.icon_size,
            spec.icon_size,
        ))
        .tooltip(&spec.tooltip)
        .menu(&menu)
        // Left click opens the widget; the menu belongs to the RIGHT button.
        .show_menu_on_left_click(false)
        .on_menu_event(|app, event| {
            let app = app.clone();
            let id = event.id().0.clone();
            // Runs on the event loop: a panic here would unwind through wry's
            // window procedure, so it stops at this boundary (CLAUDE.md).
            let _ = std::panic::catch_unwind(std::panic::AssertUnwindSafe(move || {
                on_menu_event(&app, &id);
            }));
        })
        .on_tray_icon_event(move |tray, event| {
            let app = tray.app_handle().clone();
            let id = click_id.clone();
            let _ = std::panic::catch_unwind(std::panic::AssertUnwindSafe(move || {
                on_icon_event(&app, &id, &event);
            }));
        })
        .build(app)
        .map_err(|e| e.to_string())
}

/// Right-click menu for one icon. "Remove from tray" is deliberately absent:
/// the list is frontend-owned, so Settings > Tray is the one place it is
/// edited ([S-08]).
fn build_menu(
    app: &tauri::AppHandle,
    widget_id: &str,
    labels: &TrayLabels,
) -> Result<tauri::menu::Menu<tauri::Wry>, String> {
    use tauri::menu::{Menu, MenuItem, PredefinedMenuItem};

    let open = MenuItem::with_id(
        app,
        format!("open:{widget_id}"),
        &labels.open,
        true,
        None::<&str>,
    )
    .map_err(|e| e.to_string())?;
    let settings = MenuItem::with_id(
        app,
        format!("settings:{widget_id}"),
        &labels.settings,
        true,
        None::<&str>,
    )
    .map_err(|e| e.to_string())?;
    let app_settings = MenuItem::with_id(
        app,
        "app-settings",
        &labels.app_settings,
        true,
        None::<&str>,
    )
    .map_err(|e| e.to_string())?;
    let sep = PredefinedMenuItem::separator(app).map_err(|e| e.to_string())?;
    let quit =
        MenuItem::with_id(app, "quit", &labels.quit, true, None::<&str>).map_err(|e| e.to_string())?;

    Menu::with_items(app, &[&open, &settings, &app_settings, &sep, &quit]).map_err(|e| e.to_string())
}

/// Route one tray menu click. Ids are `<action>[:<widgetId>]`.
fn on_menu_event(app: &tauri::AppHandle, id: &str) {
    match id.split_once(':') {
        Some(("open", widget_id)) => open_flyout_at_cursor(app, widget_id),
        Some(("settings", widget_id)) => {
            crate::show_config_window(app, FLYOUT_LABEL, Some(format!("widget:{widget_id}")));
        }
        _ => match id {
            "app-settings" => crate::show_config_window(app, FLYOUT_LABEL, None),
            "quit" => crate::request_quit(app),
            other => log::warn!(target: "tray", "unrecognised tray menu id: {other}"),
        },
    }
}

// ── Click handling ──────────────────────────────────────────────────────────

fn on_icon_event(app: &tauri::AppHandle, widget_id: &str, event: &TrayIconEvent) {
    match event {
        TrayIconEvent::Enter { .. } | TrayIconEvent::Move { .. } => {
            *hovered() = Some(widget_id.to_string());
        }
        TrayIconEvent::Leave { .. } => {
            let mut h = hovered();
            if h.as_deref() == Some(widget_id) {
                *h = None;
            }
        }
        TrayIconEvent::Click {
            button: MouseButton::Left,
            button_state: MouseButtonState::Up,
            position,
            ..
        } => {
            // [S-06] The blur this very click caused already hid the flyout and
            // armed the suppression, so the click closes instead of instantly
            // reopening. Any armed suppression is spent either way, so a stale
            // one cannot swallow a later, unrelated click.
            let suppressed = {
                let mut s = suppress_click();
                let hit = s.as_deref() == Some(widget_id);
                *s = None;
                hit
            };
            if suppressed {
                return;
            }
            show_flyout(app, widget_id, (position.x as i32, position.y as i32));
        }
        _ => {}
    }
}

/// Open a widget's flyout from the menu, where there is no click position to
/// anchor to: use the cursor, which is over the tray icon that opened the menu.
fn open_flyout_at_cursor(app: &tauri::AppHandle, widget_id: &str) {
    match cursor_position() {
        Some(pos) => show_flyout(app, widget_id, pos),
        None => log::warn!(target: "tray", "no cursor position; cannot anchor the flyout"),
    }
}

#[cfg(target_os = "windows")]
fn cursor_position() -> Option<(i32, i32)> {
    use winapi::shared::windef::POINT;
    use winapi::um::winuser::GetCursorPos;
    let mut p = POINT { x: 0, y: 0 };
    // SAFETY: `p` is a stack local written by the call; the return code is checked.
    if unsafe { GetCursorPos(&mut p) } == 0 {
        return None;
    }
    Some((p.x, p.y))
}

#[cfg(not(target_os = "windows"))]
fn cursor_position() -> Option<(i32, i32)> {
    None
}

/// Show `widget_id`'s flyout anchored at `cursor` ([S-05]).
///
/// Once the window exists this runs INLINE on the calling (event-loop) thread,
/// deliberately: the blur-to-hide handler runs there too, and a click that
/// blurs the open flyout raises both. Doing the show on a worker raced that
/// hide, so the panel could be hidden a moment after it was shown. Only the
/// first-ever build is handed to a worker, because `WebviewWindowBuilder::build`
/// waits on this thread's message pump and would deadlock.
fn show_flyout(app: &tauri::AppHandle, widget_id: &str, cursor: (i32, i32)) {
    let Some((width, height)) = tray_state()
        .iter()
        .find(|e| e.id == widget_id)
        .map(|e| (e.width, e.height))
    else {
        log::warn!(target: "tray", "click for unknown tray widget {widget_id}");
        return;
    };

    let report = |widget_id: &str, r: Result<(), String>| {
        if let Err(e) = r {
            log::error!(target: "tray", "could not open flyout for {widget_id}: {e}");
        }
    };

    if app.get_webview_window(FLYOUT_LABEL).is_some() {
        report(widget_id, present_flyout(app, widget_id, cursor, width, height));
        return;
    }

    let app = app.clone();
    let widget_id = widget_id.to_string();
    std::thread::spawn(move || {
        report(&widget_id, present_flyout(&app, &widget_id, cursor, width, height));
    });
}

/// Build (first time) or retarget (every later time) the one flyout window, then
/// show it — but only once the document that will be on screen is the right one
/// ([S-12]).
fn present_flyout(
    app: &tauri::AppHandle,
    widget_id: &str,
    cursor: (i32, i32),
    width: f64,
    height: f64,
) -> Result<(), String> {
    let (position, size_px) = flyout_geometry(app, cursor, width, height);

    let existing = app.get_webview_window(FLYOUT_LABEL);

    // Fast path: the live document is already this widget and nothing is
    // loading, so the panel is correct the instant it appears — place and show.
    if let Some(win) = &existing {
        if loaded_for().as_deref() == Some(widget_id) && pending().is_none() {
            return place_and_show(win, widget_id, position, size_px);
        }
    }

    // Already loading this widget: keep the one request, just re-aim it. A
    // second click must not start a second load.
    {
        let mut p = pending();
        if p.as_ref().is_some_and(|q| q.widget == widget_id) {
            *p = Some(Pending { widget: widget_id.to_string(), position, size_px });
            return Ok(());
        }
    }

    // [S-12] A load is needed, so the panel stays HIDDEN until the new document
    // reports itself ready. Showing now would put the PREVIOUS widget on screen
    // for as long as the navigation takes — the flash this exists to prevent.
    if let Some(win) = &existing {
        hide_flyout_window(win);
    }
    *pending() = Some(Pending { widget: widget_id.to_string(), position, size_px });

    let built = match existing {
        Some(win) => {
            retarget_flyout(&win, widget_id)?;
            None
        }
        None => Some(build_flyout_window(app, widget_id, width, height)?),
    };
    // A fresh window's document may already have reported ready while we were
    // still inside `build()`, in which case nothing is left to wake it up.
    if let Some(win) = built {
        if pending().as_ref().is_some_and(|p| p.widget == widget_id)
            && loaded_for().as_deref() == Some(widget_id)
        {
            let p = pending().take();
            if let Some(p) = p {
                return place_and_show(&win, widget_id, p.position, p.size_px);
            }
        }
    }
    Ok(())
}

/// Where and how big the flyout goes for an anchor point, both in PHYSICAL px
/// ([S-15]).
///
/// The one place the two are derived, and they are derived TOGETHER on purpose:
/// the placement clamps the panel against the work-area edges, so it is only
/// correct if the window really is the size the clamp assumed. Sizing logically
/// while placing physically broke exactly that — a logical size resolves against
/// whatever monitor the window is currently on, not the one it is about to move
/// to, so on a scaled display the panel came out wider than the clamp allowed
/// for and hung off the right edge.
fn flyout_geometry(
    app: &tauri::AppHandle,
    anchor: (i32, i32),
    width: f64,
    height: f64,
) -> ((i32, i32), (i32, i32)) {
    let scale = app
        .monitor_from_point(anchor.0 as f64, anchor.1 as f64)
        .ok()
        .flatten()
        .map(|m| m.scale_factor())
        .unwrap_or(1.0);
    let size_px = (
        (width * scale).round() as i32,
        (height * scale).round() as i32,
    );
    // [S-16] The margin scales with the panel, so it looks the same at 100% and
    // at 200% instead of thinning out as the display scales up.
    let gap = (FLYOUT_GAP * scale).round() as i32;
    // No work area (non-Windows, or a shell that would not answer) → treat the
    // anchor as the bottom-right of a nominal desktop, which still puts the
    // flyout up and to the left of it instead of off-screen.
    let work = work_area_at(anchor.0, anchor.1).unwrap_or(WorkArea {
        left: anchor.0 - size_px.0 - gap * 2,
        top: anchor.1 - size_px.1 - gap * 2,
        right: anchor.0,
        bottom: anchor.1,
    });
    (flyout_position(work, anchor, size_px, gap), size_px)
}

/// Size, place, show and focus the flyout. The one place the panel becomes
/// visible, so "visible" and `SHOWN_FOR`/`SHOWN_ANCHOR` cannot drift apart.
///
/// Position first, then size, and both in physical px ([S-15]): moving a window
/// across a DPI boundary can rescale it, so the size is applied once it is
/// already on the monitor it was measured against.
fn place_and_show(
    win: &tauri::WebviewWindow,
    widget_id: &str,
    position: (i32, i32),
    size_px: (i32, i32),
) -> Result<(), String> {
    win.set_position(tauri::PhysicalPosition::new(position.0, position.1))
        .map_err(|e| format!("set_position failed: {e}"))?;
    win.set_size(tauri::PhysicalSize::new(size_px.0, size_px.1))
        .map_err(|e| format!("set_size failed: {e}"))?;
    *shown_for() = Some(widget_id.to_string());
    *shown_anchor() = Some(position);
    win.show().map_err(|e| format!("show failed: {e}"))?;
    // The flyout is a keyboard surface (a tray Notes widget exists to be typed
    // in), so it takes focus even though every other Themia window avoids it.
    // Focus is also what arms the blur-to-close.
    let _ = win.set_focus();
    Ok(())
}

/// Point the existing flyout at another widget ([S-05]).
///
/// It RELOADS the page with the new id in the query, rather than telling the
/// live page to swap targets over an event. The window's target is then always
/// whatever its URL says — the same single path that the first open takes — so
/// there is no second, event-driven way for the panel to be showing one widget
/// while the tray thinks it is showing another. (It was: the event raced the
/// hidden webview's throttled renderer, and the flyout kept the previous
/// widget.) A reload of an already-warm page costs far less than that class of
/// bug, and reopening the SAME icon skips it entirely.
fn retarget_flyout(win: &tauri::WebviewWindow, widget_id: &str) -> Result<(), String> {
    if loaded_for().as_deref() == Some(widget_id) {
        return Ok(());
    }
    // Take the origin from the live window rather than reconstructing it: the
    // custom-protocol origin differs per platform and per Tauri version.
    let mut url = win.url().map_err(|e| format!("flyout url unavailable: {e}"))?;
    url.set_query(Some(&format!("trayflyout=1&widget={widget_id}")));
    win.navigate(url)
        .map_err(|e| format!("flyout navigate failed: {e}"))?;
    *loaded_for() = Some(widget_id.to_string());
    Ok(())
}

fn build_flyout_window(
    app: &tauri::AppHandle,
    widget_id: &str,
    width: f64,
    height: f64,
) -> Result<tauri::WebviewWindow, String> {
    let url = format!("index.html?trayflyout=1&widget={widget_id}");
    let win =
        tauri::WebviewWindowBuilder::new(app, FLYOUT_LABEL, tauri::WebviewUrl::App(url.into()))
            .additional_browser_args(crate::WEBVIEW_BROWSER_ARGS)
            .title("Themia")
            .decorations(false)
            .resizable(false)
            .skip_taskbar(true)
            .always_on_top(true)
            .shadow(true)
            .visible(false)
            .disable_drag_drop_handler()
            .inner_size(width, height)
            .build()
            .map_err(|e| format!("failed to build the tray flyout window: {e}"))?;
    *loaded_for() = Some(widget_id.to_string());

    // Hide on focus loss ([S-06]), and remember whether the cursor was over the
    // owning tray icon at that moment: if it was, this blur IS the user clicking
    // that icon again, whose click event arrives next and must close, not reopen.
    let app_for_event = app.clone();
    win.on_window_event(move |event| {
        if !matches!(event, tauri::WindowEvent::Focused(false)) {
            return;
        }
        // [S-13] A pinned preview is meant to stay up while the user works in
        // the settings window, which is exactly what took the focus away.
        if pinned_for().is_some() {
            return;
        }
        let showing = shown_for().clone();
        if let Some(id) = showing {
            if hovered().as_deref() == Some(id.as_str()) {
                *suppress_click() = Some(id);
            }
        }
        hide_flyout(&app_for_event);
    });

    // Rounded corners to match the app's card surfaces; a borderless WebView2
    // window is square by default.
    #[cfg(target_os = "windows")]
    match win.hwnd() {
        Ok(h) => round_window_corners(h.0 as usize as winapi::shared::windef::HWND),
        Err(e) => log::warn!(target: "tray", "flyout has no hwnd, corners stay square: {e}"),
    }

    Ok(win)
}

/// Hide the flyout and forget what it was showing, dropping any load that was
/// still on its way to being shown. Idempotent.
fn hide_flyout(app: &tauri::AppHandle) {
    *pending() = None;
    match app.get_webview_window(FLYOUT_LABEL) {
        Some(win) => hide_flyout_window(&win),
        None => {
            *shown_for() = None;
            *shown_anchor() = None;
        }
    }
}

/// The hide itself, for callers that already hold the window. Does NOT touch
/// `PENDING`: `present_flyout` hides the stale document precisely so it can then
/// arm a pending show.
fn hide_flyout_window(win: &tauri::WebviewWindow) {
    *shown_for() = None;
    *shown_anchor() = None;
    if let Err(e) = win.hide() {
        log::warn!(target: "tray", "flyout hide failed: {e}");
    }
}

/// Opt the flyout into the shell's rounded-corner treatment. Undocumented
/// before build 22000 and simply ignored there, so the failure mode is square
/// corners, not a failed window.
#[cfg(target_os = "windows")]
fn round_window_corners(hwnd: winapi::shared::windef::HWND) {
    use winapi::um::dwmapi::DwmSetWindowAttribute;
    const DWMWA_WINDOW_CORNER_PREFERENCE: u32 = 33;
    const DWMWCP_ROUND: u32 = 2;
    if hwnd.is_null() {
        return;
    }
    let pref: u32 = DWMWCP_ROUND;
    // SAFETY: live HWND owned by a Tauri window; `pref` outlives the call.
    unsafe {
        DwmSetWindowAttribute(
            hwnd,
            DWMWA_WINDOW_CORNER_PREFERENCE,
            &pref as *const u32 as *const winapi::ctypes::c_void,
            std::mem::size_of::<u32>() as u32,
        );
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    const WORK: WorkArea = WorkArea { left: 0, top: 0, right: 1920, bottom: 1040 };

    #[test]
    fn s05_bottom_taskbar_hangs_the_flyout_above_the_work_area() {
        // A click in the tray of a bottom-docked taskbar lands BELOW rcWork.
        // Far enough from the right edge that the clamp does not engage, so this
        // asserts the placement itself rather than the clamp below.
        let (x, y) = flyout_position(WORK, (1700, 1060), (300, 400), 8);
        assert_eq!(y, 1040 - 400 - 8);
        assert_eq!(x, 1700 - 150, "centred on the click along the free axis");
    }

    #[test]
    fn s05_clamps_inside_the_work_area_near_a_corner() {
        let (x, y) = flyout_position(WORK, (1915, 1060), (300, 400), 8);
        assert_eq!(x, 1920 - 300 - 8, "must not hang off the right edge");
        assert!(y >= WORK.top + 8);
    }

    #[test]
    fn s05_top_taskbar_drops_the_flyout_below_it() {
        let work = WorkArea { left: 0, top: 40, right: 1920, bottom: 1080 };
        let (_, y) = flyout_position(work, (1800, 10), (300, 400), 8);
        assert_eq!(y, 40 + 8);
    }

    #[test]
    fn s05_right_taskbar_hangs_the_flyout_off_the_right_edge() {
        let work = WorkArea { left: 0, top: 0, right: 1860, bottom: 1080 };
        let (x, y) = flyout_position(work, (1900, 500), (300, 400), 8);
        assert_eq!(x, 1860 - 300 - 8);
        assert_eq!(y, 500 - 200, "centred on the click, not pinned to an edge");
    }

    #[test]
    fn s05_oversized_flyout_lands_at_the_work_area_origin() {
        // Clamp order matters: the low edge wins, so a flyout bigger than the
        // screen is merely cropped at the bottom-right, never inverted.
        let (x, y) = flyout_position(WORK, (960, 1060), (4000, 4000), 8);
        assert_eq!((x, y), (WORK.left + 8, WORK.top + 8));
    }

    #[test]
    fn s16_the_panel_keeps_a_margin_off_both_clamped_edges() {
        // A bottom-right tray: the panel must stand off the right edge AND off
        // the taskbar, not sit flush in the corner.
        let monitor = WorkArea { left: 0, top: 0, right: 1920, bottom: 1080 };
        let work = WorkArea { left: 0, top: 0, right: 1920, bottom: 1040 };
        let anchor = tray_anchor_point(monitor, work);
        let gap = 12;
        let (x, y) = flyout_position(work, anchor, (300, 400), gap);
        assert_eq!(work.right - (x + 300), gap, "gap on the right");
        assert_eq!(work.bottom - (y + 400), gap, "gap above the taskbar");
    }

    #[test]
    fn s16_the_margin_scales_with_the_display() {
        // The gap is authored in logical px and converted like the size is, so a
        // 200% display gets a margin that looks the same, not one that halves.
        let logical_gap = FLYOUT_GAP;
        assert_eq!((logical_gap * 1.0).round() as i32, 12);
        assert_eq!((logical_gap * 1.5).round() as i32, 18);
        assert_eq!((logical_gap * 2.0).round() as i32, 24);
    }

    #[test]
    fn s15_a_panel_wider_than_the_clamp_assumed_hangs_off_the_edge() {
        // Regression: the placement clamps against the work-area edges, so it is
        // only correct when the window really is the size the clamp was given.
        // The panel was placed for its LOGICAL size while the window took a
        // physical one, so at 150% it came out 1.5x wider than allowed for and
        // ran past the right edge — which is what a settings preview, always
        // clamped flush right, shows first.
        let work = WorkArea { left: 0, top: 0, right: 1920, bottom: 1040 };
        let anchor = tray_anchor_point(WorkArea { left: 0, top: 0, right: 1920, bottom: 1080 }, work);
        let logical = (300, 400);
        let physical = (450, 600); // the same panel at 150%

        // Placed for the size it actually is: inside the work area.
        let (x, _) = flyout_position(work, anchor, physical, 8);
        assert_eq!(x + physical.0, work.right - 8, "flush against the edge, not past it");

        // Placed for the logical size while really being the physical one: the
        // right edge overshoots by exactly the scale difference.
        let (bad_x, _) = flyout_position(work, anchor, logical, 8);
        assert!(bad_x + physical.0 > work.right, "the old pairing ran off-screen");
    }

    #[test]
    fn s13_tray_anchor_follows_the_docked_taskbar_edge() {
        let monitor = WorkArea { left: 0, top: 0, right: 1920, bottom: 1080 };
        // Bottom taskbar: just below the work area, at its right end.
        let bottom = WorkArea { left: 0, top: 0, right: 1920, bottom: 1040 };
        assert_eq!(tray_anchor_point(monitor, bottom), (1919, 1041));
        // Top taskbar: just above.
        let top = WorkArea { left: 0, top: 40, right: 1920, bottom: 1080 };
        assert_eq!(tray_anchor_point(monitor, top), (1919, 39));
        // Right taskbar: just right of the work area.
        let right = WorkArea { left: 0, top: 0, right: 1860, bottom: 1080 };
        assert_eq!(tray_anchor_point(monitor, right), (1861, 1079));
        // Left taskbar: just left of it.
        let left = WorkArea { left: 60, top: 0, right: 1920, bottom: 1080 };
        assert_eq!(tray_anchor_point(monitor, left), (59, 1079));
    }

    #[test]
    fn s13_tray_anchor_falls_back_when_the_taskbar_covers_nothing() {
        // Auto-hidden taskbar: work area == monitor, so no edge is implied.
        let monitor = WorkArea { left: 0, top: 0, right: 1920, bottom: 1080 };
        assert_eq!(tray_anchor_point(monitor, monitor), (1919, 1079));
    }

    #[test]
    fn s13_a_bottom_taskbar_anchor_places_the_flyout_above_the_work_area() {
        // The anchor must feed `flyout_position` exactly like a real click does.
        let monitor = WorkArea { left: 0, top: 0, right: 1920, bottom: 1080 };
        let work = WorkArea { left: 0, top: 0, right: 1920, bottom: 1040 };
        let anchor = tray_anchor_point(monitor, work);
        let (x, y) = flyout_position(work, anchor, (300, 400), 8);
        assert_eq!(y, 1040 - 400 - 8);
        assert_eq!(x, 1920 - 300 - 8);
    }

    #[test]
    fn s02_icon_fingerprint_separates_size_from_pixels() {
        let px = vec![7u8; 16];
        assert_eq!(icon_fingerprint(&px, 16), icon_fingerprint(&px, 16));
        assert_ne!(icon_fingerprint(&px, 16), icon_fingerprint(&px, 24));
        assert_ne!(icon_fingerprint(&px, 16), icon_fingerprint(&[8u8; 16], 16));
    }
}
