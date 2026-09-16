use std::process::Command;
use std::sync::atomic::{AtomicUsize, Ordering};
use tauri::AppHandle;

// ──────────────────────────────────────────────────────────────────────────────
// Debug counters: track concurrent shell-API batches. High values indicate
// thread/request pile-up (cloud-sync watcher thrash, multiple folder widgets
// fanning out simultaneously, etc.).
// ──────────────────────────────────────────────────────────────────────────────
static THUMB_BATCH_INFLIGHT: AtomicUsize = AtomicUsize::new(0);
static ICON_BATCH_INFLIGHT: AtomicUsize = AtomicUsize::new(0);
static THUMB_BATCH_TOTAL: AtomicUsize = AtomicUsize::new(0);
static ICON_BATCH_TOTAL: AtomicUsize = AtomicUsize::new(0);

// [S-64] Serializes SHGetImageList across concurrent icon batches. shell32's
// lazy per-process image-list init races when several batches hit it at once
// (startup: every folder widget in every window fires simultaneously) and
// transiently fails with E_OUTOFMEMORY (0x8007000E). Under the lock the first
// caller initializes alone; later acquisitions are cheap lookups. Only the
// acquisition is serialized: per-path extraction stays parallel, and the
// returned IImageList stays per-batch (COM apartment affinity forbids caching
// it across threads).
#[cfg(target_os = "windows")]
static SHGETIMAGELIST_LOCK: std::sync::Mutex<()> = std::sync::Mutex::new(());

pub fn folder_debug_counters() -> (usize, usize, usize, usize) {
    (
        THUMB_BATCH_INFLIGHT.load(Ordering::Relaxed),
        ICON_BATCH_INFLIGHT.load(Ordering::Relaxed),
        THUMB_BATCH_TOTAL.load(Ordering::Relaxed),
        ICON_BATCH_TOTAL.load(Ordering::Relaxed),
    )
}

pub fn push_windows_to_desktop(app: &AppHandle) {
    #[cfg(target_os = "windows")]
    {
        use tauri::Manager;
        use winapi::shared::windef::HWND;
        use winapi::um::winuser::{
            SetWindowPos, HWND_BOTTOM, SWP_NOACTIVATE, SWP_NOMOVE, SWP_NOSIZE,
        };

        for (label, window) in app.webview_windows() {
            // Only push widget overlay windows (labels "monitor-N"). Detached
            // app windows like Settings ("config") must keep their z-order,
            // otherwise opening a widget context menu buries Settings behind
            // other apps with no path back to the top.
            if !label.starts_with("monitor-") {
                continue;
            }
            if let Ok(h) = window.hwnd() {
                let hwnd = h.0 as usize as HWND;
                if !hwnd.is_null() {
                    unsafe {
                        SetWindowPos(
                            hwnd,
                            HWND_BOTTOM,
                            0,
                            0,
                            0,
                            0,
                            SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE,
                        );
                    }
                }
            }
        }
    }
}

pub fn open_in_explorer(path: &str) -> Result<(), String> {
    #[cfg(target_os = "windows")]
    {
        use winapi::um::winuser::AllowSetForegroundWindow;
        // Let the spawned process take foreground (Themia holds the lock via WS_EX_NOACTIVATE)
        unsafe {
            AllowSetForegroundWindow(0xFFFFFFFF);
        } // ASFW_ANY
        let p = std::path::Path::new(path);
        if p.is_file() {
            use std::os::windows::process::CommandExt;
            let normalized = path.replace('/', "\\");
            // raw_arg emits the token verbatim so Explorer receives /select,"<path>"
            // without Rust's Command double-quoting interfering.
            std::process::Command::new("explorer.exe")
                .raw_arg(format!("/select,\"{}\"", normalized))
                .spawn()
                .map_err(|e| e.to_string())?;
        } else {
            shell_open_path(path);
        }
    }
    Ok(())
}

pub fn open_file(path: &str) -> Result<(), String> {
    #[cfg(target_os = "windows")]
    {
        use winapi::um::winuser::AllowSetForegroundWindow;
        // Let the spawned app take foreground (Themia holds the lock via WS_EX_NOACTIVATE)
        unsafe {
            AllowSetForegroundWindow(0xFFFFFFFF);
        } // ASFW_ANY
        match open_file_via_context_menu(path) {
            Ok(()) => {}
            Err(_) => open_file_fallback(path),
        }
    }
    Ok(())
}

#[cfg(target_os = "windows")]
fn open_file_via_context_menu(path: &str) -> Result<(), String> {
    use std::ptr;
    use windows::core::{HSTRING, PCSTR};
    use windows::Win32::Foundation::HWND;
    use windows::Win32::System::Com::{
        CoInitializeEx, CoTaskMemFree, CoUninitialize, COINIT_APARTMENTTHREADED,
    };
    use windows::Win32::UI::Shell::Common::ITEMIDLIST;
    use windows::Win32::UI::Shell::{
        IContextMenu, IShellFolder, SHBindToParent, SHParseDisplayName, CMF_DEFAULTONLY,
        CMINVOKECOMMANDINFO,
    };
    use windows::Win32::UI::WindowsAndMessaging::{
        CreatePopupMenu, DestroyMenu, GetMenuDefaultItem, GMDI_USEDISABLED,
    };

    // RAII: balance CoInitializeEx on every exit path (including `?` returns).
    // Only CoUninitialize when CoInitializeEx actually succeeded.
    struct ComGuard(bool);
    impl Drop for ComGuard {
        fn drop(&mut self) {
            if self.0 {
                unsafe {
                    CoUninitialize();
                }
            }
        }
    }

    unsafe {
        let _com = ComGuard(CoInitializeEx(None, COINIT_APARTMENTTHREADED).is_ok());

        let path_h = HSTRING::from(path);
        let mut pidl: *mut ITEMIDLIST = ptr::null_mut();
        SHParseDisplayName(&path_h, None, &mut pidl, 0, None)
            .map_err(|e| format!("SHParseDisplayName: {e}"))?;

        let mut child_pidl: *mut ITEMIDLIST = ptr::null_mut();
        let folder: IShellFolder = SHBindToParent(pidl, Some(&mut child_pidl)).map_err(|e| {
            CoTaskMemFree(Some(pidl as *mut _));
            format!("SHBindToParent: {e}")
        })?;

        let menu: IContextMenu = folder
            .GetUIObjectOf(HWND::default(), &[child_pidl as *const _], None)
            .map_err(|e| {
                CoTaskMemFree(Some(pidl as *mut _));
                format!("GetUIObjectOf: {e}")
            })?;

        let hmenu = CreatePopupMenu().map_err(|e| {
            CoTaskMemFree(Some(pidl as *mut _));
            format!("CreatePopupMenu: {e}")
        })?;

        let _ = menu.QueryContextMenu(hmenu, 0, 1, 0x7FFF, CMF_DEFAULTONLY);

        let cmd = GetMenuDefaultItem(hmenu, 0, GMDI_USEDISABLED);
        let _ = DestroyMenu(hmenu);

        if cmd != u32::MAX {
            let offset = (cmd - 1) as usize;
            let mut ici: CMINVOKECOMMANDINFO = std::mem::zeroed();
            ici.cbSize = std::mem::size_of::<CMINVOKECOMMANDINFO>() as u32;
            ici.lpVerb = PCSTR::from_raw(offset as *const u8);
            ici.nShow = 1;
            // Best-effort: for .exe and similar, the shell handler may launch
            // the process and still return a non-success HRESULT. Treat any
            // return as "the OS handled it" so the caller does not
            // double-launch via fallback.
            let _ = menu.InvokeCommand(&ici);
        } else {
            CoTaskMemFree(Some(pidl as *mut _));
            return Err("no default menu item".into());
        }

        CoTaskMemFree(Some(pidl as *mut _));
    }
    Ok(())
}

#[cfg(target_os = "windows")]
fn open_file_fallback(path: &str) {
    shell_open_path(path);
}

#[cfg(target_os = "windows")]
fn shell_open_path(path: &str) {
    use std::ptr;
    use winapi::um::shellapi::ShellExecuteW;
    let op: Vec<u16> = "open\0".encode_utf16().collect();
    let path_w: Vec<u16> = path.encode_utf16().chain(std::iter::once(0)).collect();
    unsafe {
        ShellExecuteW(
            ptr::null_mut(),
            op.as_ptr(),
            path_w.as_ptr(),
            ptr::null(),
            ptr::null(),
            1,
        );
    }
}

#[derive(serde::Serialize)]
pub struct ShortcutTarget {
    pub target_path: String,
    pub is_directory: bool,
}

/// Resolve a Windows `.lnk` shortcut and return its target path.
pub fn resolve_shortcut(path: &str) -> Result<ShortcutTarget, String> {
    #[cfg(target_os = "windows")]
    {
        use windows::core::{Interface, HSTRING, PCWSTR};
        use windows::Win32::System::Com::{
            CoCreateInstance, CoInitializeEx, CoUninitialize, CLSCTX_INPROC_SERVER,
            COINIT_APARTMENTTHREADED,
        };
        use windows::Win32::UI::Shell::{IShellLinkW, ShellLink};

        // RAII: balance CoInitializeEx on every `?`/early-return path. Only
        // CoUninitialize when CoInitializeEx actually succeeded.
        struct ComGuard(bool);
        impl Drop for ComGuard {
            fn drop(&mut self) {
                if self.0 {
                    unsafe {
                        CoUninitialize();
                    }
                }
            }
        }

        unsafe {
            let _com = ComGuard(CoInitializeEx(None, COINIT_APARTMENTTHREADED).is_ok());

            let shell_link: IShellLinkW = CoCreateInstance(&ShellLink, None, CLSCTX_INPROC_SERVER)
                .map_err(|e| format!("CoCreateInstance failed: {e}"))?;

            let persist_file: windows::Win32::System::Com::IPersistFile = shell_link
                .cast()
                .map_err(|e| format!("IPersistFile cast failed: {e}"))?;

            let lnk = HSTRING::from(path);
            persist_file
                .Load(PCWSTR(lnk.as_ptr()), windows::Win32::System::Com::STGM(0))
                .map_err(|e| format!("IPersistFile::Load failed: {e}"))?;

            let mut buf = [0u16; 520];
            shell_link
                .GetPath(&mut buf, std::ptr::null_mut(), 0)
                .map_err(|e| format!("GetPath failed: {e}"))?;

            let target = String::from_utf16_lossy(
                &buf[..buf.iter().position(|&c| c == 0).unwrap_or(buf.len())],
            );

            if target.is_empty() {
                return Err("Shortcut has no file-system target".into());
            }

            let is_directory = std::path::Path::new(&target).is_dir();
            Ok(ShortcutTarget {
                target_path: target,
                is_directory,
            })
        }
    }
    #[cfg(not(target_os = "windows"))]
    Err("Shortcut resolution is only supported on Windows".into())
}

/// Open a URL in the user's default browser.
pub fn open_url(url: &str) -> Result<(), String> {
    #[cfg(target_os = "windows")]
    {
        use std::ptr;
        use winapi::um::shellapi::ShellExecuteW;
        let op: Vec<u16> = "open\0".encode_utf16().collect();
        let url_w: Vec<u16> = url.encode_utf16().chain(std::iter::once(0)).collect();
        unsafe {
            ShellExecuteW(
                ptr::null_mut(),
                op.as_ptr(),
                url_w.as_ptr(),
                ptr::null(),
                ptr::null(),
                1,
            );
        }
    }

    Ok(())
}

// ---------------------------------------------------------------------------
// Cloud sync folder detection
// ---------------------------------------------------------------------------

#[derive(serde::Serialize, Clone)]
pub struct CloudFolder {
    pub provider: String,
    pub path: String,
}

/// Return known cloud-sync root folders (OneDrive, Synology Drive, etc.).
/// Tries the Windows registry first (most reliable), falls back to env vars / common paths.
pub fn get_cloud_sync_folders() -> Vec<CloudFolder> {
    let mut folders: Vec<CloudFolder> = Vec::new();

    #[cfg(target_os = "windows")]
    {
        use winreg::enums::HKEY_CURRENT_USER;
        use winreg::RegKey;

        // ── OneDrive ──────────────────────────────────────────────────────
        if let Ok(accounts) =
            RegKey::predef(HKEY_CURRENT_USER).open_subkey("Software\\Microsoft\\OneDrive\\Accounts")
        {
            for name in accounts.enum_keys().flatten() {
                if let Ok(account) = accounts.open_subkey(&name) {
                    if let Ok(path) = account.get_value::<String, _>("UserFolder") {
                        let path = path.trim().to_string();
                        if !path.is_empty() && !folders.iter().any(|f| f.path == path) {
                            folders.push(CloudFolder {
                                provider: "onedrive".to_string(),
                                path,
                            });
                        }
                    }
                }
            }
        }

        // ── Synology Drive ────────────────────────────────────────────────
        // Synology Drive Client stores sync-folder paths in the registry.
        for key_path in &[
            "Software\\Synology\\SynologyDrive",
            "Software\\Synology\\CloudStation",
            "Software\\Synology\\SynologyDrive\\Drives",
            "Software\\Synology\\CloudStation\\Drives",
        ] {
            if let Ok(key) = RegKey::predef(HKEY_CURRENT_USER).open_subkey(key_path) {
                // Try direct value on the key itself
                for value_name in &["Location", "SyncFolder", "Path", "UserFolder"] {
                    if let Ok(path) = key.get_value::<String, _>(value_name) {
                        let path = path.trim().to_string();
                        if !path.is_empty() && !folders.iter().any(|f| f.path == path) {
                            folders.push(CloudFolder {
                                provider: "synology".to_string(),
                                path,
                            });
                        }
                    }
                }
                // Enumerate subkeys (individual sync tasks / drives)
                for sub_name in key.enum_keys().flatten() {
                    if let Ok(sub) = key.open_subkey(&sub_name) {
                        for value_name in &["Location", "SyncFolder", "Path"] {
                            if let Ok(path) = sub.get_value::<String, _>(value_name) {
                                let path = path.trim().to_string();
                                if !path.is_empty() && !folders.iter().any(|f| f.path == path) {
                                    folders.push(CloudFolder {
                                        provider: "synology".to_string(),
                                        path,
                                    });
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    // ── Fallback: environment variables (OneDrive) ────────────────────────
    for var in &["OneDriveConsumer", "OneDriveCommercial", "OneDrive"] {
        if let Ok(val) = std::env::var(var) {
            let val = val.trim().to_string();
            if !val.is_empty() && !folders.iter().any(|f| f.path == val) {
                folders.push(CloudFolder {
                    provider: "onedrive".to_string(),
                    path: val,
                });
            }
        }
    }

    // ── Fallback: common Synology Drive path ──────────────────────────────
    if let Ok(profile) = std::env::var("USERPROFILE") {
        let syn_dir = std::path::Path::new(&profile).join("SynologyDrive");
        if syn_dir.is_dir()
            && !folders
                .iter()
                .any(|f| f.path == syn_dir.to_string_lossy().as_ref())
        {
            folders.push(CloudFolder {
                provider: "synology".to_string(),
                path: syn_dir.to_string_lossy().to_string(),
            });
        }
    }

    folders
}

// ---------------------------------------------------------------------------
// File attributes (Windows), used to detect cloud-only (offline) files
// ---------------------------------------------------------------------------

const FILE_ATTRIBUTE_INVALID: u32 = 0xFFFF_FFFF;

/// Return Windows file attributes for `path`, or 0 on non-Windows / error.
pub fn get_file_attributes(path: &str) -> u32 {
    #[cfg(target_os = "windows")]
    {
        use winapi::um::fileapi::GetFileAttributesW;
        let wide: Vec<u16> = path.encode_utf16().chain(std::iter::once(0)).collect();
        let attrs = unsafe { GetFileAttributesW(wide.as_ptr()) };
        if attrs == FILE_ATTRIBUTE_INVALID {
            0
        } else {
            attrs
        }
    }
    #[cfg(not(target_os = "windows"))]
    {
        let _ = path;
        0
    }
}

/// Return attributes for many paths in one call, avoids per-file IPC overhead.
pub fn get_file_attributes_batch(paths: Vec<String>) -> std::collections::HashMap<String, u32> {
    paths
        .into_iter()
        .map(|p| {
            let attrs = get_file_attributes(&p);
            (p, attrs)
        })
        .collect()
}

/// Directory entry enriched with a cross-platform `is_hidden` flag.
/// Dot-prefixed on any OS; FILE_ATTRIBUTE_HIDDEN or FILE_ATTRIBUTE_SYSTEM
/// on Windows. Used by the folder widget so the frontend can filter
/// Win32-hidden files (desktop.ini, Thumbs.db, $RECYCLE.BIN, …) that
/// Tauri's plugin-fs readDir does not surface.
#[derive(serde::Serialize)]
#[serde(rename_all = "camelCase")]
pub struct FolderDirEntry {
    pub name: String,
    pub is_directory: bool,
    pub is_file: bool,
    pub is_symlink: bool,
    pub is_hidden: bool,
}

pub fn folder_read_dir(path: String) -> Result<Vec<FolderDirEntry>, String> {
    let mut out: Vec<FolderDirEntry> = Vec::new();
    for entry in std::fs::read_dir(&path).map_err(|e| e.to_string())? {
        let Ok(entry) = entry else { continue };
        let name = entry.file_name().to_string_lossy().into_owned();
        let ft = match entry.file_type() {
            Ok(t) => t,
            Err(_) => continue,
        };

        let dot = name.starts_with('.');
        #[cfg(target_os = "windows")]
        let win_hidden = {
            let full = format!("{}/{}", path.trim_end_matches(['/', '\\']), name);
            let a = get_file_attributes(&full);
            // 0x2 = FILE_ATTRIBUTE_HIDDEN, 0x4 = FILE_ATTRIBUTE_SYSTEM
            (a & 0x2) != 0 || (a & 0x4) != 0
        };
        #[cfg(not(target_os = "windows"))]
        let win_hidden = false;

        out.push(FolderDirEntry {
            name,
            is_directory: ft.is_dir(),
            is_file: ft.is_file(),
            is_symlink: ft.is_symlink(),
            is_hidden: dot || win_hidden,
        });
    }
    Ok(out)
}

/// Return size (bytes) and mtime (ms since epoch) for many paths in one call.
pub fn get_file_stats_batch(paths: Vec<String>) -> std::collections::HashMap<String, (u64, u64)> {
    paths
        .into_iter()
        .filter_map(|p| {
            let meta = std::fs::metadata(&p).ok()?;
            let size = meta.len();
            let mtime = meta
                .modified()
                .ok()
                .and_then(|t| t.duration_since(std::time::UNIX_EPOCH).ok())
                .map(|d| d.as_millis() as u64)
                .unwrap_or(0);
            Some((p, (size, mtime)))
        })
        .collect()
}

/// Set the cloud pin state for one or more files.
/// `mode` = "pin"   → always keep on this device  (set PINNED, clear UNPINNED)
/// `mode` = "unpin" → free up space               (set UNPINNED, clear PINNED)
/// `mode` = "clear" → remove both flags (reset to provider default)
pub fn set_cloud_pin(paths: &[String], mode: &str) -> Result<(), String> {
    #[cfg(target_os = "windows")]
    {
        use winapi::um::fileapi::{GetFileAttributesW, SetFileAttributesW};

        const PINNED: u32 = 0x80000;
        const UNPINNED: u32 = 0x100000;

        for path in paths {
            let wide: Vec<u16> = path.encode_utf16().chain(std::iter::once(0)).collect();
            let attrs = unsafe { GetFileAttributesW(wide.as_ptr()) };
            if attrs == FILE_ATTRIBUTE_INVALID {
                return Err(format!("Cannot read attributes: {path}"));
            }

            let new_attrs = match mode {
                "pin" => (attrs & !UNPINNED) | PINNED,
                "unpin" => (attrs & !PINNED) | UNPINNED,
                "clear" => attrs & !(PINNED | UNPINNED),
                _ => return Err(format!("Unknown mode: {mode}")),
            };

            if new_attrs != attrs {
                let ok = unsafe { SetFileAttributesW(wide.as_ptr(), new_attrs) };
                if ok == 0 {
                    return Err(format!("SetFileAttributesW failed for: {path}"));
                }
            }
        }
        Ok(())
    }
    #[cfg(not(target_os = "windows"))]
    {
        let _ = (paths, mode);
        Err("Not supported on this platform".into())
    }
}

// ---------------------------------------------------------------------------
// File system operations: paste (copy / move) and delete
// ---------------------------------------------------------------------------

/// Use the Windows Shell `IFileOperation` COM interface for copy/move.
/// This shows the native progress dialog with cancel and conflict resolution.
#[cfg(target_os = "windows")]
mod shell_file_op {
    use windows::core::HSTRING;
    use windows::Win32::System::Com::{
        CoCreateInstance, CoInitializeEx, CoUninitialize, CLSCTX_ALL, COINIT_APARTMENTTHREADED,
    };
    use windows::Win32::UI::Shell::{
        FileOperation, IFileOperation, IShellItem, SHCreateItemFromParsingName, FILEOPERATION_FLAGS,
    };

    // FOF / FOFX flag constants
    const FOF_ALLOWUNDO: u32 = 0x0040;
    const FOF_NOCONFIRMMKDIR: u32 = 0x0200;
    const FOF_NOERRORUI: u32 = 0x0400;

    /// Defense-in-depth same-folder guard mirroring the frontend's
    /// `pastableSources` [S-54]. A move whose source already lives in `dest_dir`
    /// is a no-op; handing it to IFileOperation::MoveItem must never be allowed
    /// to delete the dragged item. Windows paths are case- and
    /// separator-insensitive, so compare normalized.
    pub(super) fn is_same_dir_move(src: &str, dest_dir: &str) -> bool {
        fn norm(p: &str) -> String {
            p.replace('/', "\\").trim_end_matches('\\').to_lowercase()
        }
        let src_n = norm(src);
        let parent = src_n.rfind('\\').map(|i| &src_n[..i]).unwrap_or("");
        parent == norm(dest_dir) || src_n == norm(dest_dir)
    }

    pub fn shell_paste(src_paths: &[String], dest_dir: &str, cut: bool) -> Result<(), String> {
        unsafe {
            CoInitializeEx(None, COINIT_APARTMENTTHREADED)
                .ok()
                .map_err(|e| format!("CoInitializeEx failed: {e}"))?;

            let result = shell_paste_inner(src_paths, dest_dir, cut);

            CoUninitialize();
            result
        }
    }

    unsafe fn shell_paste_inner(
        src_paths: &[String],
        dest_dir: &str,
        cut: bool,
    ) -> Result<(), String> {
        let pfo: IFileOperation = CoCreateInstance(&FileOperation, None, CLSCTX_ALL)
            .map_err(|e| format!("CoCreateInstance(IFileOperation) failed: {e}"))?;

        pfo.SetOperationFlags(FILEOPERATION_FLAGS(
            FOF_ALLOWUNDO | FOF_NOCONFIRMMKDIR | FOF_NOERRORUI,
        ))
        .map_err(|e| format!("SetOperationFlags failed: {e}"))?;

        let dest_item: IShellItem = SHCreateItemFromParsingName(&HSTRING::from(dest_dir), None)
            .map_err(|e| format!("SHCreateItemFromParsingName(dest) failed: {e}"))?;

        for src in src_paths {
            // Never move an item onto its own folder (or onto itself); that is
            // a no-op the shell can resolve destructively. [S-54]
            if cut && is_same_dir_move(src, dest_dir) {
                continue;
            }

            let src_item: IShellItem =
                SHCreateItemFromParsingName(&HSTRING::from(src.as_str()), None)
                    .map_err(|e| format!("SHCreateItemFromParsingName({src}) failed: {e}"))?;

            if cut {
                pfo.MoveItem(&src_item, &dest_item, None, None)
                    .map_err(|e| format!("MoveItem failed: {e}"))?;
            } else {
                pfo.CopyItem(&src_item, &dest_item, None, None)
                    .map_err(|e| format!("CopyItem failed: {e}"))?;
            }
        }

        pfo.PerformOperations()
            .map_err(|e| format!("PerformOperations failed: {e}"))?;

        // Check if the user cancelled
        let aborted = pfo
            .GetAnyOperationsAborted()
            .map_err(|e| format!("GetAnyOperationsAborted failed: {e}"))?;
        if aborted.as_bool() {
            return Ok(()); // User cancelled, not an error
        }

        Ok(())
    }
}

/// Copy or move `src_paths` into `dest_dir`.
/// On Windows, uses the native Shell IFileOperation dialog (progress, conflict
/// resolution, undo support).  Falls back to manual copy/rename on other platforms.
pub fn fs_paste(src_paths: &[String], dest_dir: &str, cut: bool) -> Result<(), String> {
    #[cfg(target_os = "windows")]
    {
        shell_file_op::shell_paste(src_paths, dest_dir, cut)
    }

    #[cfg(not(target_os = "windows"))]
    {
        fs_paste_fallback(src_paths, dest_dir, cut)
    }
}

/// Fallback paste implementation using std::fs (no progress dialog).
#[cfg(not(target_os = "windows"))]
fn fs_paste_fallback(src_paths: &[String], dest_dir: &str, cut: bool) -> Result<(), String> {
    use std::path::Path;

    for src in src_paths {
        let src_path = Path::new(src);
        let file_name = src_path
            .file_name()
            .ok_or_else(|| format!("Invalid source path: {src}"))?;

        let natural_dest = Path::new(dest_dir).join(file_name);

        // Cut to the exact same location → no-op
        if cut && src_path == natural_dest.as_path() {
            continue;
        }

        let dest_path = unique_dest_path(Path::new(dest_dir), file_name);

        if cut {
            if std::fs::rename(src_path, &dest_path).is_err() {
                // Cross-drive or rename failed: copy then delete
                if src_path.is_dir() {
                    copy_dir_recursive(src_path, &dest_path)?;
                    std::fs::remove_dir_all(src_path).map_err(|e| e.to_string())?;
                } else {
                    std::fs::copy(src_path, &dest_path).map_err(|e| e.to_string())?;
                    std::fs::remove_file(src_path).map_err(|e| e.to_string())?;
                }
            }
        } else {
            if src_path.is_dir() {
                copy_dir_recursive(src_path, &dest_path)?;
            } else {
                std::fs::copy(src_path, &dest_path).map_err(|e| e.to_string())?;
            }
        }
    }
    Ok(())
}

/// Reduce an untrusted file name (from a remote Content-Disposition header, a
/// URL path segment, or an IPC argument) to a single SAFE path component, or
/// reject it. This is the path-traversal guard: a name like `..\..\Startup\x.bat`
/// or an absolute `C:\...\x` must never let a write/rename/create escape the
/// destination directory. Returns the bare leaf, or Err for an unusable name.
fn safe_leaf_name(raw: &str) -> Result<String, String> {
    // file_name() drops any directory prefix and absolute/drive root (on Windows)
    // and returns None for `.`/`..`/trailing-separator. The explicit char checks
    // then catch residual separators on non-Windows (where `\`/`:` aren't path
    // separators) so the guard is correct on every target.
    let leaf = std::path::Path::new(raw.trim())
        .file_name()
        .and_then(|s| s.to_str())
        .ok_or_else(|| "Invalid file name".to_string())?;
    if leaf.is_empty()
        || leaf == "."
        || leaf == ".."
        || leaf.contains('/')
        || leaf.contains('\\')
        || leaf.contains(':')
        || leaf.contains('\0')
    {
        return Err("Invalid file name".to_string());
    }
    Ok(leaf.to_string())
}

/// Return a destination path that does not collide with an existing entry.
/// If `dir/name` already exists, tries `dir/stem (copy).ext`,
/// then `dir/stem (copy 2).ext`, etc.
fn unique_dest_path(dir: &std::path::Path, file_name: &std::ffi::OsStr) -> std::path::PathBuf {
    let initial = dir.join(file_name);
    if !initial.exists() {
        return initial;
    }

    let name_str = file_name.to_string_lossy();
    // Split at last dot to get stem + extension (directories have no meaningful ext)
    let (stem, ext) = match name_str.rfind('.') {
        Some(dot) if dot > 0 => (&name_str[..dot], &name_str[dot..]),
        _ => (name_str.as_ref(), ""),
    };

    let candidate = dir.join(format!("{stem} (copy){ext}"));
    if !candidate.exists() {
        return candidate;
    }

    let mut n = 2u32;
    loop {
        let candidate = dir.join(format!("{stem} (copy {n}){ext}"));
        if !candidate.exists() {
            return candidate;
        }
        n += 1;
    }
}

#[cfg(not(target_os = "windows"))]
fn copy_dir_recursive(src: &std::path::Path, dest: &std::path::Path) -> Result<(), String> {
    std::fs::create_dir_all(dest).map_err(|e| e.to_string())?;
    for entry in std::fs::read_dir(src).map_err(|e| e.to_string())? {
        let entry = entry.map_err(|e| e.to_string())?;
        let dest_child = dest.join(entry.file_name());
        if entry.file_type().map_err(|e| e.to_string())?.is_dir() {
            copy_dir_recursive(&entry.path(), &dest_child)?;
        } else {
            std::fs::copy(entry.path(), &dest_child).map_err(|e| e.to_string())?;
        }
    }
    Ok(())
}

/// Info recorded when a file/folder is moved to the trash. Returned to
/// the frontend so operations can be un-done by restoring matching items
/// from the Recycle Bin.
#[derive(serde::Serialize, serde::Deserialize, Clone, Debug)]
pub struct TrashedFile {
    pub original_path: String,
    pub name: String,
    pub deleted_at_ms: u64,
    // Fields below identify the TrashItem in the Recycle Bin so restore can
    // skip enumerating the whole bin. Optional for backward compat with tokens
    // persisted in localStorage before this change; those fall back to the
    // legacy list()+match path in fs_restore_from_trash.
    #[serde(default)]
    pub id: String,
    #[serde(default)]
    pub original_parent: String,
    #[serde(default)]
    pub time_deleted: i64,
}

/// Move files / directories to the system Recycle Bin. Returns a list of
/// TrashedFile tokens that can later be passed to `fs_restore_from_trash`.
///
/// After deleting, enumerates the bin once to capture each item's stable
/// identifier so restore can happen without re-enumerating. The enumeration
/// is slow on large bins; callers should dispatch this on a background
/// thread (see the `fs_delete` Tauri command).
pub fn fs_delete(paths: &[String]) -> Result<Vec<TrashedFile>, String> {
    let ts = std::time::SystemTime::now()
        .duration_since(std::time::UNIX_EPOCH)
        .map(|d| d.as_millis() as u64)
        .unwrap_or(0);

    #[derive(Clone)]
    struct Pending {
        original_path: String,
        name: String,
    }
    let mut pending: Vec<Pending> = Vec::with_capacity(paths.len());
    let mut failures: Vec<String> = Vec::new();

    // Do NOT early-return on the first failure: a partial failure must still
    // yield undo tokens for the files that DID make it to the bin, otherwise
    // the whole recovery record is lost and the user can't undo any of them.
    // Collect successes (-> pending) and surface per-file failures separately.
    for path_str in paths {
        let p = std::path::Path::new(path_str);
        let name = p
            .file_name()
            .map(|n| n.to_string_lossy().into_owned())
            .unwrap_or_default();
        match trash::delete(p) {
            Ok(()) => pending.push(Pending {
                original_path: path_str.clone(),
                name,
            }),
            Err(e) => {
                let msg = format!("trash::delete({path_str}): {e}");
                log::error!(target: "folder:fs_delete", "{msg}");
                failures.push(msg);
            }
        }
    }

    // Nothing reached the bin AND at least one path failed: report the
    // failure (mirrors the previous early-return contract: the caller throws
    // and drops the op-log entry). An empty input still yields an empty Ok,
    // exactly as the old loop did.
    if pending.is_empty() && !failures.is_empty() {
        return Err(failures.join("; "));
    }

    let mut out: Vec<TrashedFile> = pending
        .iter()
        .map(|p| TrashedFile {
            original_path: p.original_path.clone(),
            name: p.name.clone(),
            deleted_at_ms: ts,
            id: String::new(),
            original_parent: String::new(),
            time_deleted: 0,
        })
        .collect();

    // Best-effort: look up each freshly-deleted item's TrashItem id so undo
    // can go direct. If the enum fails (or we're on an unsupported platform)
    // we fall back to empty ids, and restore enumerates the bin itself.
    #[cfg(any(target_os = "windows", target_os = "macos"))]
    {
        if let Ok(all) = trash::os_limited::list() {
            for (i, p) in pending.iter().enumerate() {
                let want = std::path::Path::new(&p.original_path);
                let mut best: Option<&trash::TrashItem> = None;
                for t in &all {
                    let reconstructed = std::path::Path::new(&t.original_parent).join(&t.name);
                    if reconstructed == want && t.name.as_os_str() == std::ffi::OsStr::new(&p.name)
                    {
                        match best {
                            Some(b) if b.time_deleted >= t.time_deleted => {}
                            _ => best = Some(t),
                        }
                    }
                }
                if let Some(b) = best {
                    out[i].id = b.id.to_string_lossy().into_owned();
                    out[i].original_parent = b.original_parent.to_string_lossy().into_owned();
                    out[i].time_deleted = b.time_deleted;
                }
            }
        }
    }

    Ok(out)
}

/// Permanently delete files / directories (no Recycle Bin). Used to un-do
/// a copy/paste-copy: the files were created by us moments earlier and the
/// originals still exist, so trashing them would pollute the bin.
pub fn fs_delete_permanent(paths: &[String]) -> Result<(), String> {
    for path_str in paths {
        let p = std::path::Path::new(path_str);
        if !p.exists() {
            continue;
        }
        if p.is_dir() {
            std::fs::remove_dir_all(p).map_err(|e| format!("remove_dir_all({path_str}): {e}"))?;
        } else {
            std::fs::remove_file(p).map_err(|e| format!("remove_file({path_str}): {e}"))?;
        }
    }
    Ok(())
}

/// Restore previously-trashed items back to their original locations.
/// For each token, the most-recently-deleted matching Recycle Bin entry
/// is restored.
#[cfg(any(target_os = "windows", target_os = "macos"))]
pub fn fs_restore_from_trash(items: Vec<TrashedFile>) -> Result<(), String> {
    use std::ffi::OsString;
    use std::path::PathBuf;
    use trash::os_limited::{list, restore_all};

    // Fast path: every token already carries its TrashItem id, reconstruct
    // directly and skip the expensive bin enumeration.
    if !items.is_empty() && items.iter().all(|i| !i.id.is_empty()) {
        let picks: Vec<trash::TrashItem> = items
            .iter()
            .map(|i| trash::TrashItem {
                id: OsString::from(&i.id),
                name: OsString::from(&i.name),
                original_parent: PathBuf::from(&i.original_parent),
                time_deleted: i.time_deleted,
            })
            .collect();
        return restore_all(picks).map_err(|e| format!("trash::restore_all: {e:?}"));
    }

    // Legacy fallback: tokens written before ids were captured, enumerate
    // and match by original path + name, picking the newest.
    let all = list().map_err(|e| format!("trash::list: {e}"))?;
    let mut picks: Vec<trash::TrashItem> = Vec::new();

    for item in &items {
        let want = std::path::Path::new(&item.original_path);
        let mut best: Option<trash::TrashItem> = None;
        for t in &all {
            let reconstructed = std::path::Path::new(&t.original_parent).join(&t.name);
            if reconstructed == want && t.name.as_os_str() == std::ffi::OsStr::new(&item.name) {
                match &best {
                    Some(b) if b.time_deleted >= t.time_deleted => {}
                    _ => best = Some(t.clone()),
                }
            }
        }
        if let Some(b) = best {
            picks.push(b);
        }
    }

    if picks.is_empty() {
        return Err("No matching items found in Recycle Bin".into());
    }

    restore_all(picks).map_err(|e| format!("trash::restore_all: {e:?}"))?;
    Ok(())
}

#[cfg(not(any(target_os = "windows", target_os = "macos")))]
pub fn fs_restore_from_trash(_items: Vec<TrashedFile>) -> Result<(), String> {
    Err("Restore from trash is not supported on this platform".into())
}

// Keep get_clipboard_url as a no-op for backward compat
pub fn get_clipboard_url() -> Option<String> {
    None
}

/// How much is currently sitting in the Recycle Bin, across every drive.
/// `items` is the file/folder count, `bytes` the total size.
#[derive(serde::Serialize, serde::Deserialize, Clone, Copy, Debug, Default, PartialEq)]
pub struct RecycleBinStatus {
    pub items: u64,
    pub bytes: u64,
}

/// Query the Recycle Bin's fill state via `SHQueryRecycleBin`. A NULL root path
/// aggregates every bin the current user owns (all drives), which is what the
/// shell itself shows on the desktop icon.
///
/// Can take a moment on a very full bin (it walks the bin's `$I` metadata), so
/// callers should run it off the UI thread.
#[cfg(target_os = "windows")]
pub fn recycle_bin_status() -> Result<RecycleBinStatus, String> {
    use windows::Win32::UI::Shell::{SHQueryRecycleBinW, SHQUERYRBINFO};
    use windows::core::PCWSTR;

    let mut info = SHQUERYRBINFO {
        cbSize: std::mem::size_of::<SHQUERYRBINFO>() as u32,
        ..Default::default()
    };
    // SAFETY: `info` is a live, correctly sized SHQUERYRBINFO; a null root path
    // is the documented "all drives" argument.
    unsafe { SHQueryRecycleBinW(PCWSTR::null(), &mut info) }
        .map_err(|e| format!("SHQueryRecycleBin: {e}"))?;
    Ok(RecycleBinStatus {
        items: info.i64NumItems.max(0) as u64,
        bytes: info.i64Size.max(0) as u64,
    })
}

#[cfg(not(target_os = "windows"))]
pub fn recycle_bin_status() -> Result<RecycleBinStatus, String> {
    Ok(RecycleBinStatus::default())
}

// ---------------------------------------------------------------------------
// Windows clipboard: CF_HDROP (file list) read / write
// ---------------------------------------------------------------------------

#[derive(serde::Serialize, serde::Deserialize, Clone, Default)]
pub struct ClipboardFiles {
    pub paths: Vec<String>,
    pub cut: bool,
}

/// Write file paths to the Windows clipboard in CF_HDROP format.
/// Also sets "Preferred DropEffect" so Explorer knows copy vs move.
/// `hwnd_raw` is the window handle to associate with the clipboard (pass 0 for NULL).
pub fn clipboard_set_files(hwnd_raw: usize, paths: Vec<String>, cut: bool) -> Result<(), String> {
    log::debug!(target: "clipboard_set_files", "called with {} paths, cut={}, hwnd={:#x}", paths.len(), cut, hwnd_raw);
    for p in &paths {
        log::debug!(target: "clipboard_set_files", "  path: {p}");
    }

    #[cfg(target_os = "windows")]
    {
        use std::ptr;
        use winapi::um::winbase::{
            GlobalAlloc, GlobalFree, GlobalLock, GlobalUnlock, GMEM_MOVEABLE, GMEM_ZEROINIT,
        };
        use winapi::um::winuser::{
            CloseClipboard, EmptyClipboard, OpenClipboard, RegisterClipboardFormatA,
            SetClipboardData, CF_HDROP,
        };

        // DROPFILES is in the Windows SDK but not exported by winapi crate.
        // Keep the exact SDK name (acronym).
        #[allow(clippy::upper_case_acronyms)]
        #[repr(C)]
        struct DROPFILES {
            p_files: u32,
            pt_x: i32,
            pt_y: i32,
            f_nc: i32,
            f_wide: i32,
        }

        const DROPEFFECT_COPY: u32 = 1;
        const DROPEFFECT_MOVE: u32 = 2;

        // Build the wide-char file list: each path is null-terminated, double-null at end
        let wide_paths: Vec<Vec<u16>> = paths
            .iter()
            .map(|p| p.encode_utf16().chain(std::iter::once(0)).collect())
            .collect();
        let total_chars: usize = wide_paths.iter().map(|w| w.len()).sum::<usize>() + 1; // +1 for final null

        let dropfiles_size = std::mem::size_of::<DROPFILES>();
        let buf_size = dropfiles_size + total_chars * 2; // *2 for u16 bytes

        unsafe {
            let hwnd = hwnd_raw as winapi::shared::windef::HWND;
            if OpenClipboard(hwnd) == 0 {
                log::error!(target: "clipboard_set_files", "OpenClipboard FAILED");
                return Err("OpenClipboard failed".into());
            }
            EmptyClipboard();

            // --- CF_HDROP ---
            let hglobal = GlobalAlloc(GMEM_MOVEABLE | GMEM_ZEROINIT, buf_size);
            if hglobal.is_null() {
                CloseClipboard();
                return Err("GlobalAlloc failed".into());
            }
            let ptr = GlobalLock(hglobal) as *mut u8;
            if ptr.is_null() {
                // Ownership has NOT transferred to the OS (no successful
                // SetClipboardData yet), so free the block before returning.
                GlobalFree(hglobal);
                CloseClipboard();
                return Err("GlobalLock failed".into());
            }

            // Fill DROPFILES header
            let df = ptr as *mut DROPFILES;
            (*df).p_files = dropfiles_size as u32;
            (*df).f_wide = 1; // TRUE, paths are wide chars

            // Fill path list after DROPFILES header
            let mut offset = dropfiles_size;
            for wpath in &wide_paths {
                let byte_len = wpath.len() * 2;
                ptr::copy_nonoverlapping(wpath.as_ptr() as *const u8, ptr.add(offset), byte_len);
                offset += byte_len;
            }
            // Final double-null is already zero from GMEM_ZEROINIT

            GlobalUnlock(hglobal);
            if SetClipboardData(CF_HDROP, hglobal).is_null() {
                log::error!(target: "clipboard_set_files", "SetClipboardData FAILED");
                // SetClipboardData failed, so the OS did NOT take ownership;
                // free the block ourselves before bailing out.
                GlobalFree(hglobal);
                CloseClipboard();
                return Err("SetClipboardData(CF_HDROP) failed".into());
            }

            // --- Preferred DropEffect ---
            let fmt_name = b"Preferred DropEffect\0";
            let fmt = RegisterClipboardFormatA(fmt_name.as_ptr() as *const i8);
            if fmt != 0 {
                let h_effect = GlobalAlloc(GMEM_MOVEABLE | GMEM_ZEROINIT, 4);
                if !h_effect.is_null() {
                    let p_effect = GlobalLock(h_effect) as *mut u32;
                    if !p_effect.is_null() {
                        *p_effect = if cut {
                            DROPEFFECT_MOVE
                        } else {
                            DROPEFFECT_COPY
                        };
                        GlobalUnlock(h_effect);
                        // Ownership transfers to the OS only on success; free
                        // the block ourselves if SetClipboardData fails.
                        if SetClipboardData(fmt, h_effect).is_null() {
                            GlobalFree(h_effect);
                        }
                    } else {
                        // Locked failed before any SetClipboardData; free it.
                        GlobalFree(h_effect);
                    }
                }
            }

            CloseClipboard();
            log::info!(target: "clipboard_set_files", "SUCCESS: {} paths written to clipboard", paths.len());
        }
        Ok(())
    }
    #[cfg(not(target_os = "windows"))]
    {
        let _ = (hwnd_raw, paths, cut);
        Err("Not supported on this platform".into())
    }
}

/// Read file paths from the Windows clipboard (CF_HDROP format).
/// Returns paths and whether the source set "cut" (move) mode.
pub fn clipboard_get_files() -> ClipboardFiles {
    #[cfg(target_os = "windows")]
    {
        use std::ptr;
        use winapi::um::shellapi::DragQueryFileW;
        use winapi::um::winbase::{GlobalLock, GlobalUnlock};
        use winapi::um::winuser::{
            CloseClipboard, GetClipboardData, IsClipboardFormatAvailable, OpenClipboard,
            RegisterClipboardFormatA, CF_HDROP,
        };

        const DROPEFFECT_MOVE: u32 = 2;

        unsafe {
            if OpenClipboard(ptr::null_mut()) == 0 {
                return ClipboardFiles::default();
            }
            // Try CF_HDROP first (native Explorer, most apps)
            if IsClipboardFormatAvailable(CF_HDROP) != 0 {
                let hglobal = GetClipboardData(CF_HDROP);
                if !hglobal.is_null() {
                    let hdrop = hglobal as winapi::um::shellapi::HDROP;
                    let count = DragQueryFileW(hdrop, 0xFFFFFFFF, ptr::null_mut(), 0);
                    let mut paths = Vec::with_capacity(count as usize);

                    for i in 0..count {
                        let len = DragQueryFileW(hdrop, i, ptr::null_mut(), 0);
                        let mut buf: Vec<u16> = vec![0u16; (len + 1) as usize];
                        DragQueryFileW(hdrop, i, buf.as_mut_ptr(), len + 1);
                        if let Some(pos) = buf.iter().position(|&c| c == 0) {
                            buf.truncate(pos);
                        }
                        paths.push(String::from_utf16_lossy(&buf));
                    }

                    // Read Preferred DropEffect
                    let mut cut = false;
                    let fmt_name = b"Preferred DropEffect\0";
                    let fmt = RegisterClipboardFormatA(fmt_name.as_ptr() as *const i8);
                    if fmt != 0 && IsClipboardFormatAvailable(fmt) != 0 {
                        let h_effect = GetClipboardData(fmt);
                        if !h_effect.is_null() {
                            let p_effect = GlobalLock(h_effect) as *const u32;
                            if !p_effect.is_null() {
                                cut = (*p_effect & DROPEFFECT_MOVE) != 0;
                                GlobalUnlock(h_effect);
                            }
                        }
                    }

                    CloseClipboard();
                    return ClipboardFiles { paths, cut };
                }
            }

            CloseClipboard();
            ClipboardFiles::default()
        }
    }
    #[cfg(not(target_os = "windows"))]
    {
        ClipboardFiles::default()
    }
}

// ---------------------------------------------------------------------------
// Git status
// ---------------------------------------------------------------------------

#[derive(serde::Serialize, Clone)]
pub struct GitStatus {
    pub is_git: bool,
    pub branch: String,
    pub files: std::collections::HashMap<String, String>,
    pub staged: u32,
    pub modified: u32,
    pub untracked: u32,
    pub deleted: u32,
    pub conflicted: u32,
}

#[derive(serde::Serialize, Clone)]
pub struct GitRepoInfo {
    pub branch: String,
    pub dirty: bool,
}

#[cfg(target_os = "windows")]
fn no_window_cmd(program: &str) -> Command {
    use std::os::windows::process::CommandExt;
    let mut cmd = Command::new(program);
    cmd.creation_flags(0x08000000); // CREATE_NO_WINDOW
    cmd
}

#[cfg(not(target_os = "windows"))]
fn no_window_cmd(program: &str) -> Command {
    Command::new(program)
}

/// Check multiple directories for git repo info (branch + dirty state).
/// Only returns entries for directories that are git repo roots (contain .git).
pub fn get_git_repo_info_batch(
    paths: Vec<String>,
) -> std::collections::HashMap<String, GitRepoInfo> {
    use std::collections::HashMap;
    use std::path::Path;

    let mut result: HashMap<String, GitRepoInfo> = HashMap::new();

    for dir in paths {
        let git_dir = Path::new(&dir).join(".git");
        if !git_dir.exists() {
            continue;
        }

        let output = match no_window_cmd("git")
            .args(["status", "--porcelain=v1", "-b", "--untracked-files=normal"])
            .current_dir(&dir)
            .output()
        {
            Ok(o) if o.status.success() => String::from_utf8_lossy(&o.stdout).to_string(),
            _ => continue,
        };

        let mut branch = String::new();
        let mut dirty = false;

        for line in output.lines() {
            if let Some(rest) = line.strip_prefix("## ") {
                branch = rest
                    .split("...")
                    .next()
                    .unwrap_or(rest.split(' ').next().unwrap_or(rest))
                    .to_string();
            } else if line.len() >= 2 {
                dirty = true;
            }
        }

        result.insert(dir, GitRepoInfo { branch, dirty });
    }

    result
}

/// Return git status for the directory at `path`.
/// Detects if inside a git repo, returns branch name and per-file status codes.
pub fn get_git_status(path: &str) -> GitStatus {
    use std::collections::HashMap;
    use std::path::Path;

    let empty = GitStatus {
        is_git: false,
        branch: String::new(),
        files: HashMap::new(),
        staged: 0,
        modified: 0,
        untracked: 0,
        deleted: 0,
        conflicted: 0,
    };

    // 1. Check if inside a git repo and get the repo root
    let toplevel = match no_window_cmd("git")
        .args(["rev-parse", "--show-toplevel"])
        .current_dir(path)
        .output()
    {
        Ok(o) if o.status.success() => String::from_utf8_lossy(&o.stdout).trim().to_string(),
        _ => return empty,
    };

    // 2. Run git status porcelain
    let output = match no_window_cmd("git")
        .args(["status", "--porcelain=v1", "-b", "--untracked-files=normal"])
        .current_dir(path)
        .output()
    {
        Ok(o) if o.status.success() => String::from_utf8_lossy(&o.stdout).to_string(),
        _ => {
            return GitStatus {
                is_git: true,
                branch: String::new(),
                files: HashMap::new(),
                staged: 0,
                modified: 0,
                untracked: 0,
                deleted: 0,
                conflicted: 0,
            }
        }
    };

    let mut branch = String::new();
    let mut files: HashMap<String, String> = HashMap::new();
    let root = Path::new(&toplevel);

    for line in output.lines() {
        if let Some(rest) = line.strip_prefix("## ") {
            // Parse branch: "## main...origin/main [ahead 1]" or "## main"
            branch = rest
                .split("...")
                .next()
                .unwrap_or(rest.split(' ').next().unwrap_or(rest))
                .to_string();
            continue;
        }

        if line.len() < 4 {
            continue;
        }

        let status_code = &line[0..2];
        let filename_part = &line[3..];

        // Handle renames: "R  old -> new"
        let filename = if status_code.starts_with('R') || status_code.starts_with('C') {
            filename_part.split(" -> ").last().unwrap_or(filename_part)
        } else {
            filename_part
        };

        // Build absolute path, normalize to backslashes on Windows
        let abs_path = root.join(filename);
        let mut abs_str = abs_path.to_string_lossy().to_string();
        #[cfg(target_os = "windows")]
        {
            abs_str = abs_str.replace('/', "\\");
        }

        files.insert(abs_str, status_code.to_string());
    }

    // Compute summary counts from status codes
    let mut staged: u32 = 0;
    let mut modified: u32 = 0;
    let mut untracked: u32 = 0;
    let mut deleted: u32 = 0;
    let mut conflicted: u32 = 0;

    for code in files.values() {
        let bytes = code.as_bytes();
        if bytes.len() < 2 {
            continue;
        }
        let (x, y) = (bytes[0], bytes[1]);
        if code == "??" {
            untracked += 1;
        } else if x == b'U' || y == b'U' || (x == b'A' && y == b'A') || (x == b'D' && y == b'D') {
            conflicted += 1;
        } else {
            // Index (staged) changes
            if b"MADRC".contains(&x) {
                staged += 1;
            }
            // Working tree changes
            if y == b'M' {
                modified += 1;
            } else if y == b'D' {
                deleted += 1;
            }
        }
    }

    GitStatus {
        is_git: true,
        branch,
        files,
        staged,
        modified,
        untracked,
        deleted,
        conflicted,
    }
}

// ---------------------------------------------------------------------------
// Write file from raw bytes (for browser drag-and-drop)
// ---------------------------------------------------------------------------

/// Write raw bytes as a file in `dest_dir` with the given `file_name`.
/// Deduplicates if a file with the same name already exists.
pub fn fs_write_file(dest_dir: &str, file_name: &str, data: &[u8]) -> Result<(), String> {
    use std::path::Path;
    // Confine to a safe leaf inside dest_dir; file_name is an IPC argument.
    let leaf = safe_leaf_name(file_name)?;
    let dest = unique_dest_path(Path::new(dest_dir), std::ffi::OsStr::new(&leaf));
    std::fs::write(&dest, data).map_err(|e| e.to_string())
}

// ---------------------------------------------------------------------------
// Download a URL to a file (for browser image/URL drops)
// ---------------------------------------------------------------------------

/// A dropped file can be large, so this is generous compared to the app's other
/// timeouts - but bounded, unlike before.
const DOWNLOAD_TIMEOUT: std::time::Duration = std::time::Duration::from_secs(120);

/// Download the content at `url` and save it to `dest_dir`.
/// Returns the final file path on success.
/// Handles any file type: derives filename from Content-Disposition, URL path,
/// or Content-Type header (for URLs without a clear filename like Google Images).
pub fn fs_download_url(url: &str, dest_dir: &str) -> Result<String, String> {
    use std::path::Path;

    // Via the Windows proxy when one applies (netproxy SPEC [PX-01]). The
    // timeout is new: this ran with none at all, on an arbitrary drop URL.
    let parsed = url::Url::parse(url).map_err(|e| format!("Invalid URL: {e}"))?;
    let resp = widget_netproxy::ureq_agent(&parsed, DOWNLOAD_TIMEOUT)
        .get(url)
        .call()
        .map_err(|e| format!("Download failed: {e}"))?;

    let content_type = resp.header("content-type").unwrap_or("").to_string();

    // 1. Try Content-Disposition header
    let file_name = resp
        .header("content-disposition")
        .and_then(|cd| {
            cd.split("filename=")
                .nth(1)
                .map(|s| s.trim_matches('"').trim_matches('\'').to_string())
        })
        // 2. Try URL path (before query string)
        .or_else(|| {
            url.split('?')
                .next()
                .and_then(|u| u.split('/').next_back())
                .filter(|s| !s.is_empty() && s.contains('.'))
                .map(|s| {
                    // URL-decode percent-encoded characters
                    urlish_decode(s)
                })
        })
        // 3. Derive from Content-Type (for URLs like /images?q=...)
        .unwrap_or_else(|| {
            let ext = mime_to_ext(&content_type);
            format!("download.{ext}")
        });

    // The filename is fully attacker-controlled (remote Content-Disposition / URL
    // path). Reduce it to a safe leaf so a `..\..\Startup\x.bat` or `C:\...` can't
    // write outside dest_dir; fall back to a generated name if it's unusable.
    let leaf = safe_leaf_name(&file_name)
        .unwrap_or_else(|_| format!("download.{}", mime_to_ext(&content_type)));
    let dest = unique_dest_path(Path::new(dest_dir), std::ffi::OsStr::new(&leaf));

    let mut reader = resp.into_reader();
    let mut buf = Vec::new();
    reader.read_to_end(&mut buf).map_err(|e| e.to_string())?;
    std::fs::write(&dest, &buf).map_err(|e| e.to_string())?;

    Ok(dest.to_string_lossy().to_string())
}

/// Map a Content-Type to a file extension.
fn mime_to_ext(ct: &str) -> &str {
    let base = ct.split(';').next().unwrap_or("").trim();
    match base {
        "image/jpeg" | "image/jpg" => "jpg",
        "image/png" => "png",
        "image/gif" => "gif",
        "image/webp" => "webp",
        "image/svg+xml" => "svg",
        "image/bmp" => "bmp",
        "image/tiff" => "tiff",
        "image/avif" => "avif",
        "image/x-icon" | "image/vnd.microsoft.icon" => "ico",
        "video/mp4" => "mp4",
        "video/webm" => "webm",
        "audio/mpeg" => "mp3",
        "audio/ogg" => "ogg",
        "application/pdf" => "pdf",
        "application/zip" => "zip",
        "text/html" => "html",
        "text/plain" => "txt",
        "application/json" => "json",
        _ => "bin",
    }
}

/// Simple percent-decode for URL path segments.
fn urlish_decode(s: &str) -> String {
    let mut out = String::with_capacity(s.len());
    let mut chars = s.bytes();
    while let Some(b) = chars.next() {
        if b == b'%' {
            let hi = chars.next().unwrap_or(b'0');
            let lo = chars.next().unwrap_or(b'0');
            let val =
                u8::from_str_radix(&format!("{}{}", hi as char, lo as char), 16).unwrap_or(b'_');
            out.push(val as char);
        } else {
            out.push(b as char);
        }
    }
    out
}

// ---------------------------------------------------------------------------
// Shell icon extraction
// ---------------------------------------------------------------------------

// SHIL_* image list size constants
#[cfg(target_os = "windows")]
const SHIL_SMALL: i32 = 1; // 16x16
#[cfg(target_os = "windows")]
const SHIL_EXTRALARGE: i32 = 2; // 48x48
#[cfg(target_os = "windows")]
const SHIL_JUMBO: i32 = 4; // 256x256

/// Pick the best SHIL and target render size for `requested_size`.
#[cfg(target_os = "windows")]
fn select_shil(requested_size: u32) -> (i32, i32) {
    if requested_size <= 16 {
        (SHIL_SMALL, 16)
    } else if requested_size <= 32 {
        (SHIL_EXTRALARGE, 48)
    } else {
        (SHIL_JUMBO, 256)
    }
}

/// Extract the shell icon for `path` and return it as a PNG data URL.
/// Initializes COM automatically; use `get_file_icon_inner` if COM is already init'd.
#[cfg(target_os = "windows")]
pub fn get_file_icon(path: &str, requested_size: u32) -> Result<String, String> {
    use std::ptr;
    use winapi::um::combaseapi::{CoInitializeEx, CoUninitialize};
    use winapi::um::objbase::COINIT_APARTMENTTHREADED;
    use windows::Win32::UI::Controls::IImageList;
    use windows::Win32::UI::Shell::SHGetImageList;
    // RAII: balance CoInitializeEx only when it succeeded (S_OK / S_FALSE);
    // never balance RPC_E_CHANGED_MODE. See get_file_icons_batch.
    struct ComGuard(bool);
    impl Drop for ComGuard {
        fn drop(&mut self) {
            if self.0 {
                unsafe {
                    CoUninitialize();
                }
            }
        }
    }
    let _com = ComGuard(unsafe { CoInitializeEx(ptr::null_mut(), COINIT_APARTMENTTHREADED) } >= 0);

    let (shil, render_size) = select_shil(requested_size);
    // [S-64] Serialize acquisition with the batch path; single-icon calls
    // race the same shell32 lazy init.
    let image_list: IImageList = {
        let _g = SHGETIMAGELIST_LOCK
            .lock()
            .unwrap_or_else(|e| e.into_inner());
        unsafe { SHGetImageList(shil) }
    }
    .map_err(|e| format!("SHGetImageList failed: {e}"))?;
    get_file_icon_inner(path, shil, render_size, &image_list)
}

/// Index of `path`'s icon in the system image list.
///
/// A file-system path goes straight through `SHGetFileInfoW`. A `shell:`
/// namespace URI (`shell:RecycleBinFolder`, `shell:MyComputerFolder`, …) has no
/// file-system path at all, so it is parsed into a PIDL first and queried with
/// `SHGFI_PIDL`. That is also what makes the Recycle Bin report its FULL vs
/// EMPTY icon: the shell picks the variant, exactly as Explorer shows it.
///
/// COM must already be initialized on this thread (both callers do it).
#[cfg(target_os = "windows")]
fn shell_icon_index(path: &str) -> Result<i32, String> {
    use winapi::um::shellapi::{SHGetFileInfoW, SHFILEINFOW, SHGFI_PIDL, SHGFI_SYSICONINDEX};
    use windows::core::HSTRING;
    use windows::Win32::System::Com::CoTaskMemFree;
    use windows::Win32::UI::Shell::Common::ITEMIDLIST;
    use windows::Win32::UI::Shell::SHParseDisplayName;

    /// RAII for the parsed PIDL: shell-allocated, so it must be freed on every
    /// exit path, including the error returns below.
    struct Pidl(*mut ITEMIDLIST);
    impl Drop for Pidl {
        fn drop(&mut self) {
            if !self.0.is_null() {
                unsafe { CoTaskMemFree(Some(self.0 as *mut _)) };
            }
        }
    }

    let is_shell_uri = path.len() >= 6 && path[..6].eq_ignore_ascii_case("shell:");

    unsafe {
        let mut fi: SHFILEINFOW = std::mem::zeroed();
        let cb = std::mem::size_of::<SHFILEINFOW>() as u32;

        let ok = if is_shell_uri {
            let mut raw: *mut ITEMIDLIST = std::ptr::null_mut();
            SHParseDisplayName(&HSTRING::from(path), None, &mut raw, 0, None)
                .map_err(|e| format!("SHParseDisplayName({path}): {e}"))?;
            let pidl = Pidl(raw);
            SHGetFileInfoW(
                pidl.0 as *const u16,
                0,
                &mut fi,
                cb,
                SHGFI_SYSICONINDEX | SHGFI_PIDL,
            )
        } else {
            let wide: Vec<u16> = path.encode_utf16().chain(std::iter::once(0)).collect();
            SHGetFileInfoW(wide.as_ptr(), 0, &mut fi, cb, SHGFI_SYSICONINDEX)
        };

        if ok == 0 {
            return Err(format!("SHGetFileInfoW({path}) failed"));
        }
        Ok(fi.iIcon)
    }
}

/// Inner icon extraction; caller must ensure COM is initialized on this thread
/// and supply the IImageList for the chosen SHIL.
#[cfg(target_os = "windows")]
fn get_file_icon_inner(
    path: &str,
    shil: i32,
    render_size: i32,
    image_list: &windows::Win32::UI::Controls::IImageList,
) -> Result<String, String> {
    use std::ptr;
    use winapi::ctypes::c_void;
    use winapi::um::wingdi::{
        CreateCompatibleDC, CreateDIBSection, DeleteDC, DeleteObject, GetDIBits, SelectObject,
        BITMAPINFO, BITMAPINFOHEADER, DIB_RGB_COLORS,
    };
    use winapi::um::winuser::{DestroyIcon, DrawIconEx, GetDC, ReleaseDC};
    const DI_NORMAL: u32 = 0x0003;

    // Index in the system image list (handles file paths and `shell:` URIs).
    let icon_index = shell_icon_index(path)?;

    unsafe {

        // Get the HICON from the system image list at the chosen size.
        let hicon = image_list
            .GetIcon(icon_index, 0)
            .map_err(|e| format!("IImageList::GetIcon failed: {e}"))?;

        let size = render_size;
        let hdc_screen = GetDC(ptr::null_mut());
        let hdc = CreateCompatibleDC(hdc_screen);
        ReleaseDC(ptr::null_mut(), hdc_screen);

        let mut bmi: BITMAPINFO = std::mem::zeroed();
        bmi.bmiHeader.biSize = std::mem::size_of::<BITMAPINFOHEADER>() as u32;
        bmi.bmiHeader.biWidth = size;
        bmi.bmiHeader.biHeight = -size;
        bmi.bmiHeader.biPlanes = 1;
        bmi.bmiHeader.biBitCount = 32;
        bmi.bmiHeader.biCompression = 0;

        let mut bits_ptr: *mut c_void = ptr::null_mut();
        let hbm = CreateDIBSection(hdc, &bmi, DIB_RGB_COLORS, &mut bits_ptr, ptr::null_mut(), 0);
        if hbm.is_null() {
            DestroyIcon(hicon.0 as _);
            DeleteDC(hdc);
            return Err("CreateDIBSection failed".into());
        }

        let old = SelectObject(hdc, hbm as *mut _);
        std::ptr::write_bytes(bits_ptr as *mut u8, 0u8, (size * size * 4) as usize);
        DrawIconEx(
            hdc,
            0,
            0,
            hicon.0 as _,
            size,
            size,
            0,
            ptr::null_mut(),
            DI_NORMAL,
        );

        let mut pixels = vec![0u8; (size * size * 4) as usize];
        GetDIBits(
            hdc,
            hbm,
            0,
            size as u32,
            pixels.as_mut_ptr() as *mut _,
            &mut bmi,
            DIB_RGB_COLORS,
        );

        SelectObject(hdc, old);
        DeleteObject(hbm as *mut _);
        DeleteDC(hdc);
        DestroyIcon(hicon.0 as _);

        for chunk in pixels.chunks_mut(4) {
            chunk.swap(0, 2);
        }

        let has_alpha = pixels.chunks(4).any(|c| c[3] > 0);
        if !has_alpha {
            for chunk in pixels.chunks_mut(4) {
                if chunk[0] != 0 || chunk[1] != 0 || chunk[2] != 0 {
                    chunk[3] = 255;
                }
            }
        }

        // Crop to content bounding box if the icon is smaller than the canvas
        // (happens when SHIL_JUMBO returns a small icon in a 256x256 canvas).
        let (enc_w, enc_h, enc_pixels);
        if shil == SHIL_JUMBO {
            let s = size as usize;
            let (mut min_x, mut min_y, mut max_x, mut max_y) = (s, s, 0usize, 0usize);
            for y in 0..s {
                for x in 0..s {
                    if pixels[(y * s + x) * 4 + 3] > 0 {
                        if x < min_x {
                            min_x = x;
                        }
                        if x > max_x {
                            max_x = x;
                        }
                        if y < min_y {
                            min_y = y;
                        }
                        if y > max_y {
                            max_y = y;
                        }
                    }
                }
            }
            let bw = if max_x >= min_x { max_x - min_x + 1 } else { s };
            let bh = if max_y >= min_y { max_y - min_y + 1 } else { s };
            if bw < (s * 70 / 100) || bh < (s * 70 / 100) {
                // Content is significantly smaller than canvas, crop it.
                let mut cropped = vec![0u8; bw * bh * 4];
                for y in 0..bh {
                    let src_off = ((min_y + y) * s + min_x) * 4;
                    let dst_off = y * bw * 4;
                    cropped[dst_off..dst_off + bw * 4]
                        .copy_from_slice(&pixels[src_off..src_off + bw * 4]);
                }
                enc_w = bw as u32;
                enc_h = bh as u32;
                enc_pixels = cropped;
            } else {
                enc_w = size as u32;
                enc_h = size as u32;
                enc_pixels = pixels;
            }
        } else {
            enc_w = size as u32;
            enc_h = size as u32;
            enc_pixels = pixels;
        }

        let mut buf = Vec::new();
        {
            let mut enc = png::Encoder::new(std::io::Cursor::new(&mut buf), enc_w, enc_h);
            enc.set_color(png::ColorType::Rgba);
            enc.set_depth(png::BitDepth::Eight);
            let mut writer = enc.write_header().map_err(|e| e.to_string())?;
            writer
                .write_image_data(&enc_pixels)
                .map_err(|e| e.to_string())?;
        }

        use base64::{engine::general_purpose::STANDARD, Engine};
        Ok(format!("data:image/png;base64,{}", STANDARD.encode(&buf)))
    }
}

#[cfg(not(target_os = "windows"))]
pub fn get_file_icon(_path: &str, _requested_size: u32) -> Result<String, String> {
    Err("Not supported on this platform".into())
}

/// Batch version: extract shell icons for multiple paths on a single thread.
/// Initializes COM once for the entire batch to avoid per-icon overhead.
/// Returns a map of path → data-URL for every path that succeeded.
#[cfg(target_os = "windows")]
pub fn get_file_icons_batch(
    paths: Vec<String>,
    size: u32,
) -> std::collections::HashMap<String, String> {
    use std::ptr;
    use winapi::um::combaseapi::{CoInitializeEx, CoUninitialize};
    use winapi::um::objbase::COINIT_APARTMENTTHREADED;
    use windows::Win32::UI::Controls::IImageList;
    use windows::Win32::UI::Shell::SHGetImageList;
    // RAII: balance CoInitializeEx only when it succeeded (S_OK / S_FALSE).
    // RPC_E_CHANGED_MODE (thread already MTA) must NOT be balanced; an extra
    // CoUninitialize would tear down an apartment we don't own. The shell
    // calls below work from MTA too, so proceed either way.
    struct ComGuard(bool);
    impl Drop for ComGuard {
        fn drop(&mut self) {
            if self.0 {
                unsafe {
                    CoUninitialize();
                }
            }
        }
    }
    let _com = ComGuard(unsafe { CoInitializeEx(ptr::null_mut(), COINIT_APARTMENTTHREADED) } >= 0);

    let n = paths.len();
    let inflight = ICON_BATCH_INFLIGHT.fetch_add(1, Ordering::Relaxed) + 1;
    let total = ICON_BATCH_TOTAL.fetch_add(1, Ordering::Relaxed) + 1;
    let start = std::time::Instant::now();
    // DEBUG, not INFO: one BEGIN/END pair per icon batch at INFO flooded the
    // release log file (and, pre-[LOG-02], every window's webview buffer).
    log::debug!(target: "folder:icons", "BEGIN  n={n} inflight={inflight} total={total}");

    let (shil, render_size) = select_shil(size);

    // Acquire the system image list once for the whole batch, serialized via
    // SHGETIMAGELIST_LOCK [S-64] so concurrent batches can't race shell32's
    // lazy init (E_OUTOFMEMORY bursts at startup). The retry remains as a
    // fallback for cold-COM transients (RPC_E_WRONG_THREAD /
    // CO_E_NOTINITIALIZED) that predate the lock, expected to never fire now.
    let image_list: Option<IImageList> = (0..3).find_map(|_| {
        let result = {
            // Scoped: hold the lock for the call only, never across the
            // backoff sleep below.
            let _g = SHGETIMAGELIST_LOCK
                .lock()
                .unwrap_or_else(|e| e.into_inner());
            unsafe { SHGetImageList(shil) }
        };
        match result {
            Ok(il) => Some(il),
            Err(e) => {
                log::warn!(target: "folder:icons", "SHGetImageList retry: {e}");
                std::thread::sleep(std::time::Duration::from_millis(10));
                None
            }
        }
    });

    let mut out = std::collections::HashMap::new();
    if let Some(image_list) = image_list {
        for path in paths {
            // Retry once per path; SHGetFileInfoW / GetIcon can transiently
            // fail on the very first shell call of a session.
            let mut result = get_file_icon_inner(&path, shil, render_size, &image_list);
            if result.is_err() {
                result = get_file_icon_inner(&path, shil, render_size, &image_list);
            }
            match result {
                Ok(icon) => {
                    out.insert(path, icon);
                }
                Err(e) => {
                    // DEBUG, not WARN: the common cause is a persisted exe path
                    // that no longer exists (app updated into a new versioned
                    // MSIX dir, uninstalled, temp installer gone). Callers
                    // already degrade gracefully (screentime [S-37] initial
                    // chip), so this is expected noise, not an error.
                    log::debug!(target: "folder:icons", "icon extract failed path={path}: {e}")
                }
            }
        }
    } else {
        log::error!(target: "folder:icons", "SHGetImageList failed after retries, batch skipped");
    }

    let elapsed = start.elapsed().as_millis();
    ICON_BATCH_INFLIGHT.fetch_sub(1, Ordering::Relaxed);
    log::debug!(target: "folder:icons", "END    n={n} elapsed={elapsed}ms ok={}", out.len());
    out
}

#[cfg(not(target_os = "windows"))]
pub fn get_file_icons_batch(
    paths: Vec<String>,
    _size: u32,
) -> std::collections::HashMap<String, String> {
    let _ = paths;
    std::collections::HashMap::new()
}

// ---------------------------------------------------------------------------
// ShellNew enumeration (Windows "New >" context menu entries)
// ---------------------------------------------------------------------------

#[derive(serde::Serialize, Clone)]
pub struct ShellNewEntry {
    pub extension: String,
    pub label: String,
    pub icon_base64: Option<String>,
    pub template_data: Option<Vec<u8>>,
}

#[cfg(target_os = "windows")]
pub fn get_shell_new_entries() -> Vec<ShellNewEntry> {
    use winreg::enums::{HKEY_CLASSES_ROOT, KEY_READ};
    use winreg::RegKey;

    let hkcr = RegKey::predef(HKEY_CLASSES_ROOT);
    let mut entries = Vec::new();

    for name in hkcr.enum_keys().flatten() {
        if !name.starts_with('.') {
            continue;
        }

        let ext_key = match hkcr.open_subkey_with_flags(&name, KEY_READ) {
            Ok(k) => k,
            Err(_) => continue,
        };

        // ShellNew can be directly at .ext\ShellNew or nested under a ProgID
        // subkey: .ext\<ProgID>\ShellNew (used by Office apps like Word, Excel).
        let shell_new = if let Ok(k) = ext_key.open_subkey_with_flags("ShellNew", KEY_READ) {
            k
        } else {
            // Check ProgID subkeys: .docx\Word.Document.12\ShellNew
            let mut found = None;
            for sub_name in ext_key.enum_keys().flatten() {
                if let Ok(sub) = ext_key.open_subkey_with_flags(&sub_name, KEY_READ) {
                    if let Ok(sn) = sub.open_subkey_with_flags("ShellNew", KEY_READ) {
                        found = Some(sn);
                        break;
                    }
                }
            }
            match found {
                Some(k) => k,
                None => continue,
            }
        };

        // Determine template data
        let template_data = if shell_new.get_value::<String, _>("NullFile").is_ok() {
            Some(Vec::new())
        } else if let Ok(data) = shell_new.get_raw_value("Data") {
            Some(data.bytes)
        } else if let Ok(file_name) = shell_new.get_value::<String, _>("FileName") {
            // Resolve template file path
            let template_path = if file_name.contains('\\') || file_name.contains('/') {
                std::path::PathBuf::from(&file_name)
            } else {
                // Look in %SystemRoot%\ShellNew
                let sys_root = std::env::var("SystemRoot").unwrap_or_else(|_| "C:\\Windows".into());
                std::path::Path::new(&sys_root)
                    .join("ShellNew")
                    .join(&file_name)
            };
            std::fs::read(&template_path).ok()
        } else {
            continue; // No recognized ShellNew type
        };

        // Get friendly name and icon via ProgID chain
        let label = strip_accelerator(&get_progid_friendly_name(&hkcr, &ext_key, &name));
        let icon_base64 = get_extension_icon_base64(&hkcr, &ext_key);

        entries.push(ShellNewEntry {
            extension: name,
            label,
            icon_base64,
            template_data,
        });
    }

    entries.sort_by(|a, b| a.label.to_lowercase().cmp(&b.label.to_lowercase()));
    entries
}

#[cfg(target_os = "windows")]
fn get_extension_icon_base64(hkcr: &winreg::RegKey, ext_key: &winreg::RegKey) -> Option<String> {
    use winreg::enums::KEY_READ;

    // Follow ProgID chain: .docx → "Word.Document.12" → HKCR\Word.Document.12\DefaultIcon
    let prog_id = ext_key
        .get_value::<String, _>("")
        .ok()
        .filter(|s| !s.is_empty())?;
    let prog_key = hkcr.open_subkey_with_flags(&prog_id, KEY_READ).ok()?;
    let icon_key = prog_key
        .open_subkey_with_flags("DefaultIcon", KEY_READ)
        .ok()?;
    let icon_path: String = icon_key.get_value("").ok()?;
    if icon_path.is_empty() {
        return None;
    }
    extract_icon_base64(&icon_path)
}

#[cfg(target_os = "windows")]
fn get_progid_friendly_name(
    hkcr: &winreg::RegKey,
    ext_key: &winreg::RegKey,
    extension: &str,
) -> String {
    use winreg::enums::KEY_READ;

    // Try ProgID default value → "txtfile" → HKCR\txtfile → default = "Text Document"
    if let Ok(prog_id) = ext_key.get_value::<String, _>("") {
        if !prog_id.is_empty() {
            if let Ok(prog_key) = hkcr.open_subkey_with_flags(&prog_id, KEY_READ) {
                if let Ok(name) = prog_key.get_value::<String, _>("") {
                    if !name.is_empty() {
                        return name;
                    }
                }
            }
        }
    }

    // Fallback: extension without dot, capitalized
    let ext = extension.trim_start_matches('.');
    format!("{} File", ext.to_uppercase())
}

#[cfg(not(target_os = "windows"))]
pub fn get_shell_new_entries() -> Vec<ShellNewEntry> {
    Vec::new()
}

// ---------------------------------------------------------------------------
// Create folder
// ---------------------------------------------------------------------------

pub fn fs_create_folder(dest_dir: &str, name: &str) -> Result<String, String> {
    // Confine to a safe leaf inside dest_dir; name is an IPC argument.
    let leaf = safe_leaf_name(name)?;
    let dest = unique_dest_path(std::path::Path::new(dest_dir), std::ffi::OsStr::new(&leaf));
    std::fs::create_dir(&dest).map_err(|e| e.to_string())?;
    Ok(dest.to_string_lossy().to_string())
}

// ---------------------------------------------------------------------------
// Windows shell context menu entries ("Open with Code", etc.)
// ---------------------------------------------------------------------------

#[derive(serde::Serialize, Clone)]
pub struct ShellMenuEntry {
    pub id: String,
    pub label: String,
    pub icon_path: Option<String>,
    pub icon_base64: Option<String>,
    pub command: String,
    pub extended: bool,
}

/// Strip Windows `&` accelerator prefixes: `&&` → `&`, `&X` → `X`.
fn strip_accelerator(s: &str) -> String {
    let mut result = String::with_capacity(s.len());
    let mut chars = s.chars();
    while let Some(c) = chars.next() {
        if c == '&' {
            if let Some(next) = chars.next() {
                result.push(next);
            }
        } else {
            result.push(c);
        }
    }
    result
}

#[cfg(target_os = "windows")]
fn parse_icon_resource(raw: &str) -> (String, i32) {
    let s = raw.trim().trim_matches('"');
    if let Some(pos) = s.rfind(',') {
        // Re-trim quotes on the path part: the whole-string trim_matches above
        // only strips the leading quote when a `,index` follows (the string ends
        // in the index, not a quote), leaving a stray `"` before the comma.
        let path_part = s[..pos].trim().trim_matches('"');
        let idx_part = s[pos + 1..].trim();
        if let Ok(idx) = idx_part.parse::<i32>() {
            return (path_part.to_string(), idx);
        }
    }
    (s.to_string(), 0)
}

#[cfg(target_os = "windows")]
fn extract_icon_base64(icon_path_raw: &str) -> Option<String> {
    use std::ffi::OsStr;
    use std::os::windows::ffi::OsStrExt;
    use winapi::shared::minwindef::UINT;
    use winapi::um::shellapi::ExtractIconExW;
    use winapi::um::wingdi::{
        CreateCompatibleDC, DeleteDC, DeleteObject, GetDIBits, BITMAPINFOHEADER, BI_RGB,
        DIB_RGB_COLORS,
    };
    use winapi::um::winuser::{DestroyIcon, GetIconInfo};

    let (path, index) = parse_icon_resource(icon_path_raw);

    // Expand environment variables (e.g. %SystemRoot%)
    let expanded = {
        let mut s = path;
        while let Some(start) = s.find('%') {
            if let Some(end) = s[start + 1..].find('%') {
                let var_name = &s[start + 1..start + 1 + end];
                if let Ok(val) = std::env::var(var_name) {
                    s = format!("{}{}{}", &s[..start], val, &s[start + 2 + end..]);
                } else {
                    break;
                }
            } else {
                break;
            }
        }
        s
    };

    let wide_path: Vec<u16> = OsStr::new(&expanded).encode_wide().chain(Some(0)).collect();
    const SIZE: usize = 16;

    unsafe {
        let mut h_small = std::ptr::null_mut();
        let count = ExtractIconExW(
            wide_path.as_ptr(),
            index,
            std::ptr::null_mut(),
            &mut h_small,
            1,
        );
        if count == 0 || h_small.is_null() {
            return None;
        }

        let mut icon_info = std::mem::zeroed::<winapi::um::winuser::ICONINFO>();
        if GetIconInfo(h_small, &mut icon_info) == 0 {
            DestroyIcon(h_small);
            return None;
        }

        let hdc = CreateCompatibleDC(std::ptr::null_mut());
        if hdc.is_null() {
            if !icon_info.hbmMask.is_null() {
                DeleteObject(icon_info.hbmMask as _);
            }
            if !icon_info.hbmColor.is_null() {
                DeleteObject(icon_info.hbmColor as _);
            }
            DestroyIcon(h_small);
            return None;
        }

        let mut bmi = std::mem::zeroed::<winapi::um::wingdi::BITMAPINFO>();
        bmi.bmiHeader.biSize = std::mem::size_of::<BITMAPINFOHEADER>() as u32;
        bmi.bmiHeader.biWidth = SIZE as i32;
        bmi.bmiHeader.biHeight = -(SIZE as i32); // top-down
        bmi.bmiHeader.biPlanes = 1;
        bmi.bmiHeader.biBitCount = 32;
        bmi.bmiHeader.biCompression = BI_RGB;

        let mut pixels = vec![0u8; SIZE * SIZE * 4];

        let source_bmp = if !icon_info.hbmColor.is_null() {
            icon_info.hbmColor
        } else {
            icon_info.hbmMask
        };

        let rows = GetDIBits(
            hdc,
            source_bmp,
            0,
            SIZE as UINT,
            pixels.as_mut_ptr() as *mut _,
            &mut bmi,
            DIB_RGB_COLORS,
        );

        DeleteDC(hdc);
        if !icon_info.hbmMask.is_null() {
            DeleteObject(icon_info.hbmMask as _);
        }
        if !icon_info.hbmColor.is_null() {
            DeleteObject(icon_info.hbmColor as _);
        }
        DestroyIcon(h_small);

        if rows == 0 {
            return None;
        }

        // BGRA → RGBA
        for chunk in pixels.chunks_exact_mut(4) {
            chunk.swap(0, 2);
        }

        let img = image::RgbaImage::from_raw(SIZE as u32, SIZE as u32, pixels)?;
        let mut png_buf = Vec::new();
        img.write_to(
            &mut std::io::Cursor::new(&mut png_buf),
            image::ImageFormat::Png,
        )
        .ok()?;

        use base64::Engine;
        Some(base64::engine::general_purpose::STANDARD.encode(&png_buf))
    }
}

#[cfg(target_os = "windows")]
pub fn get_shell_menu_entries(is_directory: bool, is_background: bool) -> Vec<ShellMenuEntry> {
    use winreg::enums::{HKEY_CLASSES_ROOT, KEY_READ};
    use winreg::RegKey;

    let hkcr = RegKey::predef(HKEY_CLASSES_ROOT);
    let mut entries = Vec::new();

    let shell_paths: Vec<&str> = if is_background {
        vec!["Directory\\Background\\shell"]
    } else if is_directory {
        vec!["Directory\\shell"]
    } else {
        vec!["*\\shell"]
    };

    for shell_path in shell_paths {
        let shell_key = match hkcr.open_subkey_with_flags(shell_path, KEY_READ) {
            Ok(k) => k,
            Err(_) => continue,
        };

        for key_name in shell_key.enum_keys().flatten() {
            let sub = match shell_key.open_subkey_with_flags(&key_name, KEY_READ) {
                Ok(k) => k,
                Err(_) => continue,
            };

            // Skip entries with LegacyDisable or ProgrammaticAccessOnly
            if sub.get_value::<String, _>("LegacyDisable").is_ok()
                || sub.get_raw_value("LegacyDisable").is_ok()
                || sub.get_value::<String, _>("ProgrammaticAccessOnly").is_ok()
                || sub.get_raw_value("ProgrammaticAccessOnly").is_ok()
            {
                continue;
            }

            // Get command
            let command = match sub.open_subkey_with_flags("command", KEY_READ) {
                Ok(cmd_key) => match cmd_key.get_value::<String, _>("") {
                    Ok(c) if !c.is_empty() => c,
                    _ => continue,
                },
                Err(_) => continue,
            };

            // Get label: prefer MUIVerb, then default value, then key name
            let label = strip_accelerator(
                &sub.get_value::<String, _>("MUIVerb")
                    .ok()
                    .filter(|s| !s.is_empty() && !s.starts_with('@'))
                    .or_else(|| {
                        sub.get_value::<String, _>("")
                            .ok()
                            .filter(|s| !s.is_empty() && !s.starts_with('@'))
                    })
                    .unwrap_or_else(|| key_name.clone()),
            );

            let icon_path = sub.get_value::<String, _>("Icon").ok();
            let icon_base64 = icon_path.as_deref().and_then(extract_icon_base64);
            let extended = sub.get_raw_value("Extended").is_ok();

            entries.push(ShellMenuEntry {
                id: key_name,
                label,
                icon_path,
                icon_base64,
                command,
                extended,
            });
        }
    }

    // Deduplicate by id
    let mut seen = std::collections::HashSet::new();
    entries.retain(|e| seen.insert(e.id.clone()));

    entries
}

#[cfg(not(target_os = "windows"))]
pub fn get_shell_menu_entries(_is_directory: bool, _is_background: bool) -> Vec<ShellMenuEntry> {
    Vec::new()
}

// ---------------------------------------------------------------------------
// Execute a shell context menu command
// ---------------------------------------------------------------------------

/// Substitute the Windows shell path placeholders with the target path.
#[cfg(target_os = "windows")]
fn substitute_path(s: &str, target_path: &str) -> String {
    s.replace("%1", target_path)
        .replace("%V", target_path)
        .replace("%L", target_path)
        .replace("%W", target_path)
}

/// A target path is unsafe to hand to a shell verb if it contains characters
/// that are ILLEGAL in Windows paths (`" < > |`) or control characters. Such a
/// value is malformed or hostile: reject it rather than execute. (S8) Real
/// filesystem paths never contain these. (`&`, `^`, `(`, `)`, `%` ARE legal in
/// filenames; those are handled safely by executing via argv, not a shell.)
fn is_safe_target_path(p: &str) -> bool {
    !p.chars()
        .any(|c| matches!(c, '"' | '<' | '>' | '|') || c.is_control())
}

/// Split a Windows command template into argv tokens, honouring double-quoted
/// segments (quotes are stripped). e.g. `"C:\Program Files\a.exe" "%1"` →
/// [`C:\Program Files\a.exe`, `%1`].
#[cfg(target_os = "windows")]
fn tokenize_windows_command(s: &str) -> Vec<String> {
    let mut tokens = Vec::new();
    let mut cur = String::new();
    let mut in_quotes = false;
    let mut has_token = false;
    for c in s.chars() {
        match c {
            '"' => {
                in_quotes = !in_quotes;
                has_token = true;
            }
            c if c.is_whitespace() && !in_quotes => {
                if has_token {
                    tokens.push(std::mem::take(&mut cur));
                    has_token = false;
                }
            }
            c => {
                cur.push(c);
                has_token = true;
            }
        }
    }
    if has_token {
        tokens.push(cur);
    }
    tokens
}

/// Expand `%NAME%` environment-variable references (Windows-style) so verbs that
/// use `%SystemRoot%` etc. still resolve without a shell. Unknown vars and the
/// path placeholders (`%1`, no closing `%`) are left intact. Run on the trusted
/// template BEFORE the path is substituted, so a `%` inside the path is never
/// treated as a variable.
#[cfg(target_os = "windows")]
fn expand_env_vars(s: &str) -> String {
    let mut out = String::with_capacity(s.len());
    let mut rest = s;
    while let Some(start) = rest.find('%') {
        out.push_str(&rest[..start]);
        let after = &rest[start + 1..];
        if let Some(end) = after.find('%') {
            let name = &after[..end];
            if !name.is_empty()
                && name
                    .chars()
                    .all(|c| c.is_ascii_alphanumeric() || c == '_' || c == '(' || c == ')')
            {
                match std::env::var(name) {
                    Ok(val) => out.push_str(&val),
                    Err(_) => {
                        out.push('%');
                        out.push_str(name);
                        out.push('%');
                    }
                }
                rest = &after[end + 1..];
                continue;
            }
        }
        // Lone `%` (e.g. the `%1` placeholder): emit literally, move past it.
        out.push('%');
        rest = after;
    }
    out.push_str(rest);
    out
}

/// Run a Windows Explorer context-menu verb for `target_path`, selected by the
/// verb's registry key id (NOT by a caller-supplied command string).
///
/// SECURITY (S4/S8/S-shellverb): the command string is NEVER taken from the
/// caller. The frontend round-trips through this command, so a command string
/// passed in would be attacker-controllable → arbitrary argv execution. Instead
/// we re-enumerate the trusted shell verbs from the registry here (same context
/// the menu was built with) and look the verb up by `verb_id`; only the
/// registry's own command template is ever executed. The template is then run
/// via real argv, NOT a `cmd /C "<string>"`, so a folder name containing cmd
/// metacharacters (`&`, `|`, …) cannot break out and run arbitrary commands:
/// the template is tokenized once, env vars expanded, and the path substituted
/// into a single argv element with no shell to re-interpret it.
pub fn execute_shell_command(
    verb_id: &str,
    target_path: &str,
    is_directory: bool,
    is_background: bool,
) -> Result<(), String> {
    if !is_safe_target_path(target_path) {
        return Err("Refusing to run a shell command: the path contains illegal characters".into());
    }

    // Re-derive the trusted command from the registry by id. Never trust a
    // command string from the caller.
    let entry = get_shell_menu_entries(is_directory, is_background)
        .into_iter()
        .find(|e| e.id == verb_id)
        .ok_or_else(|| format!("Unknown shell verb '{verb_id}'"))?;
    let command_template = entry.command;

    #[cfg(target_os = "windows")]
    {
        use std::os::windows::process::CommandExt;
        const CREATE_NO_WINDOW: u32 = 0x08000000;

        let mut tokens = tokenize_windows_command(&command_template).into_iter();
        let program_tmpl = tokens
            .next()
            .ok_or_else(|| "Empty shell command".to_string())?;
        // Expand env vars on the template token first, then insert the path (so a
        // `%` in the path is never env-expanded).
        let program = substitute_path(&expand_env_vars(&program_tmpl), target_path);
        let args: Vec<String> = tokens
            .map(|t| substitute_path(&expand_env_vars(&t), target_path))
            .collect();

        Command::new(&program)
            .args(&args)
            .creation_flags(CREATE_NO_WINDOW)
            .spawn()
            .map_err(|e| format!("Failed to launch '{program}': {e}"))?;
    }

    #[cfg(not(target_os = "windows"))]
    {
        // Folder widget is Windows-only (shell verbs come from the Windows
        // registry); this branch exists only so the crate compiles cross-platform
        // and is never reached in a shipped build.
        let _ = command_template;
    }

    Ok(())
}

// ---------------------------------------------------------------------------
// File system: rename, folder item count, image thumbnail
// ---------------------------------------------------------------------------

/// Rename a file or folder in-place within its current directory.
pub fn fs_rename(path: &str, new_name: &str) -> Result<(), String> {
    let src = std::path::Path::new(path);
    let parent = src
        .parent()
        .ok_or_else(|| "No parent directory".to_string())?;
    // A rename must stay in the SAME directory: confine new_name to a safe leaf so
    // `..\..\x` can't move/overwrite a file outside the item's parent.
    let leaf = safe_leaf_name(new_name)?;
    let dest = parent.join(&leaf);
    if dest.exists() {
        return Err(format!("'{}' already exists", leaf));
    }
    std::fs::rename(src, &dest).map_err(|e| e.to_string())
}

/// Return the number of immediate children for each directory path.
pub fn get_folder_item_counts(paths: Vec<String>) -> std::collections::HashMap<String, u32> {
    paths
        .into_iter()
        .map(|p| {
            let count = std::fs::read_dir(&p).map(|d| d.count() as u32).unwrap_or(0);
            (p, count)
        })
        .collect()
}

/// Open the Windows "Open with…" app chooser for a file (the `openas` shell
/// verb). Unlike the enumerated `*\shell` registry verbs, this OS app-picker is
/// a shell COM cascade and never appears in `get_shell_menu_entries`, so the
/// file context menu invokes it directly.
pub fn open_with(path: &str) -> Result<(), String> {
    if !is_safe_target_path(path) {
        return Err("Refusing to open: the path contains illegal characters".into());
    }
    #[cfg(target_os = "windows")]
    {
        use winapi::um::shellapi::{ShellExecuteExW, SHELLEXECUTEINFOW};
        const SEE_MASK_INVOKEIDLIST: u32 = 0x0000_000C;
        let verb: Vec<u16> = "openas\0".encode_utf16().collect();
        let path_w: Vec<u16> = path.encode_utf16().chain(std::iter::once(0)).collect();
        let mut sei = unsafe { std::mem::zeroed::<SHELLEXECUTEINFOW>() };
        sei.cbSize = std::mem::size_of::<SHELLEXECUTEINFOW>() as u32;
        sei.fMask = SEE_MASK_INVOKEIDLIST;
        sei.lpVerb = verb.as_ptr();
        sei.lpFile = path_w.as_ptr();
        sei.nShow = 1; // SW_SHOWNORMAL
        let ok = unsafe { ShellExecuteExW(&mut sei) };
        if ok == 0 {
            return Err("Could not open the 'Open with' dialog".into());
        }
    }
    #[cfg(not(target_os = "windows"))]
    {
        let _ = path;
    }
    Ok(())
}

/// Show the Windows file properties dialog for a file or folder.
pub fn show_file_properties(path: &str) -> Result<(), String> {
    #[cfg(target_os = "windows")]
    {
        use winapi::um::shellapi::{ShellExecuteExW, SHELLEXECUTEINFOW};
        // SEE_MASK_INVOKEIDLIST is required to reliably invoke the "properties" verb
        const SEE_MASK_INVOKEIDLIST: u32 = 0x0000_000C;
        let verb: Vec<u16> = "properties\0".encode_utf16().collect();
        let path_w: Vec<u16> = path.encode_utf16().chain(std::iter::once(0)).collect();
        let mut sei = unsafe { std::mem::zeroed::<SHELLEXECUTEINFOW>() };
        sei.cbSize = std::mem::size_of::<SHELLEXECUTEINFOW>() as u32;
        sei.fMask = SEE_MASK_INVOKEIDLIST;
        sei.lpVerb = verb.as_ptr();
        sei.lpFile = path_w.as_ptr();
        sei.nShow = 1; // SW_SHOWNORMAL
        unsafe { ShellExecuteExW(&mut sei) };
    }
    Ok(())
}

/// Fallback thumbnail generation using the `image` crate (reads full file).
fn get_image_thumbnail_fallback(path: &str, size: u32) -> Result<String, String> {
    use base64::Engine;
    let img = image::open(path).map_err(|e| e.to_string())?;
    let thumb = img.thumbnail(size, size);
    let mut cursor = std::io::Cursor::new(Vec::<u8>::new());
    thumb
        .write_to(&mut cursor, image::ImageFormat::Png)
        .map_err(|e| e.to_string())?;
    Ok(format!(
        "data:image/png;base64,{}",
        base64::engine::general_purpose::STANDARD.encode(cursor.into_inner())
    ))
}

/// Windows Shell thumbnail via IShellItemImageFactory; does NOT trigger
/// cloud file downloads because we pass SIIGBF_THUMBNAILONLY.
#[cfg(target_os = "windows")]
fn get_image_thumbnail_shell(path: &str, size: u32) -> Result<String, String> {
    use std::ptr;
    use winapi::um::wingdi::{
        CreateCompatibleDC, DeleteDC, DeleteObject, GetDIBits, GetObjectW, BITMAP, BITMAPINFO,
        BITMAPINFOHEADER, DIB_RGB_COLORS,
    };
    use winapi::um::winuser::{GetDC, ReleaseDC};
    use windows::core::{Interface, HSTRING};
    use windows::Win32::Foundation::SIZE;
    use windows::Win32::UI::Shell::{IShellItemImageFactory, SHCreateItemFromParsingName, SIIGBF};

    unsafe {
        let item: windows::Win32::UI::Shell::IShellItem =
            SHCreateItemFromParsingName(&HSTRING::from(path), None)
                .map_err(|e| format!("SHCreateItemFromParsingName: {e}"))?;

        let factory: IShellItemImageFactory = item
            .cast()
            .map_err(|e| format!("cast to IShellItemImageFactory: {e}"))?;

        let req = SIZE {
            cx: size as i32,
            cy: size as i32,
        };
        let hbm = factory
            .GetImage(req, SIIGBF(0x1 | 0x200))
            .map_err(|e| format!("GetImage: {e}"))?;

        // Query actual bitmap dimensions
        let mut bmp: BITMAP = std::mem::zeroed();
        let got = GetObjectW(
            hbm.0 as *mut _,
            std::mem::size_of::<BITMAP>() as i32,
            &mut bmp as *mut _ as *mut _,
        );
        if got == 0 {
            DeleteObject(hbm.0 as *mut _);
            return Err("GetObjectW(HBITMAP) failed".into());
        }
        let w = bmp.bmWidth;
        let h = bmp.bmHeight.abs();

        // Extract pixel data via GetDIBits
        let hdc_screen = GetDC(ptr::null_mut());
        let hdc = CreateCompatibleDC(hdc_screen);
        ReleaseDC(ptr::null_mut(), hdc_screen);

        let mut bmi: BITMAPINFO = std::mem::zeroed();
        bmi.bmiHeader.biSize = std::mem::size_of::<BITMAPINFOHEADER>() as u32;
        bmi.bmiHeader.biWidth = w;
        bmi.bmiHeader.biHeight = -h; // top-down
        bmi.bmiHeader.biPlanes = 1;
        bmi.bmiHeader.biBitCount = 32;
        bmi.bmiHeader.biCompression = 0;

        let pixel_count = (w * h * 4) as usize;
        let mut pixels = vec![0u8; pixel_count];
        GetDIBits(
            hdc,
            hbm.0 as _,
            0,
            h as u32,
            pixels.as_mut_ptr() as *mut _,
            &mut bmi,
            DIB_RGB_COLORS,
        );

        DeleteObject(hbm.0 as *mut _);
        DeleteDC(hdc);

        // BGRA → RGBA
        for chunk in pixels.chunks_mut(4) {
            chunk.swap(0, 2);
        }
        // Alpha fixup
        let has_alpha = pixels.chunks(4).any(|c| c[3] > 0);
        if !has_alpha {
            for chunk in pixels.chunks_mut(4) {
                if chunk[0] != 0 || chunk[1] != 0 || chunk[2] != 0 {
                    chunk[3] = 255;
                }
            }
        }

        // Resize if returned bitmap is larger than requested
        let (enc_w, enc_h, enc_pixels) = if w as u32 > size || h as u32 > size {
            let rgba = image::RgbaImage::from_raw(w as u32, h as u32, pixels)
                .ok_or("failed to create RgbaImage from shell thumbnail")?;
            let resized = image::DynamicImage::ImageRgba8(rgba).thumbnail(size, size);
            let rw = resized.width();
            let rh = resized.height();
            (rw, rh, resized.into_rgba8().into_raw())
        } else {
            (w as u32, h as u32, pixels)
        };

        // Encode PNG
        let mut buf = Vec::new();
        {
            let mut enc = png::Encoder::new(std::io::Cursor::new(&mut buf), enc_w, enc_h);
            enc.set_color(png::ColorType::Rgba);
            enc.set_depth(png::BitDepth::Eight);
            let mut writer = enc.write_header().map_err(|e| e.to_string())?;
            writer
                .write_image_data(&enc_pixels)
                .map_err(|e| e.to_string())?;
        }

        use base64::{engine::general_purpose::STANDARD, Engine};
        Ok(format!("data:image/png;base64,{}", STANDARD.encode(&buf)))
    }
}

/// Public entry point; tries Windows Shell thumbnail first (cloud-safe),
/// falls back to image::open if Shell API fails.
#[cfg(target_os = "windows")]
pub fn get_image_thumbnail(path: &str, size: u32) -> Result<String, String> {
    get_image_thumbnail_shell(path, size).or_else(|shell_err| {
        // The shell path passes SIIGBF_THUMBNAILONLY so it never hydrates a
        // cloud-only (dehydrated) file. The image::open fallback, however,
        // opens the file for real and would force a download, defeating the
        // whole point. Skip the hydrating fallback for cloud-only files and
        // let the UI show a placeholder instead.
        if is_cloud_only(path) {
            return Err(format!(
                "thumbnail unavailable for cloud-only file (shell: {shell_err})"
            ));
        }
        get_image_thumbnail_fallback(path, size)
    })
}

/// True when the file is a dehydrated cloud placeholder (OneDrive Files
/// On-Demand, etc.) that has no local data; opening it would trigger a
/// network download.
#[cfg(target_os = "windows")]
fn is_cloud_only(path: &str) -> bool {
    attrs_are_cloud_only(get_file_attributes(path))
}

/// Pure predicate over a Windows file-attribute bitmask: any of
/// FILE_ATTRIBUTE_RECALL_ON_OPEN | RECALL_ON_DATA_ACCESS | OFFLINE means the
/// file's data isn't local. Split out so it can be unit-tested without a real
/// cloud-only file on disk.
#[cfg(target_os = "windows")]
fn attrs_are_cloud_only(attrs: u32) -> bool {
    const RECALL_ON_OPEN: u32 = 0x0004_0000;
    const RECALL_ON_DATA_ACCESS: u32 = 0x0040_0000;
    const OFFLINE: u32 = 0x0000_1000;
    attrs & (RECALL_ON_OPEN | RECALL_ON_DATA_ACCESS | OFFLINE) != 0
}

#[cfg(not(target_os = "windows"))]
pub fn get_image_thumbnail(path: &str, size: u32) -> Result<String, String> {
    get_image_thumbnail_fallback(path, size)
}

/// Batch thumbnail generation; processes images in parallel across CPU cores.
pub fn get_image_thumbnails_batch(
    paths: Vec<String>,
    size: u32,
) -> std::collections::HashMap<String, String> {
    use std::sync::mpsc;

    let n = paths.len();
    let inflight = THUMB_BATCH_INFLIGHT.fetch_add(1, Ordering::Relaxed) + 1;
    let total = THUMB_BATCH_TOTAL.fetch_add(1, Ordering::Relaxed) + 1;
    let start = std::time::Instant::now();

    // Cap concurrency: IShellItemImageFactory calls serialize through
    // explorer.exe, so 8+ OS threads just pile up waiting on the same COM
    // apartment. 4 is enough to keep throughput up without hammering explorer.
    let concurrency = std::thread::available_parallelism()
        .map(|n| n.get())
        .unwrap_or(4)
        .min(4)
        .min(paths.len());
    log::info!(target: "folder:thumbs", "BEGIN n={n} threads={concurrency} inflight={inflight} total={total}");

    let (tx, rx) = mpsc::channel::<(String, String)>();
    let paths = std::sync::Arc::new(std::sync::Mutex::new(paths.into_iter()));

    let mut handles = Vec::with_capacity(concurrency);
    for _ in 0..concurrency {
        let paths = paths.clone();
        let tx = tx.clone();
        handles.push(std::thread::spawn(move || {
            // Balance CoInitializeEx only on success (S_OK / S_FALSE); never
            // balance RPC_E_CHANGED_MODE. See get_file_icons_batch.
            #[cfg(target_os = "windows")]
            let com_ok = unsafe {
                winapi::um::combaseapi::CoInitializeEx(
                    std::ptr::null_mut(),
                    winapi::um::objbase::COINIT_APARTMENTTHREADED,
                ) >= 0
            };
            loop {
                let path = {
                    // Poison-safe: a panic in another worker thread must not
                    // turn this lock acquisition into a cascading panic (the
                    // remaining iterator state is still usable). Recover the
                    // guard from the poison error rather than unwrap-panicking.
                    let mut iter = paths.lock().unwrap_or_else(|e| e.into_inner());
                    iter.next()
                };
                let Some(path) = path else { break };
                if let Ok(thumb) = get_image_thumbnail(&path, size) {
                    let _ = tx.send((path, thumb));
                }
            }
            #[cfg(target_os = "windows")]
            if com_ok {
                unsafe {
                    winapi::um::combaseapi::CoUninitialize();
                }
            }
        }));
    }
    drop(tx); // close sender so rx iterator ends when all threads finish

    let mut out = std::collections::HashMap::new();
    for (path, thumb) in rx {
        out.insert(path, thumb);
    }
    for h in handles {
        let _ = h.join();
    }
    let elapsed = start.elapsed().as_millis();
    THUMB_BATCH_INFLIGHT.fetch_sub(1, Ordering::Relaxed);
    log::info!(target: "folder:thumbs", "END   n={n} elapsed={elapsed}ms ok={}", out.len());
    out
}

// ---------------------------------------------------------------------------
// Shell-command execution: security tests (S4/S8)
// ---------------------------------------------------------------------------
#[cfg(all(test, target_os = "windows"))]
mod shell_exec_tests {
    use super::*;

    // Security: an untrusted filename (remote Content-Disposition / IPC arg) must
    // never let a write/rename/create escape the destination directory.
    #[test]
    fn safe_leaf_name_confines_to_destination() {
        // Normal names pass through.
        assert_eq!(safe_leaf_name("photo.jpg").unwrap(), "photo.jpg");
        assert_eq!(
            safe_leaf_name("My File (1).txt").unwrap(),
            "My File (1).txt"
        );
        // Directory prefixes are stripped to the leaf (which is then confined).
        assert_eq!(safe_leaf_name("a/b/c.txt").unwrap(), "c.txt");
        assert_eq!(safe_leaf_name("../../etc/passwd").unwrap(), "passwd");
        // The classic exploit: backslash traversal + absolute path reduce to a leaf
        // (Windows) and join inside dest, never to Startup / another drive.
        assert_eq!(safe_leaf_name(r"..\..\Startup\run.bat").unwrap(), "run.bat");
        assert_eq!(
            safe_leaf_name(r"C:\Windows\System32\evil.dll").unwrap(),
            "evil.dll"
        );
        // Unusable names are rejected outright.
        for bad in ["", ".", "..", "   ", "\u{0}"] {
            assert!(safe_leaf_name(bad).is_err(), "must reject {bad:?}");
        }
        // INVARIANT: an Ok leaf joined to a base dir never escapes the base.
        let base = std::path::Path::new(r"C:\dest");
        for input in [r"..\..\x", "C:/abs/y.txt", "../../../z", "ok.png"] {
            if let Ok(leaf) = safe_leaf_name(input) {
                assert_eq!(
                    base.join(&leaf).parent(),
                    Some(base),
                    "escaped for {input:?}"
                );
            }
        }
    }

    // Regression [S-54]: dropping files back onto their own folder widget used
    // to forward a same-directory move to IFileOperation, which could delete the
    // dragged item. The move engine must skip a source that already lives in the
    // destination, regardless of slash/case formatting.
    #[test]
    fn same_dir_move_is_detected_across_formats() {
        use shell_file_op::is_same_dir_move;
        // file already in dest: different slash + case + trailing separator
        assert!(is_same_dir_move(r"D:\Docs\a.txt", "D:/Docs/"));
        assert!(is_same_dir_move(r"C:\Users\Foo\x", r"c:\users\foo"));
        // folder dropped onto itself
        assert!(is_same_dir_move(r"D:\Docs\Sub", r"D:\Docs\Sub"));
        // genuine move into a different directory must NOT be skipped
        assert!(!is_same_dir_move(r"D:\Docs\a.txt", r"D:\Docs\Sub"));
        assert!(!is_same_dir_move(r"D:\Other\b.txt", r"D:\Docs"));
    }

    #[test]
    fn rejects_paths_with_shell_illegal_chars() {
        // A `"` would let a crafted name break argv quoting; `<>|` and control
        // chars are illegal in real Windows paths.
        assert!(!is_safe_target_path(r#"C:\a" & calc & ".txt"#));
        assert!(!is_safe_target_path(r"C:\a|b"));
        assert!(!is_safe_target_path(r"C:\a<b"));
        assert!(!is_safe_target_path(r"C:\a>b"));
        assert!(!is_safe_target_path("C:\\a\nb"));
    }

    #[test]
    fn accepts_legal_paths_including_ampersand_and_percent() {
        // `&`, `^`, `(`, `)`, `%`, spaces are all legal in Windows file names,
        // they must be accepted (and are safe because we exec via argv).
        assert!(is_safe_target_path(r"C:\Users\me\Q1 & Q2 (50%) ^notes.txt"));
        assert!(is_safe_target_path(r"C:\Program Files\My App\file.txt"));
    }

    // S8: the command run is selected by registry verb *id*, never supplied by the
    // caller. A verb id present in no registry menu must be refused: this is the
    // regression guard for the old API that executed a caller-supplied command
    // string (attacker-controllable through the IPC round-trip → arbitrary exec).
    #[test]
    fn s8_unknown_verb_id_is_rejected_not_executed() {
        let r = execute_shell_command(
            "themia::definitely-not-a-real-verb-id",
            r"C:\Windows",
            true,
            false,
        );
        assert!(r.is_err(), "unknown verb id must be rejected, got {r:?}");
        assert!(
            r.unwrap_err().contains("Unknown shell verb"),
            "error should name the unknown verb",
        );
    }

    // S8: an illegal target path is refused before any registry lookup or exec.
    #[test]
    fn s8_illegal_target_path_is_rejected_before_lookup() {
        let r = execute_shell_command("open", r#"C:\a" & calc & ".txt"#, false, false);
        assert!(r.is_err(), "illegal path must be rejected, got {r:?}");
    }

    // open_with shares the same path guard; an illegal path is refused before
    // any ShellExecute call.
    #[test]
    fn open_with_rejects_illegal_target_path() {
        let r = open_with(r#"C:\a" & calc & ".txt"#);
        assert!(r.is_err(), "illegal path must be rejected, got {r:?}");
    }

    #[test]
    fn select_shil_picks_size_bucket() {
        // <=16 → small/16, <=32 → extralarge/48, else jumbo/256. Boundaries
        // land in the lower bucket (uses <=).
        assert_eq!(select_shil(0), (SHIL_SMALL, 16));
        assert_eq!(select_shil(1), (SHIL_SMALL, 16));
        assert_eq!(select_shil(16), (SHIL_SMALL, 16));
        assert_eq!(select_shil(17), (SHIL_EXTRALARGE, 48));
        assert_eq!(select_shil(32), (SHIL_EXTRALARGE, 48));
        assert_eq!(select_shil(33), (SHIL_JUMBO, 256));
        assert_eq!(select_shil(256), (SHIL_JUMBO, 256));
        // Adversarial: a huge requested size must not panic, just lands in jumbo.
        assert_eq!(select_shil(u32::MAX), (SHIL_JUMBO, 256));
    }

    #[test]
    fn parse_icon_resource_splits_path_and_index() {
        // "path,N" → (path, N).
        assert_eq!(
            parse_icon_resource(r"C:\Windows\System32\shell32.dll,3"),
            (r"C:\Windows\System32\shell32.dll".to_string(), 3)
        );
        // Surrounding quotes and whitespace are trimmed.
        assert_eq!(
            parse_icon_resource(r#"  "C:\a\b.dll" , 12 "#),
            (r"C:\a\b.dll".to_string(), 12)
        );
        // Negative resource indices are valid (resource ID vs ordinal).
        assert_eq!(
            parse_icon_resource(r"C:\a.exe,-100"),
            (r"C:\a.exe".to_string(), -100)
        );
        // No comma → whole string is the path, index defaults to 0.
        assert_eq!(
            parse_icon_resource(r"C:\a\icon.ico"),
            (r"C:\a\icon.ico".to_string(), 0)
        );
        // Trailing comma with non-numeric suffix → not a valid index, so the
        // whole (trimmed) string is treated as the path with index 0.
        assert_eq!(
            parse_icon_resource(r"C:\a.dll,notanumber"),
            (r"C:\a.dll,notanumber".to_string(), 0)
        );
        // Empty input must not panic.
        assert_eq!(parse_icon_resource(""), (String::new(), 0));
    }

    #[test]
    fn expand_env_vars_expands_known_and_preserves_unknown() {
        // A known env var expands; set one deterministically for the test.
        std::env::set_var("THEMIA_TEST_EXPAND_VAR", "EXPANDED");
        assert_eq!(
            expand_env_vars("a %THEMIA_TEST_EXPAND_VAR% b"),
            "a EXPANDED b"
        );
        // Multiple references in one string.
        assert_eq!(
            expand_env_vars("%THEMIA_TEST_EXPAND_VAR%/%THEMIA_TEST_EXPAND_VAR%"),
            "EXPANDED/EXPANDED"
        );
        // A `%(name)%`-style var with parens is allowed by the char filter.
        // Unknown ones are still preserved literally (no panic on the parens).
        assert_eq!(
            expand_env_vars("%ProgramFiles(x86)_NOPE%"),
            "%ProgramFiles(x86)_NOPE%"
        );
        // Empty `%%` is not a valid name → left literal, no infinite loop.
        assert_eq!(expand_env_vars("a %% b"), "a %% b");
        std::env::remove_var("THEMIA_TEST_EXPAND_VAR");
    }

    #[test]
    fn substitute_path_replaces_all_placeholders() {
        let p = r"C:\dir\file.txt";
        assert_eq!(substitute_path("%1", p), p);
        assert_eq!(substitute_path("%V", p), p);
        assert_eq!(substitute_path("%L", p), p);
        assert_eq!(substitute_path("%W", p), p);
        // Multiple placeholders in one template all get the path.
        assert_eq!(substitute_path("a %1 b %V c", p), format!("a {p} b {p} c"));
        // No placeholder → unchanged.
        assert_eq!(substitute_path("no placeholder", p), "no placeholder");
    }

    #[test]
    fn tokenizes_quoted_program_path_and_placeholder() {
        let t = tokenize_windows_command(r#""C:\Program Files\7-Zip\7zFM.exe" "%1""#);
        assert_eq!(
            t,
            vec![
                r"C:\Program Files\7-Zip\7zFM.exe".to_string(),
                "%1".to_string()
            ]
        );
    }

    #[test]
    fn tokenizes_rundll_style_command() {
        let t = tokenize_windows_command("rundll32.exe shell32.dll,OpenAs_RunDLL %1");
        assert_eq!(
            t,
            vec![
                "rundll32.exe".to_string(),
                "shell32.dll,OpenAs_RunDLL".to_string(),
                "%1".to_string(),
            ]
        );
    }

    #[test]
    fn placeholder_substitution_keeps_path_as_one_token() {
        // The injection-y name lands in a SINGLE argv token, no splitting on `&`.
        let path = r"C:\a & b\file.txt";
        let t = tokenize_windows_command(r#""app.exe" "%1""#);
        let args: Vec<String> = t.iter().skip(1).map(|s| substitute_path(s, path)).collect();
        assert_eq!(args, vec![r"C:\a & b\file.txt".to_string()]);
    }

    #[test]
    fn env_expansion_leaves_placeholders_and_unknown_vars() {
        // %1 has no closing %, so it survives expansion untouched.
        assert_eq!(expand_env_vars("foo %1 bar"), "foo %1 bar");
        // An unknown %VAR% is left literal rather than blanked.
        assert_eq!(
            expand_env_vars("%THEMIA_NOT_A_REAL_VAR% x"),
            "%THEMIA_NOT_A_REAL_VAR% x"
        );
    }

    // Regression: cloud-only thumbnail fallback must be skipped so the
    // image::open path can't force-hydrate a dehydrated file.
    #[test]
    fn attrs_are_cloud_only_detects_recall_and_offline_bits() {
        const RECALL_ON_OPEN: u32 = 0x0004_0000;
        const RECALL_ON_DATA_ACCESS: u32 = 0x0040_0000;
        const OFFLINE: u32 = 0x0000_1000;
        const ARCHIVE: u32 = 0x0000_0020;
        // Each cloud-only bit, alone or mixed with unrelated attrs, trips it.
        assert!(attrs_are_cloud_only(RECALL_ON_OPEN));
        assert!(attrs_are_cloud_only(RECALL_ON_DATA_ACCESS));
        assert!(attrs_are_cloud_only(OFFLINE));
        assert!(attrs_are_cloud_only(ARCHIVE | OFFLINE));
        // A normal, fully-local file is NOT cloud-only.
        assert!(!attrs_are_cloud_only(ARCHIVE));
        assert!(!attrs_are_cloud_only(0));
    }

    // Regression: fs_delete must not lose the whole recovery record on a
    // partial failure. With every path failing (all nonexistent), it returns
    // Err rather than an empty Ok, and the error names the offending paths.
    #[test]
    fn fs_delete_all_failures_reports_error() {
        let bogus = vec![
            r"C:\__themia_nonexistent__\a.txt".to_string(),
            r"C:\__themia_nonexistent__\b.txt".to_string(),
        ];
        let res = fs_delete(&bogus);
        assert!(
            res.is_err(),
            "all-nonexistent delete should be Err, got {res:?}"
        );
        let msg = res.unwrap_err();
        assert!(
            msg.contains("a.txt") || msg.contains("trash::delete"),
            "error should surface the failing path(s): {msg}"
        );
    }
}

// ---------------------------------------------------------------------------
// Cross-platform pure-helper tests (download filename derivation + label
// cleanup). These functions have no fs/network/clock/global-state side
// effects, so they run on any target.
// ---------------------------------------------------------------------------
#[cfg(test)]
mod pure_helper_tests {
    use super::*;

    #[test]
    fn mime_to_ext_maps_known_types_and_defaults_to_bin() {
        // Canonical types.
        assert_eq!(mime_to_ext("image/png"), "png");
        assert_eq!(mime_to_ext("image/jpeg"), "jpg");
        assert_eq!(mime_to_ext("image/jpg"), "jpg");
        assert_eq!(mime_to_ext("application/pdf"), "pdf");
        assert_eq!(mime_to_ext("image/x-icon"), "ico");
        assert_eq!(mime_to_ext("image/vnd.microsoft.icon"), "ico");
        // Parameters after `;` are stripped and surrounding whitespace trimmed.
        assert_eq!(mime_to_ext("text/html; charset=utf-8"), "html");
        assert_eq!(mime_to_ext("  image/png ; q=1 "), "png");
        // Unknown / empty / malformed → "bin", never a panic.
        assert_eq!(mime_to_ext(""), "bin");
        assert_eq!(mime_to_ext("application/octet-stream"), "bin");
        assert_eq!(mime_to_ext(";;;"), "bin");
        // Case-sensitive by design: an uppercased type is NOT matched.
        assert_eq!(mime_to_ext("IMAGE/PNG"), "bin");
    }

    #[test]
    fn urlish_decode_decodes_percent_escapes() {
        // Plain text passes through unchanged.
        assert_eq!(urlish_decode("plain.txt"), "plain.txt");
        // %20 → space, repeated escapes.
        assert_eq!(urlish_decode("my%20file%20name.txt"), "my file name.txt");
        // Mixed case hex digits are valid.
        assert_eq!(urlish_decode("a%2Fb%2fc"), "a/b/c");
        // %25 → literal percent.
        assert_eq!(urlish_decode("100%25.png"), "100%.png");
    }

    #[test]
    fn urlish_decode_handles_malformed_escapes_without_panic() {
        // Trailing lone `%` with no hex digits: missing bytes default to '0',
        // so "%" alone decodes "00" → NUL char. Must not panic (no slicing OOB).
        assert_eq!(urlish_decode("%"), "\u{0}");
        // `%` followed by a single digit: second digit defaults to '0' → "5_" no,
        // "5" then '0' = 0x50 = 'P'.
        assert_eq!(urlish_decode("a%5"), "aP");
        // Non-hex digits after `%` → from_str_radix fails → replaced with '_'.
        assert_eq!(urlish_decode("%zz"), "_");
        assert_eq!(urlish_decode("x%g1y"), "x_y");
    }

    #[test]
    fn strip_accelerator_removes_ampersand_prefixes() {
        // `&X` → X (the mnemonic underline marker).
        assert_eq!(strip_accelerator("&Open"), "Open");
        assert_eq!(strip_accelerator("E&xtract"), "Extract");
        // `&&` → a single literal `&`.
        assert_eq!(strip_accelerator("Fish && Chips"), "Fish & Chips");
        // No ampersand → unchanged.
        assert_eq!(strip_accelerator("Properties"), "Properties");
        // Trailing lone `&` (no following char) is dropped, no panic.
        assert_eq!(strip_accelerator("Edit&"), "Edit");
        // Empty input.
        assert_eq!(strip_accelerator(""), "");
        // Multibyte char after `&` is preserved intact (char-based iteration).
        assert_eq!(strip_accelerator("&Ä"), "Ä");
    }
}

// ---------------------------------------------------------------------------
// Icon batch concurrency: [S-64]
// ---------------------------------------------------------------------------
#[cfg(all(test, target_os = "windows"))]
mod icon_batch_tests {
    use super::*;

    // Characterization [S-64]: concurrent icon batches must all return a
    // complete result set. Pre-fix, simultaneous first-call SHGetImageList
    // raced shell32's lazy per-process image-list init and transiently failed
    // with E_OUTOFMEMORY (0x8007000E); the in-loop retry papered over it, so
    // this test was green by retry, not by construction; the race itself is
    // not reliably reproducible. Its job after the SHGETIMAGELIST_LOCK fix is
    // to pin completeness and to catch a deadlock regression in the lock path
    // (a deadlock hangs this test instead of passing).
    /// [S-17] `shell:` namespace URIs have no file-system path, so they only
    /// resolve an icon through the PIDL branch. Assert the real shell hands one
    /// back for the quick-set entries, and that two different namespace objects
    /// don't collapse onto the same generic glyph.
    #[cfg(target_os = "windows")]
    #[test]
    fn s17_shell_namespace_uris_resolve_a_real_icon() {
        let bin = get_file_icon("shell:RecycleBinFolder", 48).expect("recycle bin icon");
        let pc = get_file_icon("shell:MyComputerFolder", 48).expect("this pc icon");
        assert!(bin.starts_with("data:image/png;base64,"), "not a PNG data URL");
        assert!(pc.starts_with("data:image/png;base64,"), "not a PNG data URL");
        assert!(bin.len() > 200 && pc.len() > 200, "suspiciously empty icons");
        assert_ne!(bin, pc, "namespace icons collapsed onto one glyph");
    }

    /// [S-75] The live shell query must succeed on Windows: a wrong `cbSize`
    /// or a bad root-path argument fails the HRESULT, which is exactly the
    /// class of mistake no stubbed frontend test can catch. The bin's contents
    /// are whatever this machine holds, so only the invariants are asserted.
    #[cfg(target_os = "windows")]
    #[test]
    fn s75_recycle_bin_status_queries_the_shell() {
        let st = recycle_bin_status().expect("SHQueryRecycleBin failed");
        // Size without a single item means the struct was misread. (The reverse
        // is legal: a bin holding only empty files reports items with 0 bytes.)
        assert!(
            st.bytes == 0 || st.items > 0,
            "inconsistent recycle bin reading: {st:?}"
        );
    }

    #[test]
    fn s64_concurrent_icon_batches_all_succeed() {
        let paths: Vec<String> = vec![
            r"C:\Windows\explorer.exe".into(),
            r"C:\Windows\System32\cmd.exe".into(),
            r"C:\Windows\System32\kernel32.dll".into(),
            r"C:\Windows".into(),
        ];
        let handles: Vec<_> = (0..8)
            .map(|_| {
                let p = paths.clone();
                std::thread::spawn(move || get_file_icons_batch(p, 32))
            })
            .collect();
        for h in handles {
            let out = h.join().expect("icon batch thread panicked");
            assert_eq!(out.len(), 4, "batch returned incomplete results: {out:?}");
        }
    }
}
