use std::io::Read;
use std::path::Path;
use base64::{engine::general_purpose::STANDARD, Engine};

/// Max on-disk size of an image we are willing to read + base64-encode for the
/// webview. Base64 expands payloads 4/3x, so an uncapped read of a huge file
/// would OOM the renderer. 32 MB is well above any sane thumbnail/photo.
const MAX_IMAGE_BYTES: u64 = 32 * 1024 * 1024;

/// True if `len` (the file's reported byte length) is within the read cap.
/// Pure helper so the size policy is unit-testable without touching the fs.
fn within_size_cap(len: u64) -> bool {
    len <= MAX_IMAGE_BYTES
}

// ── Local folder scanning ────────────────────────────────────────────────────

const IMAGE_EXTENSIONS: &[&str] = &[
    "jpg", "jpeg", "png", "gif", "bmp", "webp", "svg", "ico", "tiff", "tif", "avif",
];

fn is_image(path: &Path) -> bool {
    path.extension()
        .and_then(|e| e.to_str())
        .map(|e| IMAGE_EXTENSIONS.contains(&e.to_ascii_lowercase().as_str()))
        .unwrap_or(false)
}

fn collect_images(dir: &Path, recursive: bool, out: &mut Vec<String>) {
    let entries = match std::fs::read_dir(dir) {
        Ok(e) => e,
        // ignore: best-effort scan, an unreadable/removed dir contributes no images, not corruption
        Err(_) => return,
    };
    // ignore: best-effort scan, skip entries that error mid-iteration (transient/permission)
    for entry in entries.flatten() {
        let ft = match entry.file_type() {
            Ok(ft) => ft,
            // ignore: best-effort scan, an entry whose type can't be read is skipped
            Err(_) => continue,
        };
        if ft.is_dir() {
            if recursive {
                collect_images(&entry.path(), true, out);
            }
        } else if (ft.is_file() || ft.is_symlink()) && is_image(&entry.path()) {
            if let Some(s) = entry.path().to_str() {
                out.push(s.to_string());
            }
        }
    }
}

pub fn scan_images(path: &str, recursive: bool) -> Vec<String> {
    let dir = Path::new(path);
    if !dir.is_dir() {
        return Vec::new();
    }
    let mut images = Vec::new();
    collect_images(dir, recursive, &mut images);
    images.sort_by_key(|a| a.to_ascii_lowercase());
    images
}

// ── Async image reading ─────────────────────────────────────────────────────

fn mime_for_ext(ext: &str) -> &'static str {
    match ext {
        "jpg" | "jpeg" => "image/jpeg",
        "png" => "image/png",
        "gif" => "image/gif",
        "bmp" => "image/bmp",
        "webp" => "image/webp",
        "svg" => "image/svg+xml",
        "ico" => "image/x-icon",
        "tiff" | "tif" => "image/tiff",
        "avif" => "image/avif",
        _ => "application/octet-stream",
    }
}

pub fn read_image_base64(path: &str) -> Result<String, String> {
    let p = Path::new(path);
    let ext = p
        .extension()
        .and_then(|e| e.to_str())
        .map(|e| e.to_ascii_lowercase())
        .unwrap_or_default();
    let mime = mime_for_ext(&ext);

    let file = std::fs::File::open(p).map_err(|e| e.to_string())?;
    // Reject oversized files up front via the stat'd length so we never buffer
    // a huge file (the 4/3x base64 expansion would OOM the webview).
    let len = file.metadata().map_err(|e| e.to_string())?.len();
    if !within_size_cap(len) {
        return Err(format!(
            "image too large ({len} bytes, max {MAX_IMAGE_BYTES})"
        ));
    }

    // Defense in depth: bound the actual read too, so a file that grows or
    // misreports its length (e.g. a special/sparse file) can't blow past the
    // cap. Read one extra byte to detect overflow without buffering it all.
    let mut bytes = Vec::with_capacity(len as usize);
    file.take(MAX_IMAGE_BYTES + 1)
        .read_to_end(&mut bytes)
        .map_err(|e| e.to_string())?;
    if !within_size_cap(bytes.len() as u64) {
        return Err(format!("image too large (exceeds max {MAX_IMAGE_BYTES})"));
    }

    let b64 = STANDARD.encode(&bytes);
    Ok(format!("data:{};base64,{}", mime, b64))
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn is_image_handles_no_extension_and_unicode() {
        // no extension, dotfile, trailing dot, and a multi-byte unicode name must not panic
        assert!(!is_image(Path::new("/tmp/noext")));
        assert!(!is_image(Path::new("/tmp/.hidden")));
        assert!(!is_image(Path::new("/tmp/файл")));
        assert!(!is_image(Path::new("")));
        assert!(is_image(Path::new("/tmp/Photo.JPG")));
        assert!(is_image(Path::new("/tmp/p.png")));
    }

    #[test]
    fn mime_for_ext_unknown_falls_back() {
        assert_eq!(mime_for_ext("png"), "image/png");
        assert_eq!(mime_for_ext(""), "application/octet-stream");
        assert_eq!(mime_for_ext("xyz"), "application/octet-stream");
    }

    #[test]
    fn read_image_base64_missing_file_is_err_not_panic() {
        let r = read_image_base64("/this/path/should/not/exist/zzz.png");
        assert!(r.is_err());
    }

    #[test]
    fn within_size_cap_enforces_max_image_bytes() {
        assert!(within_size_cap(0));
        assert!(within_size_cap(1));
        assert!(within_size_cap(MAX_IMAGE_BYTES - 1));
        assert!(within_size_cap(MAX_IMAGE_BYTES)); // boundary: exactly the cap is allowed
        assert!(!within_size_cap(MAX_IMAGE_BYTES + 1)); // one over is rejected
        assert!(!within_size_cap(u64::MAX));
    }

    #[test]
    fn read_image_base64_rejects_oversized_file() {
        // A file larger than the cap must be rejected (skipped), not buffered.
        let mut path = std::env::temp_dir();
        path.push(format!(
            "themia_gallery_oversized_{}.png",
            std::process::id()
        ));
        // Write cap + 1 zero bytes.
        std::fs::write(&path, vec![0u8; (MAX_IMAGE_BYTES + 1) as usize])
            .expect("write temp oversized file");
        let r = read_image_base64(path.to_str().unwrap());
        let _ = std::fs::remove_file(&path); // ignore: best-effort cleanup
        assert!(r.is_err(), "oversized file must be rejected");
    }

    #[test]
    fn read_image_base64_accepts_small_file() {
        let mut path = std::env::temp_dir();
        path.push(format!("themia_gallery_small_{}.png", std::process::id()));
        std::fs::write(&path, b"\x89PNG\r\n\x1a\n").expect("write temp small file");
        let r = read_image_base64(path.to_str().unwrap());
        let _ = std::fs::remove_file(&path); // ignore: best-effort cleanup
        let s = r.expect("small file must encode");
        assert!(s.starts_with("data:image/png;base64,"));
    }

    #[test]
    fn scan_images_nonexistent_dir_returns_empty() {
        let v = scan_images("/this/path/should/not/exist", true);
        assert!(v.is_empty());
        // a path that is a file (not a dir) also yields empty without panicking
        let v2 = scan_images("", false);
        assert!(v2.is_empty());
    }
}
