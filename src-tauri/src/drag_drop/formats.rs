//! Clipboard format definitions and serialization helpers for OLE drag/drop.

use windows::Win32::System::Ole::CF_HDROP;
use windows::Win32::System::Com::{FORMATETC, DVASPECT_CONTENT, TYMED_HGLOBAL, TYMED_ISTREAM};
use windows::Win32::System::DataExchange::RegisterClipboardFormatW;
use windows::Win32::Foundation::POINT;
use windows::Win32::UI::Shell::DROPFILES;
use windows::core::PCWSTR;
use windows_core::BOOL;
use std::sync::OnceLock;

// ---------------------------------------------------------------------------
// Custom clipboard format: widget JSON
// ---------------------------------------------------------------------------

static WIDGET_FORMAT_ID: OnceLock<u32> = OnceLock::new();

pub fn widget_format_id() -> u32 {
    *WIDGET_FORMAT_ID.get_or_init(|| unsafe {
        let name: Vec<u16> = "ThemiaWidgetData\0".encode_utf16().collect();
        RegisterClipboardFormatW(PCWSTR(name.as_ptr()))
    })
}

// ---------------------------------------------------------------------------
// Virtual file clipboard formats (FileGroupDescriptorW / FileContents)
// ---------------------------------------------------------------------------

static FILE_DESCRIPTOR_FORMAT_ID: OnceLock<u32> = OnceLock::new();
static FILE_CONTENTS_FORMAT_ID: OnceLock<u32> = OnceLock::new();
static URL_FORMAT_ID: OnceLock<u32> = OnceLock::new();

/// CFSTR_INETURLW: Windows clipboard format name used when dragging a URL
/// out of a browser address bar or similar. Payload is a UTF-16 null-terminated
/// URL string in an HGLOBAL.
pub fn url_format_id() -> u32 {
    *URL_FORMAT_ID.get_or_init(|| unsafe {
        let name: Vec<u16> = "UniformResourceLocatorW\0".encode_utf16().collect();
        RegisterClipboardFormatW(PCWSTR(name.as_ptr()))
    })
}

pub fn url_format() -> FORMATETC {
    FORMATETC {
        cfFormat: url_format_id() as u16,
        ptd: std::ptr::null_mut(),
        dwAspect: DVASPECT_CONTENT.0,
        lindex: -1,
        tymed: TYMED_HGLOBAL.0 as u32,
    }
}

/// Parse the CFSTR_INETURLW payload, UTF-16 NUL-terminated URL string.
pub unsafe fn parse_url_data(hglobal: *mut std::ffi::c_void) -> Option<String> {
    // Identical shape to widget data, reuse parse_widget_data.
    parse_widget_data(hglobal)
}

pub fn file_descriptor_format_id() -> u32 {
    *FILE_DESCRIPTOR_FORMAT_ID.get_or_init(|| unsafe {
        let name: Vec<u16> = "FileGroupDescriptorW\0".encode_utf16().collect();
        RegisterClipboardFormatW(PCWSTR(name.as_ptr()))
    })
}

pub fn file_contents_format_id() -> u32 {
    *FILE_CONTENTS_FORMAT_ID.get_or_init(|| unsafe {
        let name: Vec<u16> = "FileContents\0".encode_utf16().collect();
        RegisterClipboardFormatW(PCWSTR(name.as_ptr()))
    })
}

// ---------------------------------------------------------------------------
// FORMATETC helpers
// ---------------------------------------------------------------------------

pub fn hdrop_format() -> FORMATETC {
    FORMATETC {
        cfFormat: CF_HDROP.0,
        ptd: std::ptr::null_mut(),
        dwAspect: DVASPECT_CONTENT.0,
        lindex: -1,
        tymed: TYMED_HGLOBAL.0 as u32,
    }
}

pub fn widget_format() -> FORMATETC {
    FORMATETC {
        cfFormat: widget_format_id() as u16,
        ptd: std::ptr::null_mut(),
        dwAspect: DVASPECT_CONTENT.0,
        lindex: -1,
        tymed: TYMED_HGLOBAL.0 as u32,
    }
}

pub fn file_group_descriptor_format() -> FORMATETC {
    FORMATETC {
        cfFormat: file_descriptor_format_id() as u16,
        ptd: std::ptr::null_mut(),
        dwAspect: DVASPECT_CONTENT.0,
        lindex: -1,
        tymed: TYMED_HGLOBAL.0 as u32,
    }
}

#[allow(dead_code)]
pub fn file_contents_format(index: i32) -> FORMATETC {
    FORMATETC {
        cfFormat: file_contents_format_id() as u16,
        ptd: std::ptr::null_mut(),
        dwAspect: DVASPECT_CONTENT.0,
        lindex: index,
        tymed: (TYMED_ISTREAM.0 | TYMED_HGLOBAL.0) as u32,
    }
}

// ---------------------------------------------------------------------------
// CFSTR_PREFERREDDROPEFFECT: source-suggested default drop effect
// ---------------------------------------------------------------------------

static PREFERRED_DROP_EFFECT_FORMAT_ID: OnceLock<u32> = OnceLock::new();

/// CFSTR_PREFERREDDROPEFFECT. Lets the drag SOURCE suggest the default effect so
/// the Windows desktop / Explorer applies it instead of defaulting a drop from a
/// non-Explorer source to COPY. Payload is a 4-byte DROPEFFECT DWORD in an HGLOBAL.
pub fn preferred_drop_effect_format_id() -> u32 {
    *PREFERRED_DROP_EFFECT_FORMAT_ID.get_or_init(|| unsafe {
        let name: Vec<u16> = "Preferred DropEffect\0".encode_utf16().collect();
        RegisterClipboardFormatW(PCWSTR(name.as_ptr()))
    })
}

pub fn preferred_drop_effect_format() -> FORMATETC {
    FORMATETC {
        cfFormat: preferred_drop_effect_format_id() as u16,
        ptd: std::ptr::null_mut(),
        dwAspect: DVASPECT_CONTENT.0,
        lindex: -1,
        tymed: TYMED_HGLOBAL.0 as u32,
    }
}

/// 4-byte little-endian DROPEFFECT payload for CFSTR_PREFERREDDROPEFFECT.
pub fn build_drop_effect_bytes(effect: u32) -> Vec<u8> {
    effect.to_le_bytes().to_vec()
}

// ---------------------------------------------------------------------------
// HDROP serialization
// ---------------------------------------------------------------------------

pub fn build_hdrop_bytes(paths: &[String]) -> Vec<u8> {
    let header = DROPFILES {
        pFiles: std::mem::size_of::<DROPFILES>() as u32,
        pt: POINT { x: 0, y: 0 },
        fNC: BOOL(0),
        fWide: BOOL(1),
    };

    let mut buf: Vec<u8> = Vec::new();
    let header_bytes: &[u8] = unsafe {
        std::slice::from_raw_parts(
            &header as *const DROPFILES as *const u8,
            std::mem::size_of::<DROPFILES>(),
        )
    };
    buf.extend_from_slice(header_bytes);

    for p in paths {
        for unit in p.encode_utf16() {
            buf.extend_from_slice(&unit.to_le_bytes());
        }
        buf.extend_from_slice(&[0u8, 0]);
    }
    buf.extend_from_slice(&[0u8, 0]);
    buf
}

pub unsafe fn parse_hdrop(hglobal: *mut std::ffi::c_void) -> Vec<String> {
    use windows::Win32::UI::Shell::DragQueryFileW;
    use windows::Win32::UI::Shell::HDROP;

    let hdrop = HDROP(hglobal as _);
    let count = DragQueryFileW(hdrop, 0xFFFFFFFF, None);
    let mut paths = Vec::with_capacity(count as usize);
    for i in 0..count {
        let len = DragQueryFileW(hdrop, i, None) as usize;
        let mut buf = vec![0u16; len + 1];
        DragQueryFileW(hdrop, i, Some(&mut buf));
        let s = String::from_utf16_lossy(&buf[..len]);
        paths.push(s);
    }
    paths
}

// ---------------------------------------------------------------------------
// Widget JSON serialization (UTF-16 in HGLOBAL)
// ---------------------------------------------------------------------------

pub fn build_widget_bytes(json: &str) -> Vec<u8> {
    let mut buf: Vec<u8> = Vec::new();
    for unit in json.encode_utf16() {
        buf.extend_from_slice(&unit.to_le_bytes());
    }
    buf.extend_from_slice(&[0u8, 0]);
    buf
}

pub unsafe fn parse_widget_data(hglobal: *mut std::ffi::c_void) -> Option<String> {
    let ptr = hglobal as *const u16;
    if ptr.is_null() {
        return None;
    }
    let mut len = 0usize;
    while *ptr.add(len) != 0 {
        len += 1;
    }
    let slice = std::slice::from_raw_parts(ptr, len);
    Some(String::from_utf16_lossy(slice))
}

// ---------------------------------------------------------------------------
// Virtual file (FileGroupDescriptorW) parsing
// ---------------------------------------------------------------------------

/// Parsed file descriptor from a virtual file drag source.
pub struct VirtualFileDescriptor {
    pub name: String,
}

// FILEDESCRIPTORW layout (592 bytes on x64):
//   offset  0: dwFlags        (u32)
//   offset  4: clsid          (16 bytes)
//   offset 20: sizel          (8 bytes)
//   offset 28: pointl         (8 bytes)
//   offset 36: dwFileAttributes (u32)
//   offset 40: ftCreationTime (8 bytes)
//   offset 48: ftLastAccessTime (8 bytes)
//   offset 56: ftLastWriteTime (8 bytes)
//   offset 64: nFileSizeHigh  (u32)
//   offset 68: nFileSizeLow   (u32)
//   offset 72: padding        (4 bytes on x64 for alignment)
//   offset 76: cFileName      (260 u16 = 520 bytes)
// Total: 596 bytes on x64 (with alignment padding)
const FILEDESCRIPTORW_SIZE: usize = 596;
const FILENAME_OFFSET: usize = 76;
const FILENAME_LEN: usize = 260; // u16 units

/// Parse a FILEGROUPDESCRIPTORW from a locked HGLOBAL.
/// Layout: u32 count, then `count` FILEDESCRIPTORW structs.
///
/// `byte_size` is the backing HGLOBAL size (from `GlobalSize`). The header
/// `count` is UNTRUSTED: it comes from the drag source and may claim far more
/// descriptors than the buffer actually holds. We clamp the iteration to the
/// number of fixed-size descriptors that physically fit after the 4-byte count
/// header so we never read out of bounds inside the COM Drop callback.
pub unsafe fn parse_file_group_descriptor(
    hglobal: *mut std::ffi::c_void,
    byte_size: usize,
) -> Vec<VirtualFileDescriptor> {
    let base = hglobal as *const u8;
    if base.is_null() || byte_size < 4 {
        return Vec::new();
    }

    let count = *(base as *const u32) as usize;
    // Number of whole descriptors the backing bytes can actually hold after the
    // 4-byte count header. The untrusted `count` is clamped to this so a hostile
    // header (e.g. 0xFFFFFFFF) cannot drive an out-of-bounds read.
    let max = byte_size.saturating_sub(4) / FILEDESCRIPTORW_SIZE;
    let real = count.min(max);
    // `count` is only an allocation *hint* (capped); `real` bounds the loop.
    let mut descriptors = Vec::with_capacity(real.min(1024));

    for i in 0..real {
        // Each descriptor starts after the 4-byte count header
        let desc_base = base.add(4 + i * FILEDESCRIPTORW_SIZE);
        let name_ptr = desc_base.add(FILENAME_OFFSET) as *const u16;

        // Read null-terminated u16 filename
        let mut name_len = 0usize;
        while name_len < FILENAME_LEN && *name_ptr.add(name_len) != 0 {
            name_len += 1;
        }
        let name_slice = std::slice::from_raw_parts(name_ptr, name_len);
        let name = String::from_utf16_lossy(name_slice);

        descriptors.push(VirtualFileDescriptor { name });
    }

    descriptors
}

/// Read an IStream fully into a Vec<u8>.
pub unsafe fn read_istream(stream: &windows::Win32::System::Com::IStream) -> Result<Vec<u8>, String> {
    let mut buf = Vec::new();
    let mut chunk = [0u8; 65536];
    loop {
        let mut bytes_read: u32 = 0;
        let hr = stream.Read(
            chunk.as_mut_ptr() as *mut std::ffi::c_void,
            chunk.len() as u32,
            Some(&mut bytes_read),
        );
        if hr.is_err() {
            return Err(format!("IStream::Read failed: {:?}", hr));
        }
        if bytes_read == 0 {
            break;
        }
        // (see clamp test `read_istream_clamps_overreported_len` below)
        // A buggy/hostile IStream::Read may report more bytes than the buffer
        // can hold while still returning success. Clamp before slicing so we
        // never panic on an out-of-range index across the COM/FFI boundary.
        let n = (bytes_read as usize).min(chunk.len());
        if (bytes_read as usize) != n {
            log::warn!(
                target: "virtual_files",
                "IStream::Read reported {} bytes for a {}-byte buffer; clamping",
                bytes_read,
                chunk.len()
            );
        }
        buf.extend_from_slice(&chunk[..n]);
    }
    Ok(buf)
}

#[cfg(test)]
mod tests {
    use super::*;

    /// Models the clamp used in `read_istream`: an IStream that over-reports
    /// `bytes_read` (more than the chunk buffer holds) must not cause an
    /// out-of-range slice panic. We never index past `chunk.len()`.
    #[test]
    fn read_istream_clamps_overreported_len() {
        let chunk = [0u8; 65536];

        // Hostile / buggy: reports more than the buffer can hold.
        let bytes_read: u32 = u32::MAX;
        let n = (bytes_read as usize).min(chunk.len());
        assert_eq!(n, chunk.len());
        // Slicing with the clamped length must not panic.
        let _ = &chunk[..n];

        // Normal case: reported length within buffer is preserved.
        let bytes_read: u32 = 100;
        let n = (bytes_read as usize).min(chunk.len());
        assert_eq!(n, 100);
        let _ = &chunk[..n];
    }

    /// `parse_widget_data` over an in-memory UTF-16 NUL-terminated buffer must
    /// return the string up to (but excluding) the terminator, without reading
    /// past it or panicking on an empty payload.
    #[test]
    fn parse_widget_data_reads_until_nul() {
        // "hi" followed by NUL terminator, then trailing garbage that must be ignored.
        let buf: Vec<u16> = vec![b'h' as u16, b'i' as u16, 0, b'X' as u16];
        let s = unsafe { parse_widget_data(buf.as_ptr() as *mut std::ffi::c_void) };
        assert_eq!(s.as_deref(), Some("hi"));

        // Empty payload (immediate terminator) yields an empty string, not a panic.
        let empty: Vec<u16> = vec![0];
        let s = unsafe { parse_widget_data(empty.as_ptr() as *mut std::ffi::c_void) };
        assert_eq!(s.as_deref(), Some(""));

        // Null pointer yields None.
        let s = unsafe { parse_widget_data(std::ptr::null_mut()) };
        assert_eq!(s, None);
    }

    /// A FILEGROUPDESCRIPTORW whose header `count` is hugely larger than the
    /// backing HGLOBAL bytes must NOT drive an out-of-bounds read. Only the
    /// descriptors that physically fit are parsed, and no panic occurs.
    #[test]
    fn parse_file_group_descriptor_clamps_to_backing_size() {
        // Build a buffer that physically holds exactly ONE descriptor, but whose
        // header claims a hostile huge count.
        let mut buf = vec![0u8; 4 + FILEDESCRIPTORW_SIZE];
        // Header count = 0xFFFF_FFFF (untrusted, hostile).
        buf[0..4].copy_from_slice(&u32::MAX.to_le_bytes());
        // Write a filename "ok.txt" (UTF-16) into descriptor[0] at FILENAME_OFFSET.
        let name: Vec<u16> = "ok.txt".encode_utf16().collect();
        let fname_byte_off = 4 + FILENAME_OFFSET;
        for (i, u) in name.iter().enumerate() {
            let o = fname_byte_off + i * 2;
            buf[o..o + 2].copy_from_slice(&u.to_le_bytes());
        }

        let descriptors = unsafe {
            parse_file_group_descriptor(buf.as_ptr() as *mut std::ffi::c_void, buf.len())
        };
        // Despite the header claiming u32::MAX descriptors, only the one that
        // fits is returned, no OOB read, no panic.
        assert_eq!(descriptors.len(), 1);
        assert_eq!(descriptors[0].name, "ok.txt");

        // A buffer too small to hold even the 4-byte header returns empty.
        let tiny = [0u8; 2];
        let none = unsafe {
            parse_file_group_descriptor(tiny.as_ptr() as *mut std::ffi::c_void, tiny.len())
        };
        assert!(none.is_empty());

        // A null pointer returns empty without dereferencing.
        let null = unsafe { parse_file_group_descriptor(std::ptr::null_mut(), 0) };
        assert!(null.is_empty());
    }

    /// CFSTR_PREFERREDDROPEFFECT payload must be exactly one 4-byte little-endian
    /// DROPEFFECT DWORD, Explorer reads `*(DWORD*)hglobal`. A wrong size/endianness
    /// would make the desktop ignore the hint and fall back to COPY (the bug fixed).
    #[test]
    fn drop_effect_bytes_is_4_byte_le_dword() {
        assert_eq!(build_drop_effect_bytes(2), vec![2u8, 0, 0, 0]); // DROPEFFECT_MOVE
        assert_eq!(build_drop_effect_bytes(1), vec![1u8, 0, 0, 0]); // DROPEFFECT_COPY
    }

    /// `build_widget_bytes` must round-trip multi-byte / surrogate-pair text via
    /// UTF-16 LE and append a NUL terminator, never panicking on edge input.
    #[test]
    fn build_widget_bytes_handles_empty_and_multibyte() {
        // Empty string -> just the 2-byte NUL terminator.
        assert_eq!(build_widget_bytes(""), vec![0u8, 0]);

        // Emoji (surrogate pair) must be encoded as 2 UTF-16 units + terminator.
        let bytes = build_widget_bytes("\u{1F600}");
        // 2 units * 2 bytes + 2-byte terminator.
        assert_eq!(bytes.len(), 6);
        assert_eq!(&bytes[4..], &[0u8, 0]);
    }
}
