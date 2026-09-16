//! IDropTarget COM implementation.
//!
//! Registered on each Tauri webview HWND via RegisterDragDrop.
//! Forwards OLE drag events to the frontend as Tauri events.

use windows::core::*;
use windows::Win32::Foundation::*;
use windows::Win32::System::Com::*;
use windows::Win32::System::Ole::*;
use windows::Win32::System::Memory::*;
use windows::Win32::System::SystemServices::MODIFIERKEYS_FLAGS;

use std::sync::Arc;
use std::sync::atomic::AtomicBool;

use super::formats;
use tauri::Emitter;

const MK_CONTROL: u32 = 0x0008;
const MK_SHIFT: u32 = 0x0004;

// ---------------------------------------------------------------------------
// Virtual file stash: filled in Drop(), consumed by save_virtual_files cmd
// ---------------------------------------------------------------------------

pub struct VirtualFileEntry {
    pub name: String,
    pub data: Vec<u8>,
}

static VIRTUAL_FILE_STASH: std::sync::Mutex<Option<Vec<VirtualFileEntry>>> =
    std::sync::Mutex::new(None);

pub fn take_virtual_file_stash() -> Option<Vec<VirtualFileEntry>> {
    super::lock_safe(&VIRTUAL_FILE_STASH).take()
}

/// Save stashed virtual files to a destination directory.
pub fn save_virtual_files_to(dest_dir: &str) -> std::result::Result<(), String> {
    let entries = take_virtual_file_stash()
        .ok_or_else(|| "No virtual files pending".to_string())?;
    let dest = std::path::Path::new(dest_dir);
    for entry in entries {
        let target = dest.join(&entry.name);
        let final_path = super::unique_dest_path(&target);
        std::fs::write(&final_path, &entry.data)
            .map_err(|e| format!("Failed to write {}: {e}", final_path.display()))?;
        log::info!(target: "drag_drop", "saved virtual file: {}", final_path.display());
    }
    Ok(())
}

/// Read FileGroupDescriptorW + FileContents from an IDataObject and stash the data.
/// Returns the list of file names on success.
pub unsafe fn extract_virtual_files(
    data_obj: &IDataObject,
    desc_fmt: &FORMATETC,
    file_contents_cf: Option<u16>,
) -> Option<Vec<String>> {
    let medium = match data_obj.GetData(desc_fmt) {
        Ok(m) => m,
        Err(e) => {
            log::error!(target: "virtual_files", "FileGroupDescriptorW GetData failed: {e}");
            return None;
        }
    };

    let hg = medium.u.hGlobal;
    let size = GlobalSize(hg);
    let ptr = GlobalLock(hg);
    let descriptors = if !ptr.is_null() {
        let result = formats::parse_file_group_descriptor(ptr, size);
        GlobalUnlock(hg).ok();
        result
    } else {
        Vec::new()
    };
    ReleaseStgMedium(&medium as *const _ as *mut _);

    if descriptors.is_empty() {
        return None;
    }

    log::debug!(target: "virtual_files", "{} descriptors", descriptors.len());
    let mut entries = Vec::with_capacity(descriptors.len());
    let mut names = Vec::with_capacity(descriptors.len());

    let fc_cf = file_contents_cf.unwrap_or(formats::file_contents_format_id() as u16);
    for (i, desc) in descriptors.iter().enumerate() {
        let contents_fmt = FORMATETC {
            cfFormat: fc_cf,
            ptd: std::ptr::null_mut(),
            dwAspect: DVASPECT_CONTENT.0,
            lindex: i as i32,
            tymed: (TYMED_ISTREAM.0 | TYMED_HGLOBAL.0) as u32,
        };
        match data_obj.GetData(&contents_fmt) {
            Ok(mut cm) => {
                let tymed = TYMED(cm.tymed as i32);
                let cm_ptr = &mut cm as *mut STGMEDIUM;
                let data = if tymed == TYMED_ISTREAM {
                    let stream_ptr = std::ptr::addr_of!((*cm_ptr).u.pstm).read();
                    stream_ptr.as_ref().and_then(|s| formats::read_istream(s).ok())
                } else if tymed == TYMED_HGLOBAL {
                    let hg = std::ptr::addr_of!((*cm_ptr).u.hGlobal).read();
                    let size = GlobalSize(hg);
                    let ptr = GlobalLock(hg);
                    let result = if !ptr.is_null() && size > 0 {
                        Some(std::slice::from_raw_parts(ptr as *const u8, size).to_vec())
                    } else {
                        None
                    };
                    if !ptr.is_null() { GlobalUnlock(hg).ok(); }
                    result
                } else {
                    None
                };
                ReleaseStgMedium(cm_ptr);

                if let Some(data) = data {
                    log::debug!(target: "virtual_files", "  file[{}] \"{}\" {} bytes", i, desc.name, data.len());
                    names.push(desc.name.clone());
                    entries.push(VirtualFileEntry { name: desc.name.clone(), data });
                } else {
                    log::warn!(target: "virtual_files", "  file[{}] \"{}\": failed to read contents", i, desc.name);
                }
            }
            Err(e) => {
                log::error!(target: "virtual_files", "  file[{}] \"{}\": GetData failed: {e}", i, desc.name);
            }
        }
    }

    if !entries.is_empty() {
        *super::lock_safe(&VIRTUAL_FILE_STASH) = Some(entries);
        Some(names)
    } else {
        None
    }
}

// ---------------------------------------------------------------------------
// Event payload sent to the frontend
// ---------------------------------------------------------------------------

#[derive(serde::Serialize, Clone)]
#[serde(rename_all = "camelCase")]
pub struct DragEvent {
    pub event_type: String,
    pub screen_x: i32,
    pub screen_y: i32,
    pub client_x: f64,
    pub client_y: f64,
    pub data_types: Vec<String>,
    pub effect: String,
    pub widget_data: Option<String>,
    pub file_paths: Option<Vec<String>>,
    pub virtual_file_names: Option<Vec<String>>,
    pub urls: Option<Vec<String>>,
    pub ctrl_held: bool,
}

// ---------------------------------------------------------------------------
// IDropTarget implementation
// ---------------------------------------------------------------------------

/// Window origin + DPI used to map screen drag coords into client space.
/// Shared (via `Arc`) between the live `ThemiaDropTarget` and the registry in
/// `mod.rs` so a display change can refresh it without rebuilding the target.
pub struct DropTargetPosition {
    win_x: std::sync::atomic::AtomicI32,
    win_y: std::sync::atomic::AtomicI32,
    dpr_x1000: std::sync::atomic::AtomicU32,
}

impl Default for DropTargetPosition {
    fn default() -> Self {
        DropTargetPosition {
            win_x: std::sync::atomic::AtomicI32::new(0),
            win_y: std::sync::atomic::AtomicI32::new(0),
            dpr_x1000: std::sync::atomic::AtomicU32::new(1000),
        }
    }
}

impl DropTargetPosition {
    pub fn new() -> Self {
        Self::default()
    }

    pub fn update(&self, x: i32, y: i32, dpr: f64) {
        self.win_x.store(x, std::sync::atomic::Ordering::Relaxed);
        self.win_y.store(y, std::sync::atomic::Ordering::Relaxed);
        self.dpr_x1000.store((dpr * 1000.0) as u32, std::sync::atomic::Ordering::Relaxed);
    }
}

#[implement(IDropTarget)]
pub struct ThemiaDropTarget {
    app_handle: tauri::AppHandle,
    window_label: String,
    pos: Arc<DropTargetPosition>,
    over_zone: Arc<AtomicBool>,
}

impl ThemiaDropTarget {
    pub fn new(
        app: tauri::AppHandle,
        label: String,
        pos: Arc<DropTargetPosition>,
        over_zone: Arc<AtomicBool>,
    ) -> Self {
        ThemiaDropTarget {
            app_handle: app,
            window_label: label,
            pos,
            over_zone,
        }
    }

    fn screen_to_client(&self, screen_x: i32, screen_y: i32) -> (f64, f64) {
        let wx = self.pos.win_x.load(std::sync::atomic::Ordering::Relaxed);
        let wy = self.pos.win_y.load(std::sync::atomic::Ordering::Relaxed);
        let dpr = self.pos.dpr_x1000.load(std::sync::atomic::Ordering::Relaxed) as f64 / 1000.0;
        let cx = (screen_x - wx) as f64 / dpr;
        let cy = (screen_y - wy) as f64 / dpr;
        (cx, cy)
    }

    /// True when screen point `pt` lands inside one of this window's widget
    /// rects. The rects map is keyed by HWND and stored in physical client px,
    /// so convert the screen point to physical client coords (no DPI divide)
    /// before testing.
    #[cfg(target_os = "windows")]
    fn point_over_widget_rect(&self, pt: &POINTL) -> bool {
        use tauri::Manager;
        let wx = self.pos.win_x.load(std::sync::atomic::Ordering::Relaxed);
        let wy = self.pos.win_y.load(std::sync::atomic::Ordering::Relaxed);
        let px = pt.x - wx;
        let py = pt.y - wy;
        match self
            .app_handle
            .get_webview_window(&self.window_label)
            .and_then(|w| w.hwnd().ok())
        {
            Some(h) => crate::point_in_widget_rect(h.0 as usize, px, py),
            None => false,
        }
    }

    fn query_data_types(data_obj: &IDataObject) -> Vec<String> {
        let mut types = Vec::new();
        let hdrop_fmt = formats::hdrop_format();
        if unsafe { data_obj.QueryGetData(&hdrop_fmt) } == S_OK {
            types.push("files".to_string());
        }
        let widget_fmt = formats::widget_format();
        if unsafe { data_obj.QueryGetData(&widget_fmt) } == S_OK {
            types.push("widget".to_string());
        }
        let url_fmt = formats::url_format();
        if unsafe { data_obj.QueryGetData(&url_fmt) } == S_OK {
            types.push("url".to_string());
        }
        let vfile_fmt = formats::file_group_descriptor_format();
        if unsafe { data_obj.QueryGetData(&vfile_fmt) } == S_OK {
            types.push("virtual_files".to_string());
        } else {
            // Fallback: enumerate formats and check by name, since format IDs
            // may differ from our registered ones
            let found = unsafe {
                if let Ok(enumerator) = data_obj.EnumFormatEtc(DATADIR_GET.0 as u32) {
                    let mut fmt_buf = [FORMATETC::default(); 1];
                    let mut fetched = 0u32;
                    let mut result = false;
                    loop {
                        let hr = enumerator.Next(&mut fmt_buf, Some(&mut fetched));
                        if fetched == 0 || hr != S_OK { break; }
                        let mut buf = [0u16; 256];
                        let len = windows::Win32::System::DataExchange::GetClipboardFormatNameW(
                            fmt_buf[0].cfFormat as u32,
                            &mut buf,
                        );
                        if len > 0 {
                            let name = String::from_utf16_lossy(&buf[..len as usize]);
                            if name == "FileGroupDescriptorW" {
                                result = true;
                                break;
                            }
                        }
                    }
                    result
                } else {
                    false
                }
            };
            if found {
                types.push("virtual_files".to_string());
            }
        }
        types
    }

    fn effect_string(effect: DROPEFFECT) -> String {
        if effect.contains(DROPEFFECT_COPY) && effect.contains(DROPEFFECT_MOVE) {
            "copyMove".to_string()
        } else if effect.contains(DROPEFFECT_COPY) {
            "copy".to_string()
        } else if effect.contains(DROPEFFECT_MOVE) {
            "move".to_string()
        } else if effect.contains(DROPEFFECT_LINK) {
            "link".to_string()
        } else {
            "none".to_string()
        }
    }

    fn emit_event(&self, event: DragEvent) {
        let _ = self.app_handle.emit_to(&self.window_label, "native-drag", &event);
    }

    fn choose_effect(grfkeystate: MODIFIERKEYS_FLAGS, allowed: DROPEFFECT) -> DROPEFFECT {
        let ks = grfkeystate.0;
        let ctrl = (ks & MK_CONTROL) != 0;
        let shift = (ks & MK_SHIFT) != 0;

        if ctrl && shift && allowed.contains(DROPEFFECT_LINK) {
            DROPEFFECT_LINK
        } else if ctrl && allowed.contains(DROPEFFECT_COPY) {
            DROPEFFECT_COPY
        } else if allowed.contains(DROPEFFECT_MOVE) {
            // Shift requests MOVE, but MOVE is also the default when allowed,
            // so the two collapse to the same branch.
            DROPEFFECT_MOVE
        } else if allowed.contains(DROPEFFECT_COPY) {
            DROPEFFECT_COPY
        } else {
            DROPEFFECT_NONE
        }
    }
}

// ---------------------------------------------------------------------------
// Drop files to the user's Desktop folder
// ---------------------------------------------------------------------------

/// Get the user's Desktop folder path via SHGetKnownFolderPath.
fn get_desktop_path() -> Option<std::path::PathBuf> {
    use windows::Win32::UI::Shell::{SHGetKnownFolderPath, FOLDERID_Desktop, KF_FLAG_DEFAULT};

    unsafe {
        let path = SHGetKnownFolderPath(&FOLDERID_Desktop, KF_FLAG_DEFAULT, None).ok()?;
        let s = path.to_string().ok()?;
        Some(std::path::PathBuf::from(s))
    }
}

/// Copy or move files to the Desktop folder. Returns true on success.
fn drop_files_to_desktop(file_paths: &[String], do_move: bool) -> bool {
    let desktop = match get_desktop_path() {
        Some(p) => p,
        None => {
            log::error!(target: "drag_drop", "could not resolve Desktop folder path");
            return false;
        }
    };

    let mut ok = true;
    for src_str in file_paths {
        let src = std::path::Path::new(src_str);
        let file_name = match src.file_name() {
            Some(n) => n,
            None => continue,
        };
        let dest = super::unique_dest_path(&desktop.join(file_name));

        let result = if do_move {
            std::fs::rename(src, &dest).or_else(|_| {
                // rename fails across volumes; fall back to copy + delete
                std::fs::copy(src, &dest).and_then(|_| std::fs::remove_file(src))
            })
        } else {
            if src.is_dir() {
                copy_dir_recursive(src, &dest)
            } else {
                std::fs::copy(src, &dest).map(|_| ())
            }
        };

        match result {
            Ok(_) => log::info!(target: "drag_drop", "dropped {} → {}", src_str, dest.display()),
            Err(e) => {
                log::error!(target: "drag_drop", "failed to drop {} → {}: {e}", src_str, dest.display());
                ok = false;
            }
        }
    }

    // Notify Explorer so desktop icons refresh
    unsafe {
        use windows::Win32::UI::Shell::{SHChangeNotify, SHCNE_UPDATEDIR, SHCNF_PATHW};
        let wide: Vec<u16> = desktop.to_string_lossy().encode_utf16().chain(std::iter::once(0)).collect();
        SHChangeNotify(SHCNE_UPDATEDIR, SHCNF_PATHW, Some(wide.as_ptr() as *const _), None);
    }

    ok
}

fn copy_dir_recursive(src: &std::path::Path, dest: &std::path::Path) -> std::io::Result<()> {
    std::fs::create_dir_all(dest)?;
    for entry in std::fs::read_dir(src)? {
        let entry = entry?;
        let ty = entry.file_type()?;
        let target = dest.join(entry.file_name());
        if ty.is_dir() {
            copy_dir_recursive(&entry.path(), &target)?;
        } else {
            std::fs::copy(entry.path(), &target)?;
        }
    }
    Ok(())
}

impl IDropTarget_Impl for ThemiaDropTarget_Impl {
    fn DragEnter(
        &self,
        pdataobj: Ref<'_, IDataObject>,
        grfkeystate: MODIFIERKEYS_FLAGS,
        pt: &POINTL,
        pdweffect: *mut DROPEFFECT,
    ) -> Result<()> {
        // Start every drag from a known zone state. `over_zone` is only ever set
        // true by the async JS DragOver handler; without resetting it here a
        // stale `true` from a previous drag would misroute this drop. The JS
        // handler re-asserts true while genuinely over a widget zone.
        self.over_zone.store(false, std::sync::atomic::Ordering::Relaxed);

        let data_obj: IDataObject = pdataobj.clone().ok_or(E_INVALIDARG)?;
        let data_types = ThemiaDropTarget::query_data_types(&data_obj);

        if data_types.is_empty() {
            unsafe { *pdweffect = DROPEFFECT_NONE };
            return Ok(());
        }

        let allowed = unsafe { *pdweffect };
        let chosen = ThemiaDropTarget::choose_effect(grfkeystate, allowed);
        unsafe { *pdweffect = chosen };

        let (cx, cy) = self.screen_to_client(pt.x, pt.y);
        let ctrl = (grfkeystate.0 & MK_CONTROL) != 0;

        self.emit_event(DragEvent {
            event_type: "enter".to_string(),
            screen_x: pt.x,
            screen_y: pt.y,
            client_x: cx,
            client_y: cy,
            data_types,
            effect: ThemiaDropTarget::effect_string(chosen),
            widget_data: None,
            file_paths: None,
            virtual_file_names: None,
            urls: None,
            ctrl_held: ctrl,
        });
        Ok(())
    }

    fn DragOver(
        &self,
        grfkeystate: MODIFIERKEYS_FLAGS,
        pt: &POINTL,
        pdweffect: *mut DROPEFFECT,
    ) -> Result<()> {
        let allowed = unsafe { *pdweffect };
        let chosen = ThemiaDropTarget::choose_effect(grfkeystate, allowed);
        unsafe { *pdweffect = chosen };

        let (cx, cy) = self.screen_to_client(pt.x, pt.y);
        let ctrl = (grfkeystate.0 & MK_CONTROL) != 0;

        self.emit_event(DragEvent {
            event_type: "over".to_string(),
            screen_x: pt.x,
            screen_y: pt.y,
            client_x: cx,
            client_y: cy,
            data_types: vec![],
            effect: ThemiaDropTarget::effect_string(chosen),
            widget_data: None,
            file_paths: None,
            virtual_file_names: None,
            urls: None,
            ctrl_held: ctrl,
        });
        Ok(())
    }

    fn DragLeave(&self) -> Result<()> {
        // Clear zone state so a re-entered drag doesn't inherit a stale `true`.
        self.over_zone.store(false, std::sync::atomic::Ordering::Relaxed);
        self.emit_event(DragEvent {
            event_type: "leave".to_string(),
            screen_x: 0,
            screen_y: 0,
            client_x: 0.0,
            client_y: 0.0,
            data_types: vec![],
            effect: "none".to_string(),
            widget_data: None,
            file_paths: None,
            virtual_file_names: None,
            urls: None,
            ctrl_held: false,
        });
        Ok(())
    }

    fn Drop(
        &self,
        pdataobj: Ref<'_, IDataObject>,
        grfkeystate: MODIFIERKEYS_FLAGS,
        pt: &POINTL,
        pdweffect: *mut DROPEFFECT,
    ) -> Result<()> {
        let data_obj: IDataObject = pdataobj.clone().ok_or(E_INVALIDARG)?;
        let data_types = ThemiaDropTarget::query_data_types(&data_obj);

        let allowed = unsafe { *pdweffect };
        let chosen = ThemiaDropTarget::choose_effect(grfkeystate, allowed);
        unsafe { *pdweffect = chosen };

        // If not over a widget drop zone and the drag contains files or
        // virtual files, copy/move them to the Desktop folder.
        //
        // `over_zone` is set by an async JS invoke responding to DragOver, so a
        // quick drag→drop can reach here before it lands and read stale-false,
        // dumping a file drop onto the desktop instead of the widget under the
        // cursor. Cross-check the drop point against the window's widget rects
        // (the same source of truth that shapes the region) so a drop visibly
        // over a widget is always handled by the widget.
        let is_over_zone = self.over_zone.load(std::sync::atomic::Ordering::Relaxed)
            || self.point_over_widget_rect(pt);
        if !is_over_zone && data_types.iter().any(|t| t == "files" || t == "virtual_files") {
            // Try real files first (CF_HDROP)
            let mut handled = false;
            let file_paths = {
                let fmt = formats::hdrop_format();
                if unsafe { data_obj.QueryGetData(&fmt) } == S_OK {
                    unsafe {
                        match data_obj.GetData(&fmt) {
                            Ok(medium) => {
                                let hg = medium.u.hGlobal;
                                let ptr = GlobalLock(hg);
                                let paths = if !ptr.is_null() {
                                    let result = formats::parse_hdrop(ptr);
                                    GlobalUnlock(hg).ok();
                                    Some(result)
                                } else {
                                    None
                                };
                                ReleaseStgMedium(&medium as *const _ as *mut _);
                                paths
                            }
                            Err(_) => None,
                        }
                    }
                } else {
                    None
                }
            };

            if let Some(paths) = &file_paths {
                if !paths.is_empty() {
                    let do_move = chosen.contains(DROPEFFECT_MOVE);
                    log::info!(target: "drag_drop", "dropping {} file(s) to Desktop (move={})", paths.len(), do_move);
                    drop_files_to_desktop(paths, do_move);
                    handled = true;
                }
            }

            // Fall back to virtual files (e.g. images dragged from Chrome)
            if !handled {
                if let Some(desktop) = get_desktop_path() {
                    // Enumerate formats to find FileGroupDescriptorW / FileContents
                    let mut file_descriptor_fmt: Option<FORMATETC> = None;
                    let mut file_contents_cf: Option<u16> = None;
                    unsafe {
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
                    }

                    if let Some(desc_fmt) = file_descriptor_fmt {
                        let names = unsafe {
                            extract_virtual_files(&data_obj, &desc_fmt, file_contents_cf)
                        };
                        if let Some(names) = names {
                            log::info!(target: "drag_drop", "saving {} virtual file(s) to Desktop", names.len());
                            let dest_dir = desktop.to_string_lossy().to_string();
                            if let Err(e) = save_virtual_files_to(&dest_dir) {
                                log::error!(target: "drag_drop", "failed to save virtual files: {e}");
                            }
                            // Notify Explorer
                            unsafe {
                                use windows::Win32::UI::Shell::{SHChangeNotify, SHCNE_UPDATEDIR, SHCNF_PATHW};
                                let wide: Vec<u16> = desktop.to_string_lossy().encode_utf16()
                                    .chain(std::iter::once(0)).collect();
                                SHChangeNotify(SHCNE_UPDATEDIR, SHCNF_PATHW,
                                    Some(wide.as_ptr() as *const _), None);
                            }
                            handled = true;
                        }
                    }
                }
            }

            if handled {
                return Ok(());
            }
        }

        let (cx, cy) = self.screen_to_client(pt.x, pt.y);
        let ctrl = (grfkeystate.0 & MK_CONTROL) != 0;

        // Enumerate formats to discover virtual file format IDs (which may
        // differ from our registered ones) and to find CF_HDROP.
        let mut has_file_descriptor = false;
        let mut file_descriptor_fmt: Option<FORMATETC> = None;
        let mut file_contents_cf: Option<u16> = None;
        unsafe {
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
                            has_file_descriptor = true;
                            file_descriptor_fmt = Some(*f);
                        } else if name == "FileContents" {
                            file_contents_cf = Some(f.cfFormat);
                        }
                    }
                }
            }
        };

        // Extract file paths via CF_HDROP
        let file_paths = {
            let fmt = formats::hdrop_format();
            if unsafe { data_obj.QueryGetData(&fmt) } == S_OK {
                unsafe {
                    match data_obj.GetData(&fmt) {
                        Ok(medium) => {
                            let hg = medium.u.hGlobal;
                            let ptr = GlobalLock(hg);
                            let paths = if !ptr.is_null() {
                                let result = formats::parse_hdrop(ptr);
                                GlobalUnlock(hg).ok();
                                Some(result)
                            } else {
                                None
                            };
                            ReleaseStgMedium(&medium as *const _ as *mut _);
                            paths
                        }
                        Err(_) => None,
                    }
                }
            } else {
                None
            }
        };

        // Extract widget data if available
        let widget_data = {
            let fmt = formats::widget_format();
            if unsafe { data_obj.QueryGetData(&fmt) } == S_OK {
                unsafe {
                    if let Ok(medium) = data_obj.GetData(&fmt) {
                        let hg = medium.u.hGlobal;
                        let ptr = GlobalLock(hg);
                        let data = if !ptr.is_null() {
                            let result = formats::parse_widget_data(ptr);
                            GlobalUnlock(hg).ok();
                            result
                        } else {
                            None
                        };
                        ReleaseStgMedium(&medium as *const _ as *mut _);
                        data
                    } else {
                        None
                    }
                }
            } else {
                None
            }
        };

        // Fallback: Chromium/Electron (VS Code) advertises CF_HDROP but fails
        // GetData; extract file paths from CF_UNICODETEXT instead.
        let file_paths = if file_paths.is_none() {
            let cf_unicodetext: u16 = 13;
            let text_fmt = FORMATETC {
                cfFormat: cf_unicodetext,
                ptd: std::ptr::null_mut(),
                dwAspect: DVASPECT_CONTENT.0,
                lindex: -1,
                tymed: TYMED_HGLOBAL.0 as u32,
            };
            let text = unsafe {
                if data_obj.QueryGetData(&text_fmt) == S_OK {
                    match data_obj.GetData(&text_fmt) {
                        Ok(medium) => {
                            let hg = medium.u.hGlobal;
                            let ptr = GlobalLock(hg);
                            let result = if !ptr.is_null() {
                                let wptr = ptr as *const u16;
                                let mut len = 0usize;
                                while *wptr.add(len) != 0 { len += 1; }
                                let s = String::from_utf16_lossy(std::slice::from_raw_parts(wptr, len));
                                GlobalUnlock(hg).ok();
                                Some(s)
                            } else {
                                None
                            };
                            ReleaseStgMedium(&medium as *const _ as *mut _);
                            result
                        }
                        Err(_) => None,
                    }
                } else {
                    None
                }
            };
            if let Some(text) = text {
                let paths: Vec<String> = text.lines()
                    .map(|line| line.trim())
                    .map(|line| {
                        if let Some(rest) = line.strip_prefix("file:///") {
                            url_decode(rest).replace('/', "\\")
                        } else {
                            line.to_string()
                        }
                    })
                    .filter(|p| !p.is_empty() && std::path::Path::new(p).exists())
                    .collect();
                if !paths.is_empty() { Some(paths) } else { None }
            } else {
                None
            }
        } else {
            file_paths
        };

        // Extract virtual files if no file paths were resolved
        let virtual_file_names = if file_paths.is_none() && has_file_descriptor {
            let desc_fmt = file_descriptor_fmt.unwrap();
            unsafe { extract_virtual_files(&data_obj, &desc_fmt, file_contents_cf) }
        } else {
            None
        };

        // Extract URL via CFSTR_INETURLW, or a plain http(s) string from
        // CF_UNICODETEXT as a fallback. We only consider a URL when no file
        // paths resolved, so real file drops don't get shadowed.
        let urls = if file_paths.is_none() {
            let fmt = formats::url_format();
            let primary = if unsafe { data_obj.QueryGetData(&fmt) } == S_OK {
                unsafe {
                    match data_obj.GetData(&fmt) {
                        Ok(medium) => {
                            let hg = medium.u.hGlobal;
                            let ptr = GlobalLock(hg);
                            let parsed = if !ptr.is_null() {
                                let r = formats::parse_url_data(ptr);
                                GlobalUnlock(hg).ok();
                                r
                            } else {
                                None
                            };
                            ReleaseStgMedium(&medium as *const _ as *mut _);
                            parsed.map(|u| vec![u])
                        }
                        Err(_) => None,
                    }
                }
            } else { None };

            if primary.is_some() {
                primary
            } else {
                let cf_unicodetext: u16 = 13;
                let text_fmt = FORMATETC {
                    cfFormat: cf_unicodetext,
                    ptd: std::ptr::null_mut(),
                    dwAspect: DVASPECT_CONTENT.0,
                    lindex: -1,
                    tymed: TYMED_HGLOBAL.0 as u32,
                };
                unsafe {
                    if data_obj.QueryGetData(&text_fmt) == S_OK {
                        match data_obj.GetData(&text_fmt) {
                            Ok(medium) => {
                                let hg = medium.u.hGlobal;
                                let ptr = GlobalLock(hg);
                                let out = if !ptr.is_null() {
                                    let wptr = ptr as *const u16;
                                    let mut len = 0usize;
                                    while *wptr.add(len) != 0 { len += 1; }
                                    let s = String::from_utf16_lossy(std::slice::from_raw_parts(wptr, len));
                                    GlobalUnlock(hg).ok();
                                    let t = s.trim().to_string();
                                    if t.starts_with("http://") || t.starts_with("https://") {
                                        Some(vec![t])
                                    } else { None }
                                } else { None };
                                ReleaseStgMedium(&medium as *const _ as *mut _);
                                out
                            }
                            Err(_) => None,
                        }
                    } else { None }
                }
            }
        } else { None };

        self.emit_event(DragEvent {
            event_type: "drop".to_string(),
            screen_x: pt.x,
            screen_y: pt.y,
            client_x: cx,
            client_y: cy,
            data_types,
            effect: ThemiaDropTarget::effect_string(chosen),
            widget_data,
            file_paths,
            virtual_file_names,
            urls,
            ctrl_held: ctrl,
        });
        Ok(())
    }
}

/// Decode percent-encoded URL characters (e.g. %20 → space).
///
/// Accumulates raw BYTES (each `%XX` escape contributes the parsed byte, every
/// other input byte is kept verbatim) then interprets the whole buffer as
/// UTF-8 once. Decoding per-char as Latin-1 would corrupt multi-byte UTF-8
/// paths (e.g. `M%C3%BCller` must come back as `Müller`, not `MÃ¼ller`).
fn url_decode(input: &str) -> String {
    let mut bytes: Vec<u8> = Vec::with_capacity(input.len());
    let mut iter = input.bytes();
    while let Some(b) = iter.next() {
        if b == b'%' {
            let hi = iter.next();
            let lo = iter.next();
            if let (Some(h), Some(l)) = (hi, lo) {
                if let Ok(byte) = u8::from_str_radix(
                    &format!("{}{}", h as char, l as char), 16,
                ) {
                    bytes.push(byte);
                    continue;
                }
            }
            bytes.push(b'%');
            if let Some(h) = hi { bytes.push(h); }
            if let Some(l) = lo { bytes.push(l); }
        } else {
            bytes.push(b);
        }
    }
    String::from_utf8_lossy(&bytes).into_owned()
}

#[cfg(test)]
mod tests {
    use super::*;

    /// A percent-encoded UTF-8 path must decode back to its real characters.
    /// `file:///C:/M%C3%BCller.txt` → the `rest` `C:/Müller.txt`. The two bytes
    /// `%C3%BC` are the UTF-8 encoding of `ü`; decoding them per-char as Latin-1
    /// would yield the mojibake `MÃ¼ller`.
    #[test]
    fn url_decode_preserves_utf8_path() {
        let rest = "C:/M%C3%BCller.txt";
        assert_eq!(url_decode(rest), "C:/Müller.txt");

        // The full drop pipeline strips "file:///" then back-slashes the path.
        let line = "file:///C:/M%C3%BCller.txt";
        let path = url_decode(line.strip_prefix("file:///").unwrap()).replace('/', "\\");
        assert_eq!(path, "C:\\Müller.txt");
    }

    /// ASCII escapes (e.g. %20 → space) still decode correctly and plain text
    /// passes through unchanged.
    #[test]
    fn url_decode_handles_ascii_and_plain() {
        assert_eq!(url_decode("a%20b"), "a b");
        assert_eq!(url_decode("plain.txt"), "plain.txt");
    }
}
