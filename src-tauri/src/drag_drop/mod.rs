//! Native Windows OLE drag/drop infrastructure.
//!
//! Registers each Tauri webview HWND as an OLE drop target and provides
//! a Tauri command to initiate native drags from the frontend.

pub mod data_object;
pub mod drop_source;
pub mod drop_target;
pub mod formats;

use std::collections::HashMap;
use std::sync::{Arc, Mutex};
use std::sync::atomic::AtomicBool;

use windows::Win32::Foundation::*;
use windows::Win32::System::Ole::*;
use windows::Win32::System::Com::*;

use drop_target::ThemiaDropTarget;
use data_object::ThemiaDataObject;
use drop_source::ThemiaDropSource;

// ---------------------------------------------------------------------------
// Global registry: keeps IDropTarget alive while registered
// ---------------------------------------------------------------------------

#[allow(dead_code)]
struct SendTarget(IDropTarget, isize);
unsafe impl Send for SendTarget {}
unsafe impl Sync for SendTarget {}

static DROP_TARGETS: std::sync::OnceLock<Mutex<HashMap<String, SendTarget>>> =
    std::sync::OnceLock::new();

fn drop_targets() -> &'static Mutex<HashMap<String, SendTarget>> {
    DROP_TARGETS.get_or_init(|| Mutex::new(HashMap::new()))
}

// Per-window zone state: true when cursor is over a widget drop zone
static ZONE_STATES: std::sync::OnceLock<Mutex<HashMap<String, Arc<AtomicBool>>>> =
    std::sync::OnceLock::new();

fn zone_states() -> &'static Mutex<HashMap<String, Arc<AtomicBool>>> {
    ZONE_STATES.get_or_init(|| Mutex::new(HashMap::new()))
}

// Per-window drop-target origin/DPI, shared with the live ThemiaDropTarget so a
// display change can refresh the screen→client mapping without re-registering.
static DROP_POSITIONS: std::sync::OnceLock<Mutex<HashMap<String, Arc<drop_target::DropTargetPosition>>>> =
    std::sync::OnceLock::new();

fn drop_positions() -> &'static Mutex<HashMap<String, Arc<drop_target::DropTargetPosition>>> {
    DROP_POSITIONS.get_or_init(|| Mutex::new(HashMap::new()))
}

/// Lock a mutex, recovering the guard if a previous holder panicked and poisoned
/// it. These locks are reached from the OLE COM callbacks (IDropTarget::Drop /
/// DragOver), where unwinding a `.unwrap()` panic across the FFI boundary is
/// undefined behavior, so a poisoned lock must recover, never panic. (R2 / 1.15)
pub(crate) fn lock_safe<T>(m: &Mutex<T>) -> std::sync::MutexGuard<'_, T> {
    m.lock().unwrap_or_else(|e| e.into_inner())
}

/// Initialize OLE exactly once for the process.
///
/// Every drag/drop entry point here runs on the Tauri main/UI thread (drop-target
/// registration, the clipboard virtual-file extraction command, and the
/// `run_on_main_thread` body that calls `DoDragDrop`), so a single per-process
/// `OleInitialize` on that thread is sufficient: `OleInitialize` is idempotent
/// per thread, but the previous ad-hoc triple-init left that invariant
/// undocumented and re-initialized redundantly.
///
/// We deliberately never pair this with `OleUninitialize`: OLE stays initialized
/// for the process lifetime and is torn down by process exit, matching how the
/// rest of the app treats per-process COM/OLE. A premature `OleUninitialize`
/// could break an in-flight drag, so there is no balance to keep. (2.8)
fn ensure_ole_init() {
    static OLE_INIT: std::sync::Once = std::sync::Once::new();
    OLE_INIT.call_once(|| unsafe {
        if let Err(e) = OleInitialize(None) {
            log::error!(target: "drag_drop", "OleInitialize failed: {e}");
        }
    });
}

// ---------------------------------------------------------------------------
// Find the WebView2 content child HWND
// ---------------------------------------------------------------------------

/// Enumerate ALL descendant HWNDs of `parent`, revoke every OLE drop target,
/// and return `Chrome_RenderWidgetHostHWND`, the WebView2 content surface
/// that covers the window area and receives OLE drag routing.
unsafe fn find_and_revoke_webview_drop_target(parent: HWND) -> Option<HWND> {
    use winapi::um::winuser::EnumChildWindows;

    let mut children: Vec<HWND> = Vec::new();

    unsafe extern "system" fn enum_cb(
        hwnd: winapi::shared::windef::HWND,
        lparam: winapi::shared::minwindef::LPARAM,
    ) -> i32 {
        let list = &mut *(lparam as *mut Vec<HWND>);
        list.push(HWND(hwnd as *mut _));
        1
    }

    EnumChildWindows(
        parent.0 as winapi::shared::windef::HWND,
        Some(enum_cb),
        &mut children as *mut Vec<HWND> as winapi::shared::minwindef::LPARAM,
    );

    log::debug!(target: "drag_drop", "parent HWND {:?} has {} descendant(s)", parent, children.len());

    // Revoke ALL drop targets, then pick the best candidate BY CLASS.
    let mut candidates: Vec<(String, bool)> = Vec::with_capacity(children.len());

    for child in &children {
        let mut buf = [0u16; 256];
        let len = winapi::um::winuser::GetClassNameW(child.0 as _, buf.as_mut_ptr(), 256);
        let class = if len > 0 {
            String::from_utf16_lossy(&buf[..len as usize])
        } else {
            "<unknown>".to_string()
        };
        let had_target = RevokeDragDrop(*child).is_ok();
        log::debug!(target: "drag_drop", "  child {:?} class={class} had_drop_target={had_target}", child);
        candidates.push((class, had_target));
    }

    // Also revoke the parent
    let parent_had = RevokeDragDrop(parent).is_ok();
    log::debug!(target: "drag_drop", "  parent {:?} had_drop_target={parent_had}", parent);

    let result = pick_drop_target(&candidates)
        .map(|i| children[i])
        .or(if parent_had { Some(parent) } else { None });
    log::debug!(target: "drag_drop", "  chosen target: {:?}", result);
    result
}

/// Preference for a candidate child window class, lower = better. `None` means
/// the class must NEVER be chosen.
///
/// WHY THIS IS A RANKING AND NOT "the last one that had a target": WebView2's
/// window tree differs per monitor / per composition mode. On a machine with two
/// displays we logged monitor-0 as `[WRY_WEBVIEW(no), Chrome_WidgetWin_0(YES),
/// Chrome_WidgetWin_1(no), Chrome_RenderWidgetHostHWND(no)]` and monitor-1 as
/// the same PLUS an `Intermediate D3D Window(YES)` at the end. Picking the last
/// child that had a drop target therefore selected the D3D surface on
/// monitor-1. That surface is a DirectComposition target: it can carry a
/// registered `IDropTarget` but never receives the drag, so every drop on that
/// monitor silently missed the app — no file drops, and a shortcut dragged
/// inside its own widget fell through to the desktop instead of reordering.
fn drop_target_rank(class: &str) -> Option<u8> {
    match class {
        "Chrome_RenderWidgetHostHWND" => Some(0),
        "Chrome_WidgetWin_0" => Some(1),
        "Chrome_WidgetWin_1" => Some(2),
        "WRY_WEBVIEW" => Some(3),
        "Intermediate D3D Window" => None,
        _ => Some(4),
    }
}

/// Index of the child that should host our `IDropTarget`: the best-ranked class
/// among the children that actually had a drop target registered (ties go to the
/// first, so the choice does not depend on enumeration order). `None` when no
/// child is usable and the caller should fall back to the parent window.
fn pick_drop_target(candidates: &[(String, bool)]) -> Option<usize> {
    candidates
        .iter()
        .enumerate()
        .filter(|(_, (_, had_target))| *had_target)
        .filter_map(|(i, (class, _))| drop_target_rank(class).map(|r| (r, i)))
        .min_by_key(|(rank, i)| (*rank, *i))
        .map(|(_, i)| i)
}

#[cfg(test)]
mod drop_target_choice_tests {
    use super::{drop_target_rank, pick_drop_target};

    fn c(pairs: &[(&str, bool)]) -> Vec<(String, bool)> {
        pairs.iter().map(|(k, v)| ((*k).to_string(), *v)).collect()
    }

    /// The real monitor-0 tree: only Chrome_WidgetWin_0 carries a target.
    #[test]
    fn picks_the_webview_widget_window() {
        let tree = c(&[
            ("WRY_WEBVIEW", false),
            ("Chrome_WidgetWin_0", true),
            ("Chrome_WidgetWin_1", false),
            ("Chrome_RenderWidgetHostHWND", false),
        ]);
        assert_eq!(pick_drop_target(&tree), Some(1));
    }

    /// The real monitor-1 tree, which broke drag & drop on that display: the
    /// D3D composition surface also had a target and won on position alone.
    #[test]
    fn never_picks_the_intermediate_d3d_surface() {
        let tree = c(&[
            ("WRY_WEBVIEW", false),
            ("Chrome_WidgetWin_0", true),
            ("Chrome_WidgetWin_1", false),
            ("Chrome_RenderWidgetHostHWND", false),
            ("Intermediate D3D Window", true),
        ]);
        assert_eq!(pick_drop_target(&tree), Some(1));
        assert_eq!(drop_target_rank("Intermediate D3D Window"), None);
    }

    #[test]
    fn prefers_the_render_widget_host_when_it_has_one() {
        let tree = c(&[
            ("Chrome_WidgetWin_0", true),
            ("Chrome_RenderWidgetHostHWND", true),
        ]);
        assert_eq!(pick_drop_target(&tree), Some(1));
    }

    #[test]
    fn falls_back_to_the_parent_when_only_unusable_children_have_targets() {
        let tree = c(&[("Intermediate D3D Window", true), ("WRY_WEBVIEW", false)]);
        assert_eq!(pick_drop_target(&tree), None);
    }

    #[test]
    fn ties_resolve_to_the_first_match_not_the_last() {
        let tree = c(&[("Chrome_WidgetWin_0", true), ("Chrome_WidgetWin_0", true)]);
        assert_eq!(pick_drop_target(&tree), Some(0));
    }
}

// ---------------------------------------------------------------------------
// Public API: register / revoke
// ---------------------------------------------------------------------------

pub fn register_drop_target(
    app: tauri::AppHandle,
    label: &str,
    parent_hwnd: isize,
    x: i32,
    y: i32,
    dpr: f64,
) -> Result<(), String> {
    ensure_ole_init();
    unsafe {
        let target_hwnd = find_and_revoke_webview_drop_target(HWND(parent_hwnd as *mut _))
            .unwrap_or_else(|| {
                log::warn!(target: "drag_drop", "no child with drop target found, using parent");
                HWND(parent_hwnd as *mut _)
            });

        log::info!(target: "drag_drop", "registering IDropTarget on HWND {:?} for {label}", target_hwnd);

        let over_zone = Arc::new(AtomicBool::new(false));
        let pos = Arc::new(drop_target::DropTargetPosition::new());
        pos.update(x, y, dpr);
        let inner = ThemiaDropTarget::new(app, label.to_string(), pos.clone(), over_zone.clone());
        let target: IDropTarget = inner.into();

        RegisterDragDrop(target_hwnd, &target)
            .map_err(|e| format!("RegisterDragDrop failed on {:?}: {e}", target_hwnd))?;

        log::info!(target: "drag_drop", "successfully registered for {label}");

        let hwnd_val = target_hwnd.0 as isize;
        lock_safe(drop_targets()).insert(label.to_string(), SendTarget(target, hwnd_val));
        lock_safe(zone_states()).insert(label.to_string(), over_zone);
        lock_safe(drop_positions()).insert(label.to_string(), pos);
        Ok(())
    }
}

pub fn revoke_drop_target(label: &str) {
    if let Some(entry) = lock_safe(drop_targets()).remove(label) {
        unsafe {
            let _ = RevokeDragDrop(HWND(entry.1 as *mut _));
        }
    }
    lock_safe(zone_states()).remove(label);
    lock_safe(drop_positions()).remove(label);
}

/// Refresh a registered drop target's window origin + DPI after the overlay has
/// moved (e.g. a display change). Without this the screen→client mapping and
/// widget-rect drop routing keep using stale coordinates. No-op if the label
/// has no registered target. Poison-safe.
pub fn update_drop_target_position(label: &str, x: i32, y: i32, dpr: f64) {
    if let Some(pos) = lock_safe(drop_positions()).get(label) {
        pos.update(x, y, dpr);
    }
}

// ---------------------------------------------------------------------------
// Tauri commands
// ---------------------------------------------------------------------------

#[tauri::command]
pub fn set_drag_over_zone(label: String, over_zone: bool) {
    if let Some(state) = lock_safe(zone_states()).get(&label) {
        state.store(over_zone, std::sync::atomic::Ordering::Relaxed);
    }
}

#[derive(serde::Deserialize)]
pub struct StartDragRequest {
    pub data_type: String,
    pub file_paths: Option<Vec<String>>,
    pub widget_json: Option<String>,
    pub effect_allowed: String,
}

#[derive(serde::Serialize)]
pub struct StartDragResult {
    pub effect: String,
}

/// Extract virtual files (FileGroupDescriptorW + FileContents) from the
/// Windows clipboard via OleGetClipboard.  The file data is stashed in
/// VIRTUAL_FILE_STASH and the caller should follow up with save_virtual_files.
/// Returns the list of virtual file names, or an empty list if not available.
#[tauri::command]
pub fn clipboard_extract_virtual_files() -> Vec<String> {
    ensure_ole_init();
    unsafe {
        let data_obj: IDataObject = match OleGetClipboard() {
            Ok(obj) => obj,
            Err(e) => {
                log::error!(target: "clipboard_virtual", "OleGetClipboard failed: {e}");
                return Vec::new();
            }
        };

        // Enumerate formats to find FileGroupDescriptorW / FileContents
        let mut file_descriptor_fmt: Option<FORMATETC> = None;
        let mut file_contents_cf: Option<u16> = None;

        if let Ok(enumerator) = data_obj.EnumFormatEtc(DATADIR_GET.0 as u32) {
            let mut fmt_buf = [FORMATETC::default(); 1];
            let mut fetched = 0u32;
            loop {
                let hr = enumerator.Next(&mut fmt_buf, Some(&mut fetched));
                if fetched == 0 || hr != S_OK { break; }
                let f = &fmt_buf[0];
                let mut buf = [0u16; 256];
                let len = windows::Win32::System::DataExchange::GetClipboardFormatNameW(
                    f.cfFormat as u32,
                    &mut buf,
                );
                if len > 0 {
                    let name = String::from_utf16_lossy(&buf[..len as usize]);
                    if name == "FileGroupDescriptorW" {
                        file_descriptor_fmt = Some(*f);
                    } else if name == "FileContents" {
                        file_contents_cf = Some(f.cfFormat);
                    }
                }
            }
        }

        if let Some(desc_fmt) = file_descriptor_fmt {
            drop_target::extract_virtual_files(&data_obj, &desc_fmt, file_contents_cf)
                .unwrap_or_default()
        } else {
            Vec::new()
        }
    }
}

#[tauri::command]
pub fn save_virtual_files(dest_dir: String) -> Result<Vec<String>, String> {
    let entries = drop_target::take_virtual_file_stash()
        .ok_or_else(|| "No virtual files pending".to_string())?;

    let dest = std::path::Path::new(&dest_dir);
    if !dest.is_dir() {
        return Err(format!("Destination is not a directory: {dest_dir}"));
    }

    let mut saved_paths = Vec::with_capacity(entries.len());
    for entry in entries {
        let target = dest.join(&entry.name);
        let final_path = unique_dest_path(&target);
        std::fs::write(&final_path, &entry.data)
            .map_err(|e| format!("Failed to write {}: {e}", final_path.display()))?;
        saved_paths.push(final_path.to_string_lossy().to_string());
    }
    Ok(saved_paths)
}

/// Generate a unique file path by appending " (2)", " (3)", etc. if the file exists.
fn unique_dest_path(path: &std::path::Path) -> std::path::PathBuf {
    if !path.exists() {
        return path.to_path_buf();
    }
    let stem = path.file_stem().unwrap_or_default().to_string_lossy().to_string();
    let ext = path.extension().map(|e| format!(".{}", e.to_string_lossy())).unwrap_or_default();
    let parent = path.parent().unwrap_or(path);
    for n in 2..=999 {
        let candidate = parent.join(format!("{stem} ({n}){ext}"));
        if !candidate.exists() {
            return candidate;
        }
    }
    path.to_path_buf()
}

#[tauri::command]
pub async fn start_native_drag(
    window: tauri::Window,
    request: StartDragRequest,
) -> Result<StartDragResult, String> {
    let (tx, rx) = tokio::sync::oneshot::channel();

    window
        .run_on_main_thread(move || {
            ensure_ole_init();

            let data_obj: IDataObject = match request.data_type.as_str() {
                "files" => {
                    let paths = request.file_paths.unwrap_or_default();
                    ThemiaDataObject::for_files(&paths)
                }
                "widget" => {
                    let json = request.widget_json.unwrap_or_default();
                    ThemiaDataObject::for_widget(&json)
                }
                _ => {
                    let _ = tx.send(Err("unknown data_type".to_string()));
                    return;
                }
            };

            let source = ThemiaDropSource::new();

            let allowed = match request.effect_allowed.as_str() {
                "copy" => DROPEFFECT_COPY,
                "move" => DROPEFFECT_MOVE,
                "copyMove" => DROPEFFECT_COPY | DROPEFFECT_MOVE,
                _ => DROPEFFECT_COPY | DROPEFFECT_MOVE,
            };

            let mut result_effect = DROPEFFECT_NONE;

            let hr = unsafe {
                DoDragDrop(&data_obj, &source, allowed, &mut result_effect)
            };

            let effect_str = if hr == DRAGDROP_S_DROP {
                if result_effect.contains(DROPEFFECT_MOVE) {
                    "move"
                } else if result_effect.contains(DROPEFFECT_COPY) {
                    "copy"
                } else if result_effect.contains(DROPEFFECT_LINK) {
                    "link"
                } else {
                    "none"
                }
            } else {
                "none"
            };

            let _ = tx.send(Ok(StartDragResult {
                effect: effect_str.to_string(),
            }));
        })
        .map_err(|e| e.to_string())?;

    rx.await.map_err(|_| "drag thread dropped".to_string())?
}
