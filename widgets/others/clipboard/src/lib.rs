//! Event-driven clipboard-text watcher.
//!
//! A message-only window registers `AddClipboardFormatListener`; on each
//! `WM_CLIPBOARDUPDATE` it reads the Unicode clipboard text and emits
//! `clipboard://update` with the new string. No polling. The WndProc is wrapped
//! in `catch_unwind` (a panic must never cross the Win32 FFI boundary). History
//! itself is kept on the frontend (this layer just streams new entries).

use tauri::AppHandle;

#[cfg(target_os = "windows")]
mod win {
    use super::*;
    use std::sync::atomic::{AtomicBool, Ordering};
    use std::sync::Mutex;
    use tauri::Emitter;
    use windows::core::PCWSTR;
    use windows::Win32::Foundation::{GlobalFree, HANDLE, HGLOBAL, HINSTANCE, HWND, LPARAM, LRESULT, WPARAM};
    use windows::Win32::System::DataExchange::{
        AddClipboardFormatListener, CloseClipboard, EmptyClipboard, GetClipboardData,
        OpenClipboard, RemoveClipboardFormatListener, SetClipboardData,
    };
    use windows::Win32::System::LibraryLoader::GetModuleHandleW;
    use windows::Win32::System::Memory::{GlobalAlloc, GlobalLock, GlobalUnlock, GMEM_MOVEABLE};
    use windows::Win32::UI::WindowsAndMessaging::{
        CreateWindowExW, DefWindowProcW, DispatchMessageW, GetMessageW, PostQuitMessage,
        RegisterClassW, TranslateMessage, MSG, WINDOW_EX_STYLE, WINDOW_STYLE, WM_CLIPBOARDUPDATE,
        WM_DESTROY, WNDCLASSW,
    };
    use windows::Win32::UI::WindowsAndMessaging::HWND_MESSAGE;

    const CF_UNICODETEXT: u32 = 13;

    static RUNNING: AtomicBool = AtomicBool::new(false);
    static APP: Mutex<Option<AppHandle>> = Mutex::new(None);

    fn wide(s: &str) -> Vec<u16> {
        s.encode_utf16().chain(std::iter::once(0)).collect()
    }

    unsafe fn read_text() -> Option<String> {
        if OpenClipboard(HWND(std::ptr::null_mut())).is_err() {
            return None;
        }
        let result = (|| -> Option<String> {
            let handle = GetClipboardData(CF_UNICODETEXT).ok()?;
            if handle.0.is_null() {
                return None;
            }
            let hglobal = HGLOBAL(handle.0);
            let ptr = GlobalLock(hglobal) as *const u16;
            if ptr.is_null() {
                return None;
            }
            let mut len = 0usize;
            while *ptr.add(len) != 0 {
                len += 1;
            }
            let s = String::from_utf16_lossy(std::slice::from_raw_parts(ptr, len));
            let _ = GlobalUnlock(hglobal);
            Some(s)
        })();
        let _ = CloseClipboard();
        result
    }

    unsafe fn write_text(text: &str) -> Result<(), String> {
        let buf = wide(text);
        let bytes = buf.len() * std::mem::size_of::<u16>();
        OpenClipboard(HWND(std::ptr::null_mut())).map_err(|e| e.to_string())?;
        let res = (|| -> Result<(), String> {
            EmptyClipboard().map_err(|e| e.to_string())?;
            let hmem = GlobalAlloc(GMEM_MOVEABLE, bytes).map_err(|e| e.to_string())?;
            let dst = GlobalLock(hmem) as *mut u16;
            if dst.is_null() {
                // R1: we still own hmem (ownership only transfers to the clipboard
                // on a successful SetClipboardData), so free it before bailing.
                let _ = GlobalFree(hmem);
                return Err("GlobalLock failed".into());
            }
            std::ptr::copy_nonoverlapping(buf.as_ptr(), dst, buf.len());
            let _ = GlobalUnlock(hmem);
            // Ownership of hmem transfers to the clipboard ONLY on success. If
            // SetClipboardData fails, we still own it and must free it (R1).
            if let Err(e) = SetClipboardData(CF_UNICODETEXT, HANDLE(hmem.0)) {
                let _ = GlobalFree(hmem);
                return Err(e.to_string());
            }
            Ok(())
        })();
        let _ = CloseClipboard();
        res
    }

    fn emit_current() {
        if let Some(text) = unsafe { read_text() } {
            if !text.is_empty() {
                let guard = APP.lock().unwrap_or_else(|e| e.into_inner());
                if let Some(app) = guard.as_ref() {
                    let _ = app.emit("clipboard://update", text);
                }
            }
        }
    }

    unsafe fn handle_msg(msg: u32) -> Option<LRESULT> {
        match msg {
            WM_CLIPBOARDUPDATE => {
                emit_current();
                Some(LRESULT(0))
            }
            WM_DESTROY => {
                PostQuitMessage(0);
                Some(LRESULT(0))
            }
            _ => None,
        }
    }

    unsafe extern "system" fn wndproc(
        hwnd: HWND,
        msg: u32,
        wparam: WPARAM,
        lparam: LPARAM,
    ) -> LRESULT {
        // A panic must never unwind across the Win32 callback boundary.
        let handled = std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| unsafe {
            handle_msg(msg)
        }))
        .unwrap_or(None);
        match handled {
            Some(r) => r,
            None => unsafe { DefWindowProcW(hwnd, msg, wparam, lparam) },
        }
    }

    unsafe fn run_listener() {
        let hmodule = GetModuleHandleW(None).unwrap_or_default();
        let hinstance = HINSTANCE(hmodule.0);
        let class_name = wide("ThemiaClipboardWatcher");

        let wc = WNDCLASSW {
            lpfnWndProc: Some(wndproc),
            hInstance: hinstance,
            lpszClassName: PCWSTR(class_name.as_ptr()),
            ..Default::default()
        };
        // Ignore "class already exists" on a re-register.
        RegisterClassW(&wc);

        let hwnd = match CreateWindowExW(
            WINDOW_EX_STYLE(0),
            PCWSTR(class_name.as_ptr()),
            PCWSTR::null(),
            WINDOW_STYLE(0),
            0,
            0,
            0,
            0,
            HWND_MESSAGE,
            None,
            hinstance,
            None,
        ) {
            Ok(h) if !h.0.is_null() => h,
            _ => return,
        };

        if AddClipboardFormatListener(hwnd).is_err() {
            return;
        }

        emit_current(); // initial snapshot

        let mut msg = MSG::default();
        while GetMessageW(&mut msg, hwnd, 0, 0).as_bool() {
            let _ = TranslateMessage(&msg);
            DispatchMessageW(&msg);
        }
        let _ = RemoveClipboardFormatListener(hwnd);
    }

    pub fn start(app: AppHandle) -> Result<(), String> {
        if RUNNING.swap(true, Ordering::SeqCst) {
            // Already running, so re-emit so a freshly mounted widget gets a value.
            *APP.lock().unwrap_or_else(|e| e.into_inner()) = Some(app);
            emit_current();
            return Ok(());
        }
        *APP.lock().unwrap_or_else(|e| e.into_inner()) = Some(app);
        std::thread::spawn(|| {
            let _ = std::panic::catch_unwind(|| unsafe { run_listener() });
            RUNNING.store(false, Ordering::SeqCst);
        });
        Ok(())
    }

    pub fn set_text(text: &str) -> Result<(), String> {
        unsafe { write_text(text) }
    }
}

#[cfg(target_os = "windows")]
pub fn start(app: AppHandle) -> Result<(), String> {
    win::start(app)
}

#[cfg(target_os = "windows")]
pub fn set_text(text: String) -> Result<(), String> {
    win::set_text(&text)
}

#[cfg(not(target_os = "windows"))]
pub fn start(_app: AppHandle) -> Result<(), String> {
    Err("clipboard history is only supported on Windows".into())
}

#[cfg(not(target_os = "windows"))]
pub fn set_text(_text: String) -> Result<(), String> {
    Err("clipboard history is only supported on Windows".into())
}
